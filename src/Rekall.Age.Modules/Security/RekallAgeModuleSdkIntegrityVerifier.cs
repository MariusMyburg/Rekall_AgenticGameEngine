using System.Security.Cryptography;
using System.Text.Json;
using Rekall.Age.Core.Product;
using Rekall.Age.Modules.Sdk;

namespace Rekall.Age.Modules.Security;

public sealed record RekallAgeModuleSdkIntegrityLimits(
    int MaximumFiles = 16,
    long MaximumFileBytes = 256L * 1024 * 1024,
    long MaximumTotalBytes = 1024L * 1024 * 1024,
    long MaximumManifestBytes = 1024L * 1024);

public sealed record RekallAgeModuleSdkIntegrityIssue(string Message, string Target);

public sealed record RekallAgeModuleSdkIntegrityResult(
    bool Ready,
    string SdkRoot,
    IReadOnlyList<RekallAgeModuleSdkIntegrityIssue> Issues,
    bool StaleAgainstRunningEngine = false);

public sealed class RekallAgeModuleSdkIntegrityVerifier
{
    private readonly RekallAgeModuleSdkIntegrityLimits _limits;
    private readonly Func<string, FileAttributes> _readAttributes;

    public RekallAgeModuleSdkIntegrityVerifier(
        RekallAgeModuleSdkIntegrityLimits? limits = null,
        Func<string, FileAttributes>? readAttributes = null)
    {
        _limits = limits ?? new RekallAgeModuleSdkIntegrityLimits();
        _readAttributes = readAttributes ?? File.GetAttributes;
        if (_limits.MaximumFiles < 1
            || _limits.MaximumFileBytes < 1
            || _limits.MaximumTotalBytes < 1
            || _limits.MaximumManifestBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "All SDK integrity limits must be positive.");
        }
    }

    public RekallAgeModuleSdkIntegrityResult Verify(string projectRoot)
    {
        string sdkRoot;
        try
        {
            sdkRoot = ResolveSdkRoot(projectRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new RekallAgeModuleSdkIntegrityResult(
                false,
                projectRoot,
                [new RekallAgeModuleSdkIntegrityIssue($"Project-local module SDK path is invalid: {ex.Message}", projectRoot)]);
        }

        try
        {
            return VerifyCore(projectRoot, sdkRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new RekallAgeModuleSdkIntegrityResult(
                false,
                sdkRoot,
                [new RekallAgeModuleSdkIntegrityIssue($"Project-local module SDK could not be verified safely: {ex.Message}", sdkRoot)]);
        }
    }

    private RekallAgeModuleSdkIntegrityResult VerifyCore(string projectRoot, string sdkRoot)
    {
        var compatibility = RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion;
        var issues = new List<RekallAgeModuleSdkIntegrityIssue>();
        if (!Directory.Exists(sdkRoot))
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK is missing.", sdkRoot));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        var projectFullPath = Path.GetFullPath(projectRoot);
        var sdkPathChain = new[]
        {
            projectFullPath,
            Path.Combine(projectFullPath, ".rekall"),
            Path.Combine(projectFullPath, ".rekall", "sdk"),
            sdkRoot
        };
        var reparsePath = sdkPathChain.FirstOrDefault(path => Directory.Exists(path) && IsReparsePoint(path));
        if (reparsePath is not null)
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK path cannot contain a reparse point.", reparsePath));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        var manifestPath = Path.Combine(sdkRoot, "rekall.sdk.json");
        if (!File.Exists(manifestPath) || IsReparsePoint(manifestPath))
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK manifest is missing or uses a reparse point.", manifestPath));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        if (new FileInfo(manifestPath).Length > _limits.MaximumManifestBytes)
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK manifest exceeds its size limit.", manifestPath));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        RekallAgeModuleSdkManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RekallAgeModuleSdkManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue($"Project-local module SDK manifest is malformed: {ex.Message}", manifestPath));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        if (manifest is null
            || manifest.SchemaVersion != 1
            || manifest.CompatibilityVersion != compatibility
            || !string.Equals(manifest.ProductVersion, RekallAgeProductInfo.Current.Version, StringComparison.Ordinal)
            || manifest.Assemblies is null
            || manifest.Files is null)
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK manifest is incompatible with the running engine.", manifestPath));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        var expectedNames = RekallAgeModuleSdkInstaller.RequiredAssemblyNames
            .Append("Rekall.Age.Sdk.props")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedAssemblies = RekallAgeModuleSdkInstaller.RequiredAssemblyNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!manifest.Assemblies.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expectedAssemblies, StringComparer.Ordinal))
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK assembly contract differs from the running engine.", manifestPath));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues, StaleAgainstRunningEngine: true);
        }

        var actualEntries = Directory.EnumerateFileSystemEntries(sdkRoot, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(_limits.MaximumFiles + 2)
            .ToArray();
        if (actualEntries.Length - 1 > _limits.MaximumFiles)
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK exceeds its file-count limit.", sdkRoot));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        if (actualEntries.Any(Directory.Exists))
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK cannot contain directories.", sdkRoot));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        var actualResourceNames = actualEntries
            .Select(path => Path.GetFileName(path))
            .Where(name => !string.Equals(name, "rekall.sdk.json", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualResourceNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK contains missing or unexpected resources.", sdkRoot));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        var inventory = new Dictionary<string, RekallAgeModuleSdkFileIntegrity>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            if (file is null
                || string.IsNullOrWhiteSpace(file.Path)
                || Path.IsPathRooted(file.Path)
                || !string.Equals(file.Path, Path.GetFileName(file.Path), StringComparison.Ordinal)
                || !inventory.TryAdd(file.Path, file))
            {
                issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK inventory contains an invalid or duplicate resource path.", manifestPath));
                return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
            }
        }

        if (inventory.Count != expectedNames.Length || !inventory.Keys.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK inventory is incomplete or contains unexpected resources.", manifestPath));
            return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
        }

        long totalBytes = 0;
        // Counts only the "matches its own manifest but not the running engine" issues, which
        // are staleness rather than tampering and can be repaired by reinstalling the SDK.
        var staleAgainstEngineIssues = 0;
        foreach (var name in expectedNames)
        {
            var path = Path.Combine(sdkRoot, name);
            if (IsReparsePoint(path))
            {
                issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK resources cannot use reparse points.", path));
                continue;
            }

            var sizeBytes = new FileInfo(path).Length;
            if (sizeBytes > _limits.MaximumFileBytes)
            {
                issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK resource exceeds its file-size limit.", path));
                continue;
            }

            try
            {
                totalBytes = checked(totalBytes + sizeBytes);
            }
            catch (OverflowException)
            {
                issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK total size is invalid.", sdkRoot));
                return new RekallAgeModuleSdkIntegrityResult(false, sdkRoot, issues);
            }

            var expected = inventory[name];
            var hash = ComputeSha256(path);
            if (expected.SizeBytes != sizeBytes
                || !string.Equals(expected.Sha256, hash, StringComparison.Ordinal))
            {
                issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK resource does not match its bounded inventory.", path));
                continue;
            }

            staleAgainstEngineIssues++;
            var canonicalHash = name.Equals("Rekall.Age.Sdk.props", StringComparison.Ordinal)
                ? ComputeContentSha256(RekallAgeModuleSdkInstaller.CreatePropsFile())
                : ComputeSha256(RekallAgeModuleSdkInstaller.ResolveAssembly(name));
            if (!string.Equals(hash, canonicalHash, StringComparison.Ordinal))
            {
                issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK resource differs from the running engine's trusted resource.", path));
            }
            else
            {
                staleAgainstEngineIssues--;
            }
        }

        if (totalBytes > _limits.MaximumTotalBytes)
        {
            issues.Add(new RekallAgeModuleSdkIntegrityIssue("Project-local module SDK exceeds its total-size limit.", sdkRoot));
        }

        return new RekallAgeModuleSdkIntegrityResult(
            issues.Count == 0,
            sdkRoot,
            issues,
            StaleAgainstRunningEngine: issues.Count > 0 && staleAgainstEngineIssues == issues.Count);
    }

    private bool IsReparsePoint(string path) =>
        (_readAttributes(Path.GetFullPath(path)) & FileAttributes.ReparsePoint) != 0;

    private static string ResolveSdkRoot(string projectRoot) => Path.Combine(
        Path.GetFullPath(projectRoot),
        ".rekall",
        "sdk",
        RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture));

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeContentSha256(string content)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content), writable: false);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
