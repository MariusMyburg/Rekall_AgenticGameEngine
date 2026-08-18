using System.Buffers;
using System.IO.Compression;

namespace Rekall.Age.Workflows;

internal static class RekallAgeSafePackageExtraction
{
    private const int CopyBufferBytes = 64 * 1024;

    public static void Extract(
        string archivePath,
        string destinationRoot,
        RekallAgePackageArchiveLimits? limits = null,
        Func<string, FileAttributes>? getAttributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        var source = Path.GetFullPath(archivePath);
        var destination = Path.GetFullPath(destinationRoot);
        var attributes = getAttributes ?? File.GetAttributes;
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            throw Failure(
                "REKALL_PACKAGE_PATH_REPARSE_POINT",
                "Package archive sources must not be reparse points.",
                source);
        }

        using var archive = ZipFile.OpenRead(source);
        var plan = RekallAgePackageArchivePreflight.Inspect(archive, limits);

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw Failure(
                "REKALL_PACKAGE_EXTRACTION_DESTINATION_EXISTS",
                "Package extraction requires a destination path that does not already exist.",
                destination);
        }

        var parent = Path.GetDirectoryName(destination)
            ?? throw Failure(
                "REKALL_PACKAGE_EXTRACTION_DESTINATION_UNSAFE",
                "Package extraction destination must have a parent directory.",
                destination);
        ValidateExistingBoundary(parent, attributes);
        Directory.CreateDirectory(parent);
        ValidateExistingBoundary(parent, attributes);

        var staging = Path.Combine(parent, $".rekall-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            ExtractPlan(plan, staging, attributes);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    internal static void CopyExactly(Stream source, Stream destination, long expectedBytes, string target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedBytes);

        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            var remaining = expectedBytes;
            while (remaining > 0)
            {
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = source.Read(buffer, 0, requested);
                if (read == 0)
                {
                    throw LengthMismatch(target, expectedBytes);
                }

                destination.Write(buffer, 0, read);
                remaining -= read;
            }

            if (source.ReadByte() != -1)
            {
                throw LengthMismatch(target, expectedBytes);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ExtractPlan(
        RekallAgePackageArchivePlan plan,
        string stagingRoot,
        Func<string, FileAttributes> getAttributes)
    {
        var prefix = stagingRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        foreach (var item in plan.Entries)
        {
            var relative = item.IsDirectory ? item.NormalizedPath[..^1] : item.NormalizedPath;
            var target = Path.GetFullPath(Path.Combine(
                stagingRoot,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix, PathComparison))
            {
                throw Failure(
                    "REKALL_PACKAGE_EXTRACTION_DESTINATION_UNSAFE",
                    "Package extraction target escaped its staging directory.",
                    item.NormalizedPath);
            }

            if (item.IsDirectory)
            {
                Directory.CreateDirectory(target);
                ValidateExistingBoundary(target, getAttributes, stagingRoot);
                continue;
            }

            var targetParent = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(targetParent);
            ValidateExistingBoundary(targetParent, getAttributes, stagingRoot);
            using var input = item.Entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            CopyExactly(input, output, item.UncompressedBytes, item.NormalizedPath);
        }
    }

    private static void ValidateExistingBoundary(
        string path,
        Func<string, FileAttributes> getAttributes,
        string? stopAt = null)
    {
        var current = Path.GetFullPath(path);
        var stop = stopAt is null ? null : Path.GetFullPath(stopAt);
        while (true)
        {
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (getAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw Failure(
                    "REKALL_PACKAGE_EXTRACTION_DESTINATION_REPARSE",
                    "Package extraction destinations must not cross reparse points.",
                    current);
            }

            if (stop is not null && current.Equals(stop, PathComparison))
            {
                return;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent.Equals(current, PathComparison))
            {
                return;
            }

            current = parent;
        }
    }

    private static RekallAgePackageArchiveException LengthMismatch(string target, long expectedBytes) =>
        Failure(
            "REKALL_PACKAGE_ARCHIVE_ENTRY_LENGTH_MISMATCH",
            $"Package archive entry did not stream exactly its declared {expectedBytes} bytes.",
            target);

    private static RekallAgePackageArchiveException Failure(string code, string message, string target) =>
        new(code, message, target);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
