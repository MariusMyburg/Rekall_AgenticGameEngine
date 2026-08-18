using System.Reflection;
using System.Text.Json;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Modules.Security;

public sealed class RekallAgeProjectModuleTrustInspector
{
    private readonly RekallAgeModuleTrustLimits _limits;
    private readonly Func<string, FileAttributes> _readAttributes;

    public RekallAgeProjectModuleTrustInspector(
        RekallAgeModuleTrustLimits? limits = null,
        Func<string, FileAttributes>? readAttributes = null)
    {
        _limits = limits ?? new RekallAgeModuleTrustLimits();
        _readAttributes = readAttributes ?? File.GetAttributes;
        if (_limits.MaximumModules < 1
            || _limits.MaximumOutputFilesPerModule < 1
            || _limits.MaximumOutputFileBytes < 1
            || _limits.MaximumTotalOutputBytesPerModule < 1
            || _limits.MaximumReceiptBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "All module trust inspection limits must be positive.");
        }
    }

    public RekallAgeProjectModuleTrustInspection Inspect(string projectRoot)
    {
        var modules = new List<RekallAgeModuleTrustInspection>();
        var issues = new List<RekallAgeModuleTrustIssue>();
        try
        {
            InspectCore(projectRoot, modules, issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {
            issues.Add(new RekallAgeModuleTrustIssue(
                "REKALL_MODULE_TRUST_INSPECTION_FAILED",
                $"Module trust inspection failed safely: {ex.Message}",
                projectRoot));
        }

        return new RekallAgeProjectModuleTrustInspection(
            issues.Count == 0 && modules.All(module => module.Ready),
            RekallAgeModuleTrustPostures.InProcessFullTrust,
            modules,
            issues);
    }

    private void InspectCore(
        string projectRoot,
        ICollection<RekallAgeModuleTrustInspection> modules,
        ICollection<RekallAgeModuleTrustIssue> issues)
    {
        var root = Path.GetFullPath(projectRoot);
        var modulesRoot = Path.Combine(root, "Modules");
        if (!Directory.Exists(modulesRoot))
        {
            return;
        }

        if (IsReparse(root) || IsReparse(modulesRoot))
        {
            Add(issues, "REKALL_MODULE_TRUST_REPARSE_POINT", "Project module trust paths cannot use reparse points.", modulesRoot);
            return;
        }

        var moduleDirectories = Directory.EnumerateDirectories(modulesRoot, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(_limits.MaximumModules + 1)
            .ToArray();
        if (moduleDirectories.Length > _limits.MaximumModules)
        {
            Add(issues, "REKALL_MODULE_TRUST_BOUNDS_EXCEEDED", "Project exceeds the module trust inspection limit.", modulesRoot);
            return;
        }

        foreach (var moduleDirectory in moduleDirectories)
        {
            InspectModule(root, moduleDirectory, modules, issues);
        }
    }

    private void InspectModule(
        string projectRoot,
        string moduleDirectory,
        ICollection<RekallAgeModuleTrustInspection> modules,
        ICollection<RekallAgeModuleTrustIssue> issues)
    {
        var issueCount = issues.Count;
        var moduleName = Path.GetFileName(moduleDirectory);
        var outputRoot = Path.Combine(moduleDirectory, "bin", "rekall", "net10.0");
        var receiptPath = Path.Combine(outputRoot, RekallAgeModuleBuildReceiptService.ReceiptFileName);
        if (!Directory.Exists(outputRoot) || !File.Exists(receiptPath))
        {
            Add(issues, "REKALL_MODULE_RECEIPT_MISSING", "Module build receipt is missing. Rebuild the module.", receiptPath);
            modules.Add(Empty(moduleName, moduleDirectory, receiptPath));
            return;
        }

        if (IsReparse(moduleDirectory) || IsReparse(outputRoot) || IsReparse(receiptPath))
        {
            Add(issues, "REKALL_MODULE_TRUST_REPARSE_POINT", "Module trust paths cannot use symbolic links, junctions, or reparse points.", moduleDirectory);
            modules.Add(Empty(moduleName, moduleDirectory, receiptPath));
            return;
        }

        if (new FileInfo(receiptPath).Length > _limits.MaximumReceiptBytes)
        {
            Add(issues, "REKALL_MODULE_RECEIPT_BOUNDS_EXCEEDED", "Module build receipt exceeds its size limit.", receiptPath);
            modules.Add(Empty(moduleName, moduleDirectory, receiptPath));
            return;
        }

        RekallAgeModuleBuildReceipt? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<RekallAgeModuleBuildReceipt>(
                File.ReadAllText(receiptPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            Add(issues, "REKALL_MODULE_RECEIPT_MALFORMED", $"Module build receipt is malformed: {ex.Message}", receiptPath);
            modules.Add(Empty(moduleName, moduleDirectory, receiptPath));
            return;
        }

        if (receipt is null
            || receipt.SchemaVersion != 1
            || !string.Equals(receipt.TrustPosture, RekallAgeModuleTrustPostures.InProcessFullTrust, StringComparison.Ordinal)
            || !string.Equals(receipt.ProductVersion, RekallAgeProductInfo.Current.Version, StringComparison.Ordinal)
            || receipt.SdkCompatibilityVersion != RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion
            || !string.Equals(receipt.ModuleName, moduleName, StringComparison.Ordinal)
            || receipt.SourceFiles is null
            || receipt.SourceFiles.Count < 2
            || !receipt.SourceFiles.Contains($"{moduleName}.csproj", StringComparer.Ordinal)
            || !receipt.SourceFiles.Any(path => path?.EndsWith(".cs", StringComparison.Ordinal) == true)
            || receipt.OutputFiles is null)
        {
            Add(issues, "REKALL_MODULE_RECEIPT_INCOMPATIBLE", "Module build receipt is incompatible with the running engine.", receiptPath);
            modules.Add(Empty(moduleName, moduleDirectory, receiptPath));
            return;
        }

        var expectedProjectPath = RekallAgeModuleBuildReceiptService.NormalizeRelative(projectRoot, Path.Combine(moduleDirectory, $"{moduleName}.csproj"));
        var expectedOutputRoot = RekallAgeModuleBuildReceiptService.NormalizeRelative(projectRoot, outputRoot);
        if (!string.Equals(receipt.ProjectPath, expectedProjectPath, StringComparison.Ordinal)
            || !string.Equals(receipt.OutputRoot, expectedOutputRoot, StringComparison.Ordinal)
            || !IsSha256(receipt.SourceFingerprint))
        {
            Add(issues, "REKALL_MODULE_RECEIPT_PATH_INVALID", "Module build receipt paths or source fingerprint are invalid.", receiptPath);
        }

        ValidateOutput(outputRoot, moduleName, receipt, issues);
        ValidateSource(moduleDirectory, receipt, issues);
        modules.Add(new RekallAgeModuleTrustInspection(
            moduleName,
            moduleDirectory,
            receiptPath,
            receipt.TrustPosture,
            receipt.SourceFingerprint,
            receipt.OutputFiles,
            issues.Count == issueCount));
    }

    private void ValidateOutput(
        string outputRoot,
        string moduleName,
        RekallAgeModuleBuildReceipt receipt,
        ICollection<RekallAgeModuleTrustIssue> issues)
    {
        if (receipt.OutputFiles.Count == 0 || receipt.OutputFiles.Count > _limits.MaximumOutputFilesPerModule)
        {
            Add(issues, "REKALL_MODULE_OUTPUT_BOUNDS_EXCEEDED", "Module output inventory is empty or exceeds its file limit.", outputRoot);
            return;
        }

        var inventory = new Dictionary<string, RekallAgeModuleArtifactIntegrity>(StringComparer.Ordinal);
        var osKeys = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var file in receipt.OutputFiles)
        {
            if (file is null || !IsSafeLeaf(file.Path) || !inventory.TryAdd(file.Path, file) || !osKeys.Add(file.Path))
            {
                Add(issues, "REKALL_MODULE_RECEIPT_PATH_INVALID", "Module output inventory contains an invalid, duplicate, or colliding path.", outputRoot);
                return;
            }
        }

        var entries = Directory.EnumerateFileSystemEntries(outputRoot, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(_limits.MaximumOutputFilesPerModule + 2)
            .ToArray();
        if (entries.Any(Directory.Exists) || entries.Length - 1 > _limits.MaximumOutputFilesPerModule)
        {
            Add(issues, "REKALL_MODULE_OUTPUT_BOUNDS_EXCEEDED", "Module output contains directories or exceeds its entry limit.", outputRoot);
            return;
        }

        var reparseEntry = entries.FirstOrDefault(path => IsReparse(path));
        if (reparseEntry is not null)
        {
            Add(issues, "REKALL_MODULE_TRUST_REPARSE_POINT", "Module output files cannot use reparse points.", reparseEntry);
            return;
        }

        var actualNames = entries.Select(path => Path.GetFileName(path)!)
            .Where(name => !string.Equals(name, RekallAgeModuleBuildReceiptService.ReceiptFileName, StringComparison.Ordinal))
            .Where(RekallAgeModuleBuildReceiptService.IsReceiptArtifact)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actualNames.SequenceEqual(inventory.Keys.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            Add(issues, "REKALL_MODULE_OUTPUT_SET_MISMATCH", "Module output contains missing or unexpected files.", outputRoot);
            return;
        }

        long totalBytes = 0;
        foreach (var name in actualNames)
        {
            var path = Path.Combine(outputRoot, name);
            if (IsReparse(path))
            {
                Add(issues, "REKALL_MODULE_TRUST_REPARSE_POINT", "Module output files cannot use reparse points.", path);
                continue;
            }

            var size = new FileInfo(path).Length;
            totalBytes = checked(totalBytes + size);
            var expected = inventory[name];
            if (size > _limits.MaximumOutputFileBytes || expected.SizeBytes != size)
            {
                Add(issues, "REKALL_MODULE_OUTPUT_SIZE_MISMATCH", "Module output size differs from its build receipt or exceeds bounds.", path);
                continue;
            }

            var hash = RekallAgeModuleBuildReceiptService.ComputeSha256(path);
            if (!IsSha256(expected.Sha256) || !string.Equals(hash, expected.Sha256, StringComparison.Ordinal))
            {
                Add(issues, "REKALL_MODULE_OUTPUT_HASH_MISMATCH", "Module output hash differs from its build receipt.", path);
                continue;
            }

            if (name.Equals($"{moduleName}.dll", StringComparison.Ordinal))
            {
                AssemblyName identity;
                try
                {
                    identity = AssemblyName.GetAssemblyName(path);
                }
                catch (BadImageFormatException)
                {
                    Add(issues, "REKALL_MODULE_ASSEMBLY_IDENTITY_MISMATCH", "Module main assembly is not a valid managed assembly.", path);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(expected.AssemblyIdentity)
                    || !string.Equals(identity.FullName, expected.AssemblyIdentity, StringComparison.Ordinal)
                    || !string.Equals(identity.Name, moduleName, StringComparison.Ordinal))
                {
                    Add(issues, "REKALL_MODULE_ASSEMBLY_IDENTITY_MISMATCH", "Module assembly identity differs from its receipt or module name.", path);
                }
            }
        }

        if (totalBytes > _limits.MaximumTotalOutputBytesPerModule)
        {
            Add(issues, "REKALL_MODULE_OUTPUT_BOUNDS_EXCEEDED", "Module output exceeds its total byte limit.", outputRoot);
        }

        if (!inventory.ContainsKey($"{moduleName}.dll"))
        {
            Add(issues, "REKALL_MODULE_ASSEMBLY_MISSING", "Module main assembly is absent from its output inventory.", outputRoot);
        }
    }

    private void ValidateSource(
        string moduleDirectory,
        RekallAgeModuleBuildReceipt receipt,
        ICollection<RekallAgeModuleTrustIssue> issues)
    {
        var recorded = new HashSet<string>(StringComparer.Ordinal);
        var osKeys = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var source in receipt.SourceFiles)
        {
            if (!IsSafeLeaf(source) || !recorded.Add(source) || !osKeys.Add(source))
            {
                Add(issues, "REKALL_MODULE_RECEIPT_PATH_INVALID", "Module source inventory contains an invalid or duplicate path.", moduleDirectory);
                return;
            }
        }

        var actual = Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(moduleDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
            .Select(path => RekallAgeModuleBuildReceiptService.NormalizeRelative(moduleDirectory, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (actual.Length == 0)
        {
            return;
        }

        var existingRecorded = recorded.Select(path => Path.Combine(moduleDirectory, path))
            .Where(File.Exists)
            .ToArray();
        if (existingRecorded.Length != recorded.Count
            || !actual.SequenceEqual(recorded.OrderBy(path => path, StringComparer.Ordinal), StringComparer.Ordinal)
            || existingRecorded.Any(IsReparse)
            || !string.Equals(
                RekallAgeModuleBuildReceiptService.ComputeSourceFingerprint(moduleDirectory, existingRecorded),
                receipt.SourceFingerprint,
                StringComparison.Ordinal))
        {
            Add(issues, "REKALL_MODULE_SOURCE_STALE", "Module authoring source differs from the source used for this build.", moduleDirectory);
        }
    }

    private bool IsReparse(string path) => (_readAttributes(Path.GetFullPath(path)) & FileAttributes.ReparsePoint) != 0;

    private static bool IsSafeLeaf(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && string.Equals(path, Path.GetFileName(path), StringComparison.Ordinal)
        && !path.Contains('\\')
        && !path.Contains('/');

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static RekallAgeModuleTrustInspection Empty(string moduleName, string moduleDirectory, string receiptPath) =>
        new(moduleName, moduleDirectory, receiptPath, RekallAgeModuleTrustPostures.InProcessFullTrust, string.Empty, [], false);

    private static void Add(ICollection<RekallAgeModuleTrustIssue> issues, string code, string message, string target) =>
        issues.Add(new RekallAgeModuleTrustIssue(code, message, target));
}
