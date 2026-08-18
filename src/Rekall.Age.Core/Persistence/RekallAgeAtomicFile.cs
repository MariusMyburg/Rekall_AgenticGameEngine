using System.Text;

namespace Rekall.Age.Core.Persistence;

public static class RekallAgeAtomicFile
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static async ValueTask WriteAllTextAsync(
        string path,
        string contents,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        if (maximumBytes < 1 || maximumBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBytes),
                maximumBytes,
                $"Maximum document bytes must be between 1 and {int.MaxValue}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var byteCount = Utf8WithoutBom.GetByteCount(contents);
        if (byteCount > maximumBytes)
        {
            throw new InvalidDataException(
                $"Document for '{Path.GetFullPath(path)}' is {byteCount} bytes; the limit is {maximumBytes} bytes.");
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Document path '{fullPath}' has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.tmp-{Guid.NewGuid():N}");
        var bytes = Utf8WithoutBom.GetBytes(contents);
        var published = false;
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
            published = true;
        }
        finally
        {
            if (!published && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Preserve the publication error; stale temp files remain
                    // recognizable and are never treated as live documents.
                }
            }
        }
    }
}
