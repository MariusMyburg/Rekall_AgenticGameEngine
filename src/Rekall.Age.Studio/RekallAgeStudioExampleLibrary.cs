using System.IO;

namespace Rekall.Age.Studio;

internal sealed class RekallAgeStudioExampleLibrary
{
    private static readonly HashSet<string> TransientDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".rekall",
        ".vs",
        "bin",
        "obj",
        "TestResults"
    };

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Rekall AGE",
        "Examples");

    public static string FindFreshDestination(string libraryRoot, string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ValidateFolderName(folderName);
        var fullLibraryRoot = Path.GetFullPath(libraryRoot);
        var candidate = Path.Combine(fullLibraryRoot, folderName);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            candidate = Path.Combine(fullLibraryRoot, $"{folderName}-{suffix}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    public static bool IsOccupied(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.Exists(path) || File.Exists(path);
    }

    public async ValueTask CopyAsync(
        RekallAgeStudioExample example,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(example);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        var sourceRoot = Path.GetFullPath(example.SourceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destination = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(sourceRoot) || !File.Exists(Path.Combine(sourceRoot, "rekall.project.json")))
        {
            throw new DirectoryNotFoundException($"Example project source was not found at '{sourceRoot}'.");
        }

        if (IsSameOrNested(destination, sourceRoot))
        {
            throw new InvalidOperationException("A writable example copy cannot be created inside its packaged source.");
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"The example destination '{destination}' already exists.");
        }

        var destinationParent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The example destination must have a parent directory.");
        Directory.CreateDirectory(destinationParent);
        var stagingRoot = Path.Combine(
            destinationParent,
            $".{Path.GetFileName(destination)}.rekall-import-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingRoot);
            await CopyDirectoryAsync(sourceRoot, stagingRoot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingRoot, destination);
        }
        catch
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }

            throw;
        }
    }

    private static async ValueTask CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryInfo = new DirectoryInfo(directory);
            if (TransientDirectoryNames.Contains(directoryInfo.Name) ||
                directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var childDestination = Path.Combine(destinationRoot, directoryInfo.Name);
            Directory.CreateDirectory(childDestination);
            await CopyDirectoryAsync(directoryInfo.FullName, childDestination, cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(file);
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            await using var input = new FileStream(
                fileInfo.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                useAsync: true);
            await using var output = new FileStream(
                Path.Combine(destinationRoot, fileInfo.Name),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static bool IsSameOrNested(string candidate, string root)
    {
        return candidate.Equals(root, PathComparison) ||
            candidate.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }

    private static void ValidateFolderName(string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        if (folderName is "." or ".." ||
            folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            folderName.Contains(Path.DirectorySeparatorChar) ||
            folderName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("The example folder name must be a single valid directory name.", nameof(folderName));
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
