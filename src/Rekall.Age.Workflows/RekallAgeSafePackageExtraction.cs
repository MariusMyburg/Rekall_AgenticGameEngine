using System.IO.Compression;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Workflows;

internal static class RekallAgeSafePackageExtraction
{
    public static void Extract(string archivePath, string destinationRoot)
    {
        var destination = Path.GetFullPath(destinationRoot);
        Directory.CreateDirectory(destination);
        var destinationPrefix = destination.EndsWith(Path.DirectorySeparatorChar)
            ? destination
            : destination + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > InspectPlayablePackageCommand.MaximumArchiveEntries)
        {
            throw new InvalidDataException("Package archive exceeds the supported entry-count limit.");
        }

        var totalLength = 0L;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > InspectPlayablePackageCommand.MaximumEntrySizeBytes ||
                totalLength > InspectPlayablePackageCommand.MaximumPackageSizeBytes - entry.Length)
            {
                throw new InvalidDataException("Package archive exceeds the supported uncompressed-size limit.");
            }

            totalLength += entry.Length;
            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
            var path = isDirectory ? entry.FullName.TrimEnd('/') : entry.FullName;
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (!InspectPlayablePackageCommand.TryValidateRelativePath(path, out var normalized))
            {
                throw new InvalidDataException($"Package archive contains unsafe path '{entry.FullName}'.");
            }

            var target = Path.GetFullPath(Path.Combine(destination, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase) ||
                !destinations.Add(target))
            {
                throw new InvalidDataException($"Package archive contains a duplicate, colliding, or escaping path '{entry.FullName}'.");
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
        }
    }
}
