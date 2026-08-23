namespace Rekall.Age.Core.Persistence;

public static class RekallAgeConfinedPath
{
    public static string Resolve(string projectRoot, string candidatePath, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        var root = Path.GetFullPath(projectRoot);
        var candidate = Path.GetFullPath(candidatePath);
        var relative = Path.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new ArgumentException($"{description} must remain inside project root '{root}'.", nameof(candidatePath));
        }

        RejectLink(root, description);
        if (relative != ".")
        {
            var current = root;
            foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                RejectLink(current, description);
            }
        }

        return candidate;
    }

    private static void RejectLink(string path, string description)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException(
                    $"REKALL_PATH_REPARSE_REJECTED: {description} cannot traverse a filesystem link or junction ('{path}').");
            }
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            // Nonexistent descendants are safe to create once all existing ancestors passed.
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
