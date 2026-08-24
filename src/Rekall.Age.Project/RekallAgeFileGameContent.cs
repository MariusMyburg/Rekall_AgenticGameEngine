using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Project;

public sealed class RekallAgeFileGameContent : IRekallAgeGameContent
{
    private readonly string _root;

    public RekallAgeFileGameContent(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
    }

    public async ValueTask<RekallAgeGameContentEntry> ReadAsync(
        string logicalPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RekallAgeGameContentPath.ValidateMaximumBytes(maximumBytes);
        var normalized = RekallAgeGameContentPath.Normalize(logicalPath);
        var candidate = Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar));
        var path = RekallAgeConfinedPath.Resolve(_root, candidate, "Game content");

        try
        {
            var snapshot = await RekallAgeBoundedFileSnapshot.ReadAsync(
                path,
                maximumBytes,
                cancellationToken).ConfigureAwait(false);
            return new RekallAgeGameContentEntry(normalized, snapshot.Bytes);
        }
        catch (FileNotFoundException error)
        {
            throw NotFound(normalized, error);
        }
        catch (DirectoryNotFoundException error)
        {
            throw NotFound(normalized, error);
        }
        catch (RekallAgeBoundedFileSnapshotException error)
        {
            var code = error.Code == "REKALL_FILE_SNAPSHOT_TOO_LARGE"
                ? "REKALL_GAME_CONTENT_TOO_LARGE"
                : "REKALL_GAME_CONTENT_READ_CHANGED";
            throw new RekallAgeGameContentException(code, normalized, error.Message, error);
        }
    }

    private static RekallAgeGameContentException NotFound(string logicalPath, Exception error) =>
        new(
            "REKALL_GAME_CONTENT_NOT_FOUND",
            logicalPath,
            $"Game content '{logicalPath}' was not found.",
            error);

}
