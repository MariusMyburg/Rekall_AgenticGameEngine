namespace Rekall.Age.Project;

public sealed class RekallAgeMemoryGameContent : IRekallAgeGameContent
{
    private readonly IReadOnlyDictionary<string, byte[]> _entries;

    public RekallAgeMemoryGameContent(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToDictionary(
            entry => RekallAgeGameContentPath.Normalize(entry.Key),
            entry => entry.Value.ToArray(),
            StringComparer.Ordinal);
    }

    public ValueTask<RekallAgeGameContentEntry> ReadAsync(
        string logicalPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RekallAgeGameContentPath.ValidateMaximumBytes(maximumBytes);
        var normalized = RekallAgeGameContentPath.Normalize(logicalPath);
        if (!_entries.TryGetValue(normalized, out var bytes))
        {
            throw new RekallAgeGameContentException(
                "REKALL_GAME_CONTENT_NOT_FOUND",
                normalized,
                $"Game content '{normalized}' was not found.");
        }

        if (bytes.LongLength > maximumBytes)
        {
            throw new RekallAgeGameContentException(
                "REKALL_GAME_CONTENT_TOO_LARGE",
                normalized,
                $"Game content '{normalized}' is {bytes.LongLength} bytes; the read limit is {maximumBytes} bytes.");
        }

        return ValueTask.FromResult(new RekallAgeGameContentEntry(normalized, bytes.ToArray()));
    }
}
