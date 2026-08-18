using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Modules;

public static class RekallAgeProjectModuleAssemblyLoader
{
    public static IReadOnlyList<Assembly> LoadBuiltModuleAssemblies(string projectRoot)
    {
        var inspection = new RekallAgeProjectModuleTrustInspector().Inspect(projectRoot);
        if (!inspection.Ready)
        {
            var issue = inspection.Issues.FirstOrDefault()
                ?? new RekallAgeModuleTrustIssue(
                    "REKALL_MODULE_TRUST_NOT_READY",
                    "Project modules are not ready for verified loading.",
                    projectRoot);
            throw new RekallAgeModuleTrustException(issue.Code, issue.Message, issue.Target);
        }

        var assemblies = new List<Assembly>();
        foreach (var module in inspection.Modules.OrderBy(module => module.ModuleDirectory, StringComparer.Ordinal))
        {
            var outputRoot = Path.Combine(module.ModuleDirectory, "bin", "rekall", "net10.0");
            var assemblyName = $"{module.ModuleName}.dll";
            var mainArtifact = module.OutputFiles.SingleOrDefault(file =>
                string.Equals(file.Path, assemblyName, StringComparison.Ordinal));
            if (mainArtifact is null)
            {
                throw new RekallAgeModuleTrustException(
                    "REKALL_MODULE_ASSEMBLY_MISSING",
                    "Verified module output does not contain its main assembly.",
                    outputRoot);
            }

            var assemblyPath = Path.Combine(outputRoot, assemblyName);
            var loadContext = new RekallAgeProjectModuleLoadContext(
                assemblyPath,
                outputRoot,
                module.OutputFiles);
            using var assemblyStream = OpenVerifiedStream(assemblyPath, mainArtifact);
            assemblies.Add(loadContext.LoadFromStream(assemblyStream));
        }

        return assemblies;
    }

    private static FileStream OpenVerifiedStream(
        string path,
        RekallAgeModuleArtifactIntegrity expected)
    {
        var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete);
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (stream.Length != expected.SizeBytes
                || !string.Equals(hash, expected.Sha256, StringComparison.Ordinal))
            {
                throw new RekallAgeModuleTrustException(
                    "REKALL_MODULE_LOAD_ARTIFACT_CHANGED",
                    "Module artifact changed after trust inspection and before loading.",
                    path);
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

    private sealed class RekallAgeProjectModuleLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _outputRoot;
        private readonly IReadOnlyDictionary<string, RekallAgeModuleArtifactIntegrity> _inventory;

        public RekallAgeProjectModuleLoadContext(
            string mainAssemblyPath,
            string outputRoot,
            IReadOnlyList<RekallAgeModuleArtifactIntegrity> inventory)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(Path.GetFullPath(mainAssemblyPath));
            _outputRoot = Path.GetFullPath(outputRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _inventory = inventory.ToDictionary(
                file => Path.GetFullPath(Path.Combine(_outputRoot, file.Path)),
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null
                && assemblyName.Name.StartsWith("Rekall.Age.", StringComparison.Ordinal))
            {
                return AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName.Name);
            }

            var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolvedPath is null)
            {
                return null;
            }

            var fullPath = Path.GetFullPath(resolvedPath);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullPath.StartsWith(_outputRoot + Path.DirectorySeparatorChar, comparison)
                || !_inventory.TryGetValue(fullPath, out var expected))
            {
                throw new RekallAgeModuleTrustException(
                    "REKALL_MODULE_DEPENDENCY_NOT_VERIFIED",
                    $"Module dependency '{assemblyName.Name}' is outside the verified artifact inventory.",
                    fullPath);
            }

            using var stream = OpenVerifiedStream(fullPath, expected);
            return LoadFromStream(stream);
        }
    }
}
