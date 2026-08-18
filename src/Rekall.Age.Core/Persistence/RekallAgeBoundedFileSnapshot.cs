namespace Rekall.Age.Core.Persistence;

public sealed class RekallAgeBoundedFileSnapshotException : IOException
{
    public RekallAgeBoundedFileSnapshotException(string code, string path, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Path = path;
    }

    public string Code { get; }

    public string Path { get; }
}

public sealed record RekallAgeBoundedFileSnapshot(string Path, byte[] Bytes)
{
    public static async ValueTask<RekallAgeBoundedFileSnapshot> ReadAsync(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumBytes < 1 || maximumBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBytes),
                maximumBytes,
                $"Maximum snapshot bytes must be between 1 and {int.MaxValue}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = System.IO.Path.GetFullPath(path);
        await using var stream = await OpenForSnapshotAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var length = stream.Length;
        if (length > maximumBytes)
        {
            throw new RekallAgeBoundedFileSnapshotException(
                "REKALL_FILE_SNAPSHOT_TOO_LARGE",
                fullPath,
                $"File '{fullPath}' is {length} bytes; the limit is {maximumBytes} bytes.");
        }

        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)length));
        try
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException error)
        {
            throw new RekallAgeBoundedFileSnapshotException(
                "REKALL_FILE_SNAPSHOT_CHANGED",
                fullPath,
                $"File '{fullPath}' changed while its bounded snapshot was being read.",
                error);
        }

        if (stream.Length != length)
        {
            throw new RekallAgeBoundedFileSnapshotException(
                "REKALL_FILE_SNAPSHOT_CHANGED",
                fullPath,
                $"File '{fullPath}' changed while its bounded snapshot was being read.");
        }

        return new RekallAgeBoundedFileSnapshot(fullPath, bytes);
    }

    private static async ValueTask<FileStream> OpenForSnapshotAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 8;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(attempt, 4)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
