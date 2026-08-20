using System.Security.Cryptography;
using System.Text.Json;
using Rekall.Age.Core.Product;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Modules.Hosting;

public sealed class RekallAgeModuleHostStager
{
    private const int MaximumHostFiles = 4096;
    private const long MaximumManifestBytes = 4L * 1024 * 1024;
    private readonly string _sessionsRoot;

    public RekallAgeModuleHostStager(string? sessionsRoot = null)
    {
        _sessionsRoot = Path.GetFullPath(sessionsRoot ?? Path.Combine(
            Path.GetTempPath(),
            "RekallAge",
            "module-host-sessions"));
    }

    public async ValueTask<RekallAgeModuleHostStagedSession> StageAsync(
        string projectRoot,
        string hostRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inspection = new RekallAgeProjectModuleTrustInspector().Inspect(projectRoot);
        if (!inspection.Ready)
        {
            var issue = inspection.Issues.FirstOrDefault()
                ?? new RekallAgeModuleTrustIssue(
                    "REKALL_MODULE_TRUST_NOT_READY",
                    "Project modules are not ready for restricted staging.",
                    projectRoot);
            throw new RekallAgeModuleHostException(issue.Code, issue.Message, issue.Target);
        }

        var payload = ReadHostManifest(hostRoot);
        Directory.CreateDirectory(_sessionsRoot);
        if (IsReparse(_sessionsRoot))
        {
            throw Rejected("Module-host session storage cannot use a reparse point.", _sessionsRoot);
        }

        var stagingRoot = Path.Combine(_sessionsRoot, $"session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);
        try
        {
            var stagedHostRoot = Path.Combine(stagingRoot, "host");
            Directory.CreateDirectory(stagedHostRoot);
            foreach (var file in payload.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = ConfinedPath(hostRoot, file.Path, "host payload");
                var destination = ConfinedPath(stagedHostRoot, file.Path, "staged host payload");
                await CopyVerifiedAsync(source, destination, file.SizeBytes, file.Sha256, cancellationToken);
            }

            var modules = new List<RekallAgeModuleHostLoadModule>(inspection.Modules.Count);
            foreach (var module in inspection.Modules.OrderBy(module => module.ModuleName, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceRoot = Path.Combine(module.ModuleDirectory, "bin", "rekall", "net10.0");
                var relativeDirectory = $"modules/{module.ModuleName}";
                var destinationRoot = ConfinedPath(stagingRoot, relativeDirectory, "staged module");
                Directory.CreateDirectory(destinationRoot);
                foreach (var artifact in module.OutputFiles.OrderBy(file => file.Path, StringComparer.Ordinal))
                {
                    var source = ConfinedPath(sourceRoot, artifact.Path, "module artifact");
                    var destination = ConfinedPath(destinationRoot, artifact.Path, "staged module artifact");
                    await CopyVerifiedAsync(source, destination, artifact.SizeBytes, artifact.Sha256, cancellationToken);
                }

                modules.Add(new RekallAgeModuleHostLoadModule(
                    module.ModuleName,
                    relativeDirectory,
                    $"{module.ModuleName}.dll",
                    module.OutputFiles));
            }

            var loadPlan = new RekallAgeModuleHostLoadPlan(
                1,
                RekallAgeModuleHostProtocol.Version,
                RekallAgeModuleTrustPostures.WindowsAppContainerRestricted,
                modules);
            var loadPlanPath = Path.Combine(stagingRoot, "rekall.module.host-plan.json");
            await File.WriteAllTextAsync(
                loadPlanPath,
                JsonSerializer.Serialize(loadPlan, JsonOptions),
                cancellationToken);
            var hostExecutablePath = ConfinedPath(stagedHostRoot, payload.ExecutablePath, "module-host executable");
            if (!File.Exists(hostExecutablePath))
            {
                throw Rejected("Module-host executable is absent from its verified payload inventory.", hostExecutablePath);
            }

            foreach (var path in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            }

            return new RekallAgeModuleHostStagedSession(
                stagingRoot,
                hostExecutablePath,
                loadPlanPath,
                loadPlan);
        }
        catch
        {
            RekallAgeModuleHostStagedSession.DeleteTree(stagingRoot);
            throw;
        }
    }

    private static RekallAgeModuleHostPayloadManifest ReadHostManifest(string hostRoot)
    {
        var root = Path.GetFullPath(hostRoot);
        var manifestPath = Path.Combine(root, RekallAgeModuleHostPayloadManifest.FileName);
        if (!Directory.Exists(root)
            || IsReparse(root)
            || !File.Exists(manifestPath)
            || IsReparse(manifestPath)
            || new FileInfo(manifestPath).Length > MaximumManifestBytes)
        {
            throw Rejected("Module-host payload manifest is missing, unsafe, or exceeds its bound.", manifestPath);
        }

        RekallAgeModuleHostPayloadManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RekallAgeModuleHostPayloadManifest>(
                File.ReadAllBytes(manifestPath),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            throw Rejected("Module-host payload manifest is malformed.", manifestPath, ex);
        }

        if (manifest is null
            || manifest.SchemaVersion != 1
            || manifest.ProtocolVersion != RekallAgeModuleHostProtocol.Version
            || !string.Equals(manifest.ProductVersion, RekallAgeProductInfo.Current.Version, StringComparison.Ordinal)
            || !IsSafeRelative(manifest.ExecutablePath)
            || manifest.Files is null
            || manifest.Files.Count == 0
            || manifest.Files.Count > MaximumHostFiles)
        {
            throw Rejected("Module-host payload manifest is incompatible or exceeds its bounds.", manifestPath);
        }

        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var file in manifest.Files)
        {
            if (file is not null)
            {
                try
                {
                    totalBytes = checked(totalBytes + file.SizeBytes);
                }
                catch (OverflowException)
                {
                    throw Rejected("Module-host payload inventory byte total overflowed its bound.", manifestPath);
                }
            }

            if (file is null
                || !IsSafeRelative(file.Path)
                || !paths.Add(file.Path.Replace('\\', '/'))
                || file.SizeBytes < 0
                || file.SizeBytes > 512L * 1024 * 1024
                || totalBytes > 2L * 1024 * 1024 * 1024
                || !IsSha256(file.Sha256))
            {
                throw Rejected("Module-host payload inventory contains an invalid or duplicate file.", manifestPath);
            }
        }

        if (!paths.Contains(manifest.ExecutablePath.Replace('\\', '/')))
        {
            throw Rejected("Module-host executable is not declared by its payload manifest.", manifestPath);
        }

        return manifest;
    }

    private static async ValueTask CopyVerifiedAsync(
        string source,
        string destination,
        long expectedSize,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(source) || IsReparse(source))
        {
            throw Rejected("A staged source file is missing or uses a reparse point.", source);
        }

        if (new FileInfo(source).Length != expectedSize
            || !string.Equals(ComputeHash(source), expectedHash, StringComparison.Ordinal))
        {
            throw Rejected("A staged source file differs from its verified inventory.", source);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(output, 64 * 1024, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }

        if (new FileInfo(source).Length != expectedSize
            || !string.Equals(ComputeHash(source), expectedHash, StringComparison.Ordinal)
            || new FileInfo(destination).Length != expectedSize
            || !string.Equals(ComputeHash(destination), expectedHash, StringComparison.Ordinal))
        {
            throw Rejected("A staged file changed during copy or failed destination verification.", source);
        }
    }

    private static string ConfinedPath(string root, string relativePath, string kind)
    {
        if (!IsSafeRelative(relativePath))
        {
            throw Rejected($"The {kind} path is not a safe relative path.", relativePath);
        }

        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw Rejected($"The {kind} path escapes its confined root.", candidate);
        }

        return candidate;
    }

    internal static bool IsSafeRelative(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            return false;
        }

        var normalized = value.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.None);
        return segments.Length > 0
            && string.Equals(normalized, string.Join('/', segments), StringComparison.Ordinal)
            && segments.All(IsSafeSegment);
    }

    private static bool IsSafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)
            || segment is "." or ".."
            || segment.EndsWith(' ')
            || segment.EndsWith('.')
            || segment.Any(character => char.IsControl(character)
                || character == ':'
                || Path.GetInvalidFileNameChars().Contains(character)))
        {
            return false;
        }

        var deviceName = segment.Split('.')[0];
        return deviceName.ToUpperInvariant() is not (
            "CON" or "PRN" or "AUX" or "NUL"
            or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9"
            or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9");
    }

    private static bool IsSha256(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ComputeHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsReparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static RekallAgeModuleHostException Rejected(string message, string target, Exception? inner = null) => new(
        "REKALL_MODULE_HOST_STAGING_REJECTED",
        message,
        target,
        inner);

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = RekallAgeModuleHostProtocol.MaximumJsonDepth,
        PropertyNameCaseInsensitive = false
    };
}

public sealed class RekallAgeModuleHostStagedSession : IAsyncDisposable
{
    private int _disposed;

    internal RekallAgeModuleHostStagedSession(
        string root,
        string hostExecutablePath,
        string loadPlanPath,
        RekallAgeModuleHostLoadPlan loadPlan)
    {
        Root = root;
        HostExecutablePath = hostExecutablePath;
        LoadPlanPath = loadPlanPath;
        LoadPlan = loadPlan;
    }

    public string Root { get; }

    public string HostExecutablePath { get; }

    public string LoadPlanPath { get; }

    public RekallAgeModuleHostLoadPlan LoadPlan { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DeleteTree(Root);
        }

        return ValueTask.CompletedTask;
    }

    internal static void DeleteTree(string root)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                }

                foreach (var path in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(path, FileAttributes.Directory);
                }

                Directory.Delete(root, recursive: true);
                return;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException
                && attempt < 199)
            {
                Thread.Sleep(25);
            }
        }
    }
}
