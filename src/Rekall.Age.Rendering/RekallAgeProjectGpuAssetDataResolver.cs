using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Rekall.Age.Assets;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeProjectGpuAssetDataResolver : IRekallAgeGpuAssetDataResolver
{
    private const int MaximumCatalogBytes = 8 * 1024 * 1024;
    private const uint GenericRead = 0x80000000, FileShareRead = 1, OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000, FileFlagBackupSemantics = 0x02000000;
    private readonly string _projectRoot;
    private readonly string _assetsRoot;
    private readonly RekallAgeAssetCatalogStore _catalogStore = new();
    private static readonly JsonSerializerOptions CatalogJsonOptions = new() { PropertyNameCaseInsensitive = true, MaxDepth = 64 };

    public RekallAgeProjectGpuAssetDataResolver(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        _projectRoot = Path.GetFullPath(projectRoot);
        _assetsRoot = Path.GetFullPath(Path.Combine(_projectRoot, "Assets"));
    }

    public string CatalogRevision
    {
        get
        {
            var path = _catalogStore.GetCatalogPath(_projectRoot);
            if (!File.Exists(path)) return "missing";
            try { return Convert.ToHexString(SHA256.HashData(ReadSecureFile(path, MaximumCatalogBytes))).ToLowerInvariant(); }
            catch (Exception exception) when (IsResolutionException(exception)) { return $"invalid:{exception.GetType().Name}:{exception.Message}"; }
        }
    }

    public RekallAgeGpuAssetDataResolution Resolve(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return Invalid("REKALL_GPU_ASSET_ID_INVALID", "GPU asset ID must be nonempty.", assetId);
        try
        {
            var catalogPath = _catalogStore.GetCatalogPath(_projectRoot);
            if (!File.Exists(catalogPath)) return Invalid("REKALL_GPU_ASSET_NOT_FOUND", $"Asset catalog does not contain '{assetId}'.", assetId);
            var catalog = JsonSerializer.Deserialize<RekallAgeAssetCatalogDocument>(ReadSecureFile(catalogPath, MaximumCatalogBytes), CatalogJsonOptions)
                ?? throw new InvalidDataException("Asset catalog must contain a JSON document.");
            if (catalog.Assets is null) throw new InvalidDataException("Asset catalog assets collection cannot be null.");
            var matches = catalog.Assets.Where(asset => asset is not null && string.Equals(asset.Id, assetId, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) return Invalid("REKALL_GPU_ASSET_NOT_FOUND", $"Asset catalog must contain exactly one asset with ID '{assetId}'.", assetId);

            var asset = matches[0];
            var candidates = new List<string>();
            var rejectedOutsideProject = false;
            AddCandidate(Path.IsPathRooted(asset.ImportedPath) ? asset.ImportedPath : Path.Combine(_projectRoot, asset.ImportedPath));
            AddCandidate(Path.Combine(_assetsRoot, asset.Kind, Path.GetFileName(asset.ImportedPath)));
            var path = candidates.Distinct(PathComparer).FirstOrDefault(File.Exists);
            if (path is null)
                return rejectedOutsideProject
                    ? Invalid("REKALL_GPU_ASSET_PATH_OUTSIDE_PROJECT", "Asset catalog paths must remain inside the current project Assets directory.", assetId)
                    : Invalid("REKALL_GPU_ASSET_FILE_MISSING", $"Imported file for asset '{assetId}' was not found in the current project.", assetId);

            var data = ReadSecureFile(path, RekallAgeRuntimeGpuWorkloadCompiler.MaximumInitialAssetBytes);
            if (data.Length == 0) return Invalid("REKALL_GPU_INITIAL_DATA_LIMIT", $"GPU asset data must contain 1 to {RekallAgeRuntimeGpuWorkloadCompiler.MaximumInitialAssetBytes} bytes.", assetId);
            var actualHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            if (!actualHash.Equals(asset.ContentHash, StringComparison.OrdinalIgnoreCase))
                return Invalid("REKALL_GPU_ASSET_HASH_MISMATCH", $"Imported data for asset '{assetId}' does not match its catalog SHA-256.", assetId);
            return new(data, []);

            void AddCandidate(string candidate)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (IsWithinAssetsRoot(fullPath)) candidates.Add(fullPath); else rejectedOutsideProject = true;
            }
        }
        catch (UnsafeAssetPathException exception) { return Invalid("REKALL_GPU_ASSET_PATH_OUTSIDE_PROJECT", exception.Message, assetId); }
        catch (AssetSizeException exception) { return Invalid("REKALL_GPU_INITIAL_DATA_LIMIT", exception.Message, assetId); }
        catch (Exception exception) when (IsResolutionException(exception)) { return Invalid("REKALL_GPU_ASSET_RESOLUTION_FAILED", exception.Message, assetId); }
    }

    private byte[] ReadSecureFile(string path, int maximumBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Project GPU asset loading fails closed until handle-verified containment is available on this platform.");
        var fullPath = Path.GetFullPath(path);
        if (!IsWithinAssetsRoot(fullPath) || ContainsReparsePoint(fullPath))
            throw new UnsafeAssetPathException("GPU asset paths cannot leave Assets or traverse filesystem links or junctions.");
        using var assetsHandle = OpenDirectoryNoFollow(_assetsRoot);
        var finalAssetsRoot = FinalPath(assetsHandle);
        using var handle = OpenReadNoFollow(fullPath);
        var finalPath = FinalPath(handle);
        if (!IsWithin(finalPath, finalAssetsRoot))
            throw new UnsafeAssetPathException("The opened GPU asset handle resolves outside the current project Assets directory.");
        if (!GetFileInformationByHandle(handle, out var information)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((information.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0 || information.NumberOfLinks != 1)
            throw new UnsafeAssetPathException("GPU asset files cannot be reparse points or hard links.");
        var length = ((ulong)information.FileSizeHigh << 32) | information.FileSizeLow;
        if (length > (ulong)maximumBytes) throw new AssetSizeException($"GPU asset data cannot exceed {maximumBytes} bytes.");
        using var stream = new FileStream(handle, FileAccess.Read, 81920, false);
        using var output = new MemoryStream(Math.Min(maximumBytes, 81920));
        var buffer = new byte[81920];
        while (true)
        {
            var remaining = maximumBytes + 1L - output.Length;
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) break;
            output.Write(buffer, 0, read);
            if (output.Length > maximumBytes) throw new AssetSizeException($"GPU asset data cannot exceed {maximumBytes} bytes.");
        }
        if (ContainsReparsePoint(fullPath)) throw new UnsafeAssetPathException("GPU asset path changed while it was being read.");
        return output.ToArray();
    }

    private static SafeFileHandle OpenReadNoFollow(string path)
    {
        var handle = CreateFile(path, GenericRead, FileShareRead, IntPtr.Zero, OpenExisting, FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }

    private static SafeFileHandle OpenDirectoryNoFollow(string path)
    {
        var handle = CreateFile(path, 0, FileShareRead, IntPtr.Zero, OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetFileInformationByHandle(handle, out var information)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((information.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
        {
            handle.Dispose();
            throw new UnsafeAssetPathException("The project Assets directory cannot be a filesystem link or junction.");
        }
        return handle;
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (true)
        {
            var buffer = new char[capacity];
            var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length < buffer.Length) return NormalizeFinalPath(new string(buffer, 0, (int)length));
            capacity = checked((int)length + 1);
        }
    }

    private static string NormalizeFinalPath(string path) => Path.GetFullPath(
        path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)
            ? "\\\\" + path[8..]
            : path.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase) ? path[4..] : path);

    private static bool IsWithin(string path, string root) => path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    private bool IsWithinAssetsRoot(string path) => path.StartsWith(_assetsRoot + Path.DirectorySeparatorChar, PathComparison);
    private bool ContainsReparsePoint(string path)
    {
        FileSystemInfo? current = new FileInfo(path);
        while (current is not null && (IsWithinAssetsRoot(current.FullName) || current.FullName.Equals(_assetsRoot, PathComparison)))
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
        }
        return false;
    }

    private static bool IsResolutionException(Exception exception) => exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException or System.Security.SecurityException or JsonException or Win32Exception;
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static RekallAgeGpuAssetDataResolution Invalid(string code, string message, string? target) => new(null, [new(code, message, target)]);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] path, uint pathLength, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    private sealed class UnsafeAssetPathException(string message) : IOException(message);
    private sealed class AssetSizeException(string message) : IOException(message);
}
