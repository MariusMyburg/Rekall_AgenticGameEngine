using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Modules.Hosting;

public sealed record RekallAgeModuleHostLoadedAssembly(string ModuleName, Assembly Assembly);

public static class RekallAgeModuleHostVerifiedAssemblyLoader
{
    public static IReadOnlyList<RekallAgeModuleHostLoadedAssembly> Load(string loadPlanPath)
    {
        var path = Path.GetFullPath(loadPlanPath);
        if (!File.Exists(path) || IsReparse(path) || new FileInfo(path).Length > 1024 * 1024)
        {
            throw Rejected("Module-host load plan is missing, uses a reparse point, or exceeds its bound.", path);
        }

        RekallAgeModuleHostLoadPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<RekallAgeModuleHostLoadPlan>(
                File.ReadAllBytes(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    MaxDepth = RekallAgeModuleHostProtocol.MaximumJsonDepth,
                    PropertyNameCaseInsensitive = false
                });
        }
        catch (JsonException ex)
        {
            throw Rejected("Module-host load plan is malformed.", path, ex);
        }

        if (plan is null
            || plan.SchemaVersion != 1
            || plan.ProtocolVersion != RekallAgeModuleHostProtocol.Version
            || !string.Equals(plan.TrustPosture, RekallAgeModuleTrustPostures.WindowsAppContainerRestricted, StringComparison.Ordinal)
            || plan.Modules is null
            || plan.Modules.Count > RekallAgeModuleHostProtocol.MaximumModules)
        {
            throw Rejected("Module-host load plan is incompatible with this worker.", path);
        }

        var root = Path.GetDirectoryName(path)!;
        var result = new List<RekallAgeModuleHostLoadedAssembly>(plan.Modules.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in plan.Modules.OrderBy(item => item.ModuleName, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(module.ModuleName)
                || !names.Add(module.ModuleName)
                || (module.RelativeDirectory != "."
                    && !RekallAgeModuleHostStager.IsSafeRelative(module.RelativeDirectory))
                || !IsSafeLeaf(module.MainAssembly)
                || module.Artifacts is null
                || module.Artifacts.Count == 0
                || module.Artifacts.Count > 256)
            {
                throw Rejected("Module-host load plan contains an invalid module entry.", path);
            }

            var moduleRoot = Path.GetFullPath(Path.Combine(root, module.RelativeDirectory));
            if (!IsConfined(root, moduleRoot) || !Directory.Exists(moduleRoot) || IsReparse(moduleRoot))
            {
                throw Rejected("Module-host module directory escapes its confined plan root.", moduleRoot);
            }

            var inventory = ValidateArtifacts(moduleRoot, module, path);
            var mainPath = Path.GetFullPath(Path.Combine(moduleRoot, module.MainAssembly));
            if (!inventory.TryGetValue(mainPath, out var mainArtifact))
            {
                throw Rejected("Module-host main assembly is absent from its verified inventory.", mainPath);
            }

            var context = new VerifiedLoadContext(mainPath, moduleRoot, inventory);
            using var stream = OpenVerified(mainPath, mainArtifact);
            result.Add(new RekallAgeModuleHostLoadedAssembly(module.ModuleName, context.LoadFromStream(stream)));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, RekallAgeModuleArtifactIntegrity> ValidateArtifacts(
        string moduleRoot,
        RekallAgeModuleHostLoadModule module,
        string loadPlanPath)
    {
        var inventory = new Dictionary<string, RekallAgeModuleArtifactIntegrity>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var artifact in module.Artifacts)
        {
            if (artifact is null || !IsSafeLeaf(artifact.Path) || artifact.SizeBytes < 0 || !IsSha256(artifact.Sha256))
            {
                throw Rejected("Module-host artifact entry is malformed.", loadPlanPath);
            }

            var artifactPath = Path.GetFullPath(Path.Combine(moduleRoot, artifact.Path));
            if (!IsConfined(moduleRoot, artifactPath)
                || !inventory.TryAdd(artifactPath, artifact)
                || !File.Exists(artifactPath)
                || IsReparse(artifactPath))
            {
                throw Rejected("Module-host artifact is missing, duplicated, or outside its confined directory.", artifactPath);
            }

            using var verified = OpenVerified(artifactPath, artifact);
        }

        return inventory;
    }

    private static FileStream OpenVerified(string path, RekallAgeModuleArtifactIntegrity expected)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (stream.Length != expected.SizeBytes || !string.Equals(hash, expected.Sha256, StringComparison.Ordinal))
            {
                throw Rejected("Module-host artifact differs from its load plan.", path);
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static bool IsSafeLeaf(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !Path.IsPathRooted(value)
        && string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal)
        && !value.Contains('/')
        && !value.Contains('\\');

    private static bool IsSha256(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsConfined(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return candidate.Equals(normalizedRoot, comparison)
            || candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsReparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static RekallAgeModuleHostException Rejected(string message, string target, Exception? inner = null) => new(
        "REKALL_MODULE_HOST_MODULE_REJECTED",
        message,
        target,
        inner);

    private sealed class VerifiedLoadContext(
        string mainAssemblyPath,
        string moduleRoot,
        IReadOnlyDictionary<string, RekallAgeModuleArtifactIntegrity> inventory) : AssemblyLoadContext(false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null && assemblyName.Name.StartsWith("Rekall.Age.", StringComparison.Ordinal))
            {
                return Default.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == assemblyName.Name);
            }

            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is null)
            {
                return null;
            }

            var fullPath = Path.GetFullPath(resolved);
            if (!IsConfined(moduleRoot, fullPath) || !inventory.TryGetValue(fullPath, out var artifact))
            {
                throw Rejected("Module-host dependency is outside the verified inventory.", fullPath);
            }

            using var stream = OpenVerified(fullPath, artifact);
            return LoadFromStream(stream);
        }
    }
}
