namespace Rekall.Age.Project;

public interface IRekallAgeGameContent
{
    ValueTask<RekallAgeGameContentEntry> ReadAsync(
        string logicalPath,
        long maximumBytes,
        CancellationToken cancellationToken);
}

public sealed record RekallAgeGameContentEntry(
    string LogicalPath,
    ReadOnlyMemory<byte> Bytes);

public sealed class RekallAgeGameContentException : IOException
{
    public RekallAgeGameContentException(string code, string logicalPath, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        LogicalPath = logicalPath;
    }

    public string Code { get; }

    public string LogicalPath { get; }
}

public static class RekallAgeGameContentPath
{
    public static string Normalize(string logicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        var normalized = logicalPath.Replace('\\', '/');
        if (normalized.StartsWith('/')
            || Path.IsPathRooted(logicalPath))
        {
            throw new ArgumentException("Game-content paths must be relative logical paths.", nameof(logicalPath));
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException("Game-content paths must contain only non-empty safe segments.", nameof(logicalPath));
        }

        return string.Join('/', segments);
    }

    internal static void ValidateMaximumBytes(long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, int.MaxValue);
    }
}
