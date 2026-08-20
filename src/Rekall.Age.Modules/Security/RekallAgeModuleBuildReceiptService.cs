using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Modules.Security;

public sealed class RekallAgeModuleBuildReceiptService
{
    public const string ReceiptFileName = "rekall.module.build.json";
    private readonly RekallAgeModuleTrustLimits _limits;
    private readonly Func<string, FileAttributes> _readAttributes;

    public RekallAgeModuleBuildReceiptService(
        RekallAgeModuleTrustLimits? limits = null,
        Func<string, FileAttributes>? readAttributes = null)
    {
        _limits = limits ?? new RekallAgeModuleTrustLimits();
        _readAttributes = readAttributes ?? File.GetAttributes;
        if (_limits.MaximumOutputFilesPerModule < 1
            || _limits.MaximumOutputFileBytes < 1
            || _limits.MaximumTotalOutputBytesPerModule < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Module receipt output limits must be positive.");
        }
    }

    public string CaptureSourceFingerprint(RekallAgeModuleBuildCandidate candidate) =>
        ComputeSourceFingerprint(candidate.ModuleDirectory, SourcePaths(candidate));

    public async ValueTask<string> WriteAsync(
        string projectRoot,
        RekallAgeModuleBuildCandidate candidate,
        string expectedSourceFingerprint,
        CancellationToken cancellationToken)
    {
        var currentSourceFingerprint = CaptureSourceFingerprint(candidate);
        if (!string.Equals(currentSourceFingerprint, expectedSourceFingerprint, StringComparison.Ordinal))
        {
            throw new RekallAgeModuleReceiptException(
                "REKALL_MODULE_SOURCE_CHANGED_DURING_BUILD",
                "Module source changed while compilation was running; no receipt was issued.",
                candidate.ModuleDirectory);
        }

        if (IsReparse(candidate.ModuleDirectory) || IsReparse(candidate.OutputDirectory))
        {
            throw new RekallAgeModuleReceiptException(
                "REKALL_MODULE_RECEIPT_OUTPUT_REJECTED",
                "Module receipt paths cannot use symbolic links, junctions, or reparse points.",
                candidate.OutputDirectory);
        }

        var entries = Directory.EnumerateFileSystemEntries(candidate.OutputDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), ReceiptFileName, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(_limits.MaximumOutputFilesPerModule + 1)
            .ToArray();
        if (entries.Length == 0
            || entries.Length > _limits.MaximumOutputFilesPerModule
            || entries.Any(Directory.Exists)
            || entries.Any(IsReparse))
        {
            throw new RekallAgeModuleReceiptException(
                "REKALL_MODULE_RECEIPT_OUTPUT_REJECTED",
                "Compiler output is empty, exceeds bounds, contains directories, or uses a reparse point.",
                candidate.OutputDirectory);
        }

        long totalBytes = 0;
        foreach (var path in entries)
        {
            var size = new FileInfo(path).Length;
            totalBytes = checked(totalBytes + size);
            if (size > _limits.MaximumOutputFileBytes || totalBytes > _limits.MaximumTotalOutputBytesPerModule)
            {
                throw new RekallAgeModuleReceiptException(
                    "REKALL_MODULE_RECEIPT_OUTPUT_REJECTED",
                    "Compiler output exceeds the module receipt byte limits.",
                    path);
            }
        }

        var outputFiles = entries
            .Where(IsReceiptArtifact)
            .Select(path => CreateArtifact(candidate.OutputDirectory, path, candidate.ModuleName))
            .ToArray();
        if (!outputFiles.Any(file => file.Path.Equals($"{candidate.ModuleName}.dll", StringComparison.Ordinal)))
        {
            throw new RekallAgeModuleReceiptException(
                "REKALL_MODULE_RECEIPT_OUTPUT_REJECTED",
                "Compiler output does not contain the module's load-relevant main assembly.",
                candidate.OutputDirectory);
        }
        var sourcePaths = SourcePaths(candidate);
        var receipt = new RekallAgeModuleBuildReceipt(
            2,
            RekallAgeModuleTrustPostures.WindowsAppContainerRestricted,
            RekallAgeProductInfo.Current.Version,
            RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion,
            candidate.ModuleName,
            NormalizeRelative(projectRoot, candidate.ProjectPath),
            NormalizeRelative(projectRoot, candidate.OutputDirectory),
            sourcePaths.Select(path => NormalizeRelative(candidate.ModuleDirectory, path)).ToArray(),
            currentSourceFingerprint,
            outputFiles);

        var receiptPath = Path.Combine(candidate.OutputDirectory, ReceiptFileName);
        var temporaryPath = receiptPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(receipt, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, receiptPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return receiptPath;
    }

    internal static string ComputeSourceFingerprint(string moduleDirectory, IReadOnlyList<string> sourcePaths)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in sourcePaths.OrderBy(path => NormalizeRelative(moduleDirectory, path), StringComparer.Ordinal))
        {
            AppendField(hash, Encoding.UTF8.GetBytes(NormalizeRelative(moduleDirectory, path)));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            AppendLength(hash, stream.Length);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static string NormalizeRelative(string root, string path) =>
        Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path))
            .Replace(Path.DirectorySeparatorChar, '/');

    internal static bool IsReceiptArtifact(string path)
    {
        var name = Path.GetFileName(path);
        return !name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);
    }

    private static RekallAgeModuleArtifactIntegrity CreateArtifact(
        string outputRoot,
        string path,
        string moduleName)
    {
        var fileName = Path.GetFileName(path);
        string? assemblyIdentity = null;
        if (fileName.Equals($"{moduleName}.dll", StringComparison.Ordinal))
        {
            assemblyIdentity = AssemblyName.GetAssemblyName(path).FullName;
        }

        return new RekallAgeModuleArtifactIntegrity(
            NormalizeRelative(outputRoot, path),
            new FileInfo(path).Length,
            ComputeSha256(path),
            assemblyIdentity);
    }

    private static void AppendField(IncrementalHash hash, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private static void AppendLength(IncrementalHash hash, long value)
    {
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, value);
        hash.AppendData(length);
    }

    private bool IsReparse(string path) =>
        (_readAttributes(Path.GetFullPath(path)) & FileAttributes.ReparsePoint) != 0;

    private static string[] SourcePaths(RekallAgeModuleBuildCandidate candidate) => candidate.SourcePaths
        .Prepend(candidate.ProjectPath)
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
