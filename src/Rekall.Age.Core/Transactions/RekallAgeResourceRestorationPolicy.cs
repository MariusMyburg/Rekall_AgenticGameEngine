using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Core.Transactions;

public interface IRekallAgeAppendOnlyResourceClassifier
{
    bool IsAppendOnly(string projectRoot, string confinedResourcePath);
}

public interface IRekallAgeResourceRestorationPolicy
{
    RekallAgeResourceRestorationAdmission Admit(string projectRoot, string resourcePath);

    void Revalidate(RekallAgeResourceRestorationAdmission admission);
}

public sealed record RekallAgeResourceRestorationAdmission(
    string ProjectRoot,
    string Path,
    RekallAgeFileIdentity? FileIdentity);

public sealed record RekallAgeFileIdentity(
    ulong Device,
    ulong FileId,
    uint LinkCount);

public sealed class RekallAgeResourceRestorationException : RekallAgeCodedBoundaryException
{
    public const string ProtectedCode = "REKALL_RESOURCE_RESTORE_PROTECTED";
    public const string PathInvalidCode = "REKALL_RESOURCE_RESTORE_PATH_INVALID";

    public RekallAgeResourceRestorationException(
        string code,
        string message,
        string target,
        Exception? innerException = null)
        : base(code, message, target, innerException)
    {
    }
}

public sealed class RekallAgeResourceRestorationPolicy(
    params IRekallAgeAppendOnlyResourceClassifier[] appendOnlyClassifiers)
    : IRekallAgeResourceRestorationPolicy
{
    private readonly IReadOnlyList<IRekallAgeAppendOnlyResourceClassifier> _appendOnlyClassifiers =
        appendOnlyClassifiers?.ToArray()
        ?? throw new ArgumentNullException(nameof(appendOnlyClassifiers));

    public RekallAgeResourceRestorationAdmission Admit(string projectRoot, string resourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        try
        {
            var root = Path.GetFullPath(projectRoot);
            var candidate = Path.IsPathRooted(resourcePath)
                ? resourcePath
                : Path.Combine(root, resourcePath);
            var confined = RekallAgeConfinedPath.Resolve(root, candidate, "Transaction restoration target");
            if (_appendOnlyClassifiers.Any(classifier => classifier.IsAppendOnly(root, confined)))
            {
                throw new RekallAgeResourceRestorationException(
                    RekallAgeResourceRestorationException.ProtectedCode,
                    $"Resource '{resourcePath}' is append-only and cannot be deleted or overwritten by transaction restoration.",
                    confined);
            }

            var identity = RekallAgeFileIdentityInspector.Inspect(confined);
            RejectMultipleLinks(confined, identity);
            return new(root, confined, identity);
        }
        catch (RekallAgeResourceRestorationException)
        {
            throw;
        }
        catch (Exception error) when (
            error is ArgumentException
                or InvalidDataException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            throw new RekallAgeResourceRestorationException(
                RekallAgeResourceRestorationException.PathInvalidCode,
                $"Resource '{resourcePath}' is not a confined restorable project path.",
                resourcePath,
                error);
        }
    }

    public void Revalidate(RekallAgeResourceRestorationAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        var current = Admit(admission.ProjectRoot, admission.Path);
        if (!Equals(current.FileIdentity, admission.FileIdentity))
        {
            throw new RekallAgeResourceRestorationException(
                RekallAgeResourceRestorationException.PathInvalidCode,
                $"Resource '{admission.Path}' changed filesystem identity during transaction restoration.",
                admission.Path);
        }
    }

    private static void RejectMultipleLinks(string path, RekallAgeFileIdentity? identity)
    {
        if (identity is { LinkCount: > 1 })
        {
            throw new RekallAgeResourceRestorationException(
                RekallAgeResourceRestorationException.ProtectedCode,
                $"Resource '{path}' has multiple filesystem hard links and cannot be safely restored.",
                path);
        }
    }
}

internal static class RekallAgeFileIdentityInspector
{
    private const int AtFileWorkingDirectory = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxBasicStats = 0x07ff;

    public static RekallAgeFileIdentity? Inspect(string path)
    {
        if (!File.Exists(path))
        {
            if (Directory.Exists(path))
            {
                throw new InvalidDataException($"Transaction restoration target '{path}' must be a file.");
            }

            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            using var handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.None);
            if (!GetFileInformationByHandle(handle, out var information))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new(
                information.VolumeSerialNumber,
                ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow,
                information.NumberOfLinks);
        }

        if (OperatingSystem.IsLinux())
        {
            if (Statx(
                    AtFileWorkingDirectory,
                    path,
                    AtSymlinkNoFollow,
                    StatxBasicStats,
                    out var information) != 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return new(
                ((ulong)information.DeviceMajor << 32) | information.DeviceMinor,
                information.Inode,
                information.LinkCount);
        }

        throw new PlatformNotSupportedException(
            "Transaction restoration filesystem identity admission supports Windows and Linux.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        int directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out LinuxStatx information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStatxTimestamp
    {
        public long Seconds;
        public uint Nanoseconds;
        public int Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStatx
    {
        public uint Mask;
        public uint BlockSize;
        public ulong Attributes;
        public uint LinkCount;
        public uint UserId;
        public uint GroupId;
        public ushort Mode;
        public ushort Spare0;
        public ulong Inode;
        public ulong Size;
        public ulong Blocks;
        public ulong AttributesMask;
        public LinuxStatxTimestamp AccessTime;
        public LinuxStatxTimestamp BirthTime;
        public LinuxStatxTimestamp ChangeTime;
        public LinuxStatxTimestamp ModificationTime;
        public uint DeviceIdMajor;
        public uint DeviceIdMinor;
        public uint DeviceMajor;
        public uint DeviceMinor;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 14)]
        public ulong[] Spare;
    }
}
