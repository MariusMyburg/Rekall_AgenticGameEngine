using System.IO.Compression;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Workflows;

internal sealed record RekallAgePackageArchiveLimits(
    int MaximumEntries,
    long MaximumEntryBytes,
    long MaximumTotalBytes,
    long MaximumManifestBytes)
{
    public static RekallAgePackageArchiveLimits Default { get; } = new(
        InspectPlayablePackageCommand.MaximumArchiveEntries,
        InspectPlayablePackageCommand.MaximumEntrySizeBytes,
        InspectPlayablePackageCommand.MaximumPackageSizeBytes,
        4L * 1024 * 1024);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumManifestBytes);
        if (MaximumManifestBytes > MaximumEntryBytes)
        {
            throw new ArgumentException("Archive limits must satisfy manifest <= entry bytes.");
        }
    }
}

internal sealed record RekallAgePackageArchiveEntryPlan(
    ZipArchiveEntry Entry,
    string NormalizedPath,
    bool IsDirectory,
    long UncompressedBytes,
    long CompressedBytes);

internal sealed record RekallAgePackageArchivePlan(
    RekallAgePackageArchiveEntryPlan Manifest,
    IReadOnlyList<RekallAgePackageArchiveEntryPlan> Entries,
    int EntryCount,
    long TotalUncompressedBytes,
    long TotalCompressedBytes);

internal sealed class RekallAgePackageArchiveException : Exception
{
    public RekallAgePackageArchiveException(string code, string message, string target)
        : base(message)
    {
        Code = code;
        Target = target;
    }

    public string Code { get; }

    public string Target { get; }
}

internal static class RekallAgePackageArchivePreflight
{
    private const int MaximumPathChars = 1024;
    private const int MaximumSegmentChars = 255;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixDirectory = 0x4000;
    private const int UnixRegularFile = 0x8000;
    private static readonly char[] WindowsInvalidNameChars = ['<', '>', '"', '|', '?', '*'];
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static RekallAgePackageArchivePlan Inspect(
        ZipArchive archive,
        RekallAgePackageArchiveLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var appliedLimits = limits ?? RekallAgePackageArchiveLimits.Default;
        appliedLimits.Validate();

        if (archive.Entries.Count > appliedLimits.MaximumEntries)
        {
            throw Failure(
                "REKALL_PACKAGE_ARCHIVE_LIMIT_EXCEEDED",
                $"Package archive contains {archive.Entries.Count} entries; the limit is {appliedLimits.MaximumEntries}.",
                "archive");
        }

        var entries = new List<RekallAgePackageArchiveEntryPlan>(archive.Entries.Count);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regularFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestCount = 0;
        long totalUncompressed = 0;
        long totalCompressed = 0;
        foreach (var entry in archive.Entries)
        {
            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
            var candidate = isDirectory ? entry.FullName[..^1] : entry.FullName;
            if (!TryValidatePath(candidate, out var normalized))
            {
                throw Failure(
                    "REKALL_PACKAGE_ARCHIVE_PATH_UNSAFE",
                    "Package archive paths must be bounded, normalized, relative, unambiguous, and traversal-free.",
                    entry.FullName);
            }

            ValidateEntryType(entry, isDirectory, normalized);
            if (entry.Length < 0 || entry.CompressedLength < 0 || isDirectory && entry.Length != 0)
            {
                throw Failure(
                    "REKALL_PACKAGE_ARCHIVE_METADATA_INVALID",
                    "Package archive entry metadata is inconsistent.",
                    entry.FullName);
            }

            if (entry.Length > appliedLimits.MaximumEntryBytes ||
                totalUncompressed > appliedLimits.MaximumTotalBytes - entry.Length)
            {
                throw Failure(
                    "REKALL_PACKAGE_ARCHIVE_LIMIT_EXCEEDED",
                    "Package archive exceeds the configured per-entry or total uncompressed-size limit.",
                    entry.FullName);
            }

            if (long.MaxValue - totalCompressed < entry.CompressedLength)
            {
                throw Failure(
                    "REKALL_PACKAGE_ARCHIVE_METADATA_INVALID",
                    "Package archive compressed-size metadata overflowed the supported range.",
                    entry.FullName);
            }

            totalUncompressed += entry.Length;
            totalCompressed += entry.CompressedLength;
            if (!isDirectory && normalized.Equals("rekall.package.json", StringComparison.Ordinal))
            {
                manifestCount++;
                if (manifestCount > 1)
                {
                    throw Failure(
                        "REKALL_PACKAGE_ARCHIVE_MANIFEST_DUPLICATE",
                        "Package archive contains more than one root manifest.",
                        entry.FullName);
                }
            }

            if (!targets.Add(normalized))
            {
                throw Failure(
                    "REKALL_PACKAGE_ARCHIVE_PATH_COLLISION",
                    "Package archive contains duplicate or case-colliding target paths.",
                    entry.FullName);
            }

            if (!isDirectory)
            {
                regularFiles.Add(normalized);
            }

            entries.Add(new RekallAgePackageArchiveEntryPlan(
                entry,
                isDirectory ? normalized + "/" : normalized,
                isDirectory,
                entry.Length,
                entry.CompressedLength));
        }

        foreach (var entry in entries)
        {
            var path = entry.IsDirectory ? entry.NormalizedPath[..^1] : entry.NormalizedPath;
            var separator = path.IndexOf('/');
            while (separator >= 0)
            {
                var ancestor = path[..separator];
                if (regularFiles.Contains(ancestor))
                {
                    throw Failure(
                        "REKALL_PACKAGE_ARCHIVE_PATH_ANCESTOR_CONFLICT",
                        $"Package archive file '{ancestor}' conflicts with descendant '{entry.NormalizedPath}'.",
                        entry.NormalizedPath);
                }

                separator = path.IndexOf('/', separator + 1);
            }
        }

        var manifests = entries
            .Where(item => !item.IsDirectory && item.NormalizedPath.Equals("rekall.package.json", StringComparison.Ordinal))
            .ToArray();
        if (manifests.Length == 0)
        {
            throw Failure(
                "REKALL_PACKAGE_ARCHIVE_MANIFEST_MISSING",
                "Package archive must contain exactly one root rekall.package.json regular file.",
                "rekall.package.json");
        }

        var manifest = manifests[0];
        if (manifest.UncompressedBytes > appliedLimits.MaximumManifestBytes)
        {
            throw Failure(
                "REKALL_PACKAGE_ARCHIVE_MANIFEST_TOO_LARGE",
                $"Package manifest is {manifest.UncompressedBytes} bytes; the limit is {appliedLimits.MaximumManifestBytes} bytes.",
                manifest.NormalizedPath);
        }

        var ordered = entries
            .OrderBy(item => item == manifest ? 0 : 1)
            .ThenBy(item => item.NormalizedPath, StringComparer.Ordinal)
            .ToArray();
        return new RekallAgePackageArchivePlan(
            manifest,
            ordered,
            ordered.Length,
            totalUncompressed,
            totalCompressed);
    }

    private static bool TryValidatePath(string path, out string normalized)
    {
        normalized = path;
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > MaximumPathChars ||
            path.Contains('\\') ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            path.Any(char.IsControl))
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length is 0 or > MaximumSegmentChars ||
                segment is "." or ".." ||
                segment.Contains(':') ||
                segment.IndexOfAny(WindowsInvalidNameChars) >= 0 ||
                segment.EndsWith(".", StringComparison.Ordinal) ||
                segment.EndsWith(" ", StringComparison.Ordinal) ||
                WindowsReservedNames.Contains(segment.Split('.', 2)[0]))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateEntryType(ZipArchiveEntry entry, bool isDirectory, string target)
    {
        var attributes = unchecked((uint)entry.ExternalAttributes);
        var unixType = (int)((attributes >> 16) & UnixFileTypeMask);
        var windowsAttributes = (FileAttributes)(attributes & 0xFFFF);
        if ((windowsAttributes & FileAttributes.ReparsePoint) != 0 ||
            unixType != 0 && unixType != UnixRegularFile && unixType != UnixDirectory ||
            isDirectory && unixType == UnixRegularFile ||
            !isDirectory && unixType == UnixDirectory)
        {
            throw Failure(
                "REKALL_PACKAGE_ARCHIVE_ENTRY_SPECIAL",
                "Package archives may contain only regular files and directories, never links or special files.",
                target);
        }
    }

    private static RekallAgePackageArchiveException Failure(string code, string message, string target) =>
        new(code, message, target);
}
