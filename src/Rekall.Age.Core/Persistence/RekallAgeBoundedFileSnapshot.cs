namespace Rekall.Age.Core.Persistence;

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
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        if (length > maximumBytes)
        {
            throw new InvalidDataException(
                $"File '{fullPath}' is {length} bytes; the limit is {maximumBytes} bytes.");
        }

        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)length));
        try
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidDataException(
                $"File '{fullPath}' changed while its bounded snapshot was being read.",
                error);
        }

        if (stream.Length != length)
        {
            throw new InvalidDataException(
                $"File '{fullPath}' changed while its bounded snapshot was being read.");
        }

        return new RekallAgeBoundedFileSnapshot(fullPath, bytes);
    }
}
