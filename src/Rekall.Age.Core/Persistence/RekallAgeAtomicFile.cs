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
        var prepared = Prepare(path, contents, maximumBytes, cancellationToken);
        await using var documentLock = await AcquireLockAsync(prepared.FullPath, cancellationToken).ConfigureAwait(false);
        await WritePreparedAsync(prepared, previousVersionPath: null, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<string> WriteAllTextIfRevisionAsync(
        string path,
        string contents,
        long maximumBytes,
        string expectedRevision,
        CancellationToken cancellationToken) =>
        await WriteAllTextIfRevisionAsync(
            path,
            contents,
            maximumBytes,
            expectedRevision,
            previousVersionPath: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<string> WriteAllTextIfRevisionAsync(
        string path,
        string contents,
        long maximumBytes,
        string expectedRevision,
        string? previousVersionPath,
        CancellationToken cancellationToken)
    {
        var bytes = PrepareBytes(contents, maximumBytes, cancellationToken);
        return await WriteAllBytesIfRevisionAsync(
            path,
            bytes,
            maximumBytes,
            expectedRevision,
            previousVersionPath,
            cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<string> WriteAllBytesIfRevisionAsync(
        string path,
        ReadOnlyMemory<byte> contents,
        long maximumBytes,
        string expectedRevision,
        string? previousVersionPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        if (!RekallAgeDocumentRevision.IsValid(expectedRevision))
        {
            throw new ArgumentException("Expected revision must be 'missing' or a lowercase SHA-256 token.", nameof(expectedRevision));
        }

        var prepared = Prepare(path, contents, maximumBytes, cancellationToken);
        await using var documentLock = await AcquireLockAsync(prepared.FullPath, cancellationToken).ConfigureAwait(false);
        var currentRevision = File.Exists(prepared.FullPath)
            ? (await RekallAgeBoundedFileSnapshot.ReadAsync(
                prepared.FullPath,
                maximumBytes,
                cancellationToken).ConfigureAwait(false)).Revision
            : RekallAgeDocumentRevision.Missing;
        if (!currentRevision.Equals(expectedRevision, StringComparison.Ordinal))
        {
            throw new RekallAgeDocumentRevisionException(
                "REKALL_DOCUMENT_REVISION_CONFLICT",
                prepared.FullPath,
                $"Document '{prepared.FullPath}' changed: expected revision '{expectedRevision}', current revision '{currentRevision}'. Reload the document, reapply the semantic change, and retry.",
                expectedRevision,
                currentRevision);
        }

        string? fullPreviousVersionPath = null;
        if (currentRevision != RekallAgeDocumentRevision.Missing && previousVersionPath is not null)
        {
            fullPreviousVersionPath = Path.GetFullPath(previousVersionPath);
            if (fullPreviousVersionPath.Equals(prepared.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Previous-version path must differ from the destination.", nameof(previousVersionPath));
            }
            if (!string.Equals(
                Path.GetPathRoot(fullPreviousVersionPath),
                Path.GetPathRoot(prepared.FullPath),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Previous-version path must be on the destination volume.", nameof(previousVersionPath));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(fullPreviousVersionPath)!);
        }

        await WritePreparedAsync(prepared, fullPreviousVersionPath, cancellationToken).ConfigureAwait(false);
        return RekallAgeDocumentRevision.Compute(prepared.Bytes);
    }

    public static async ValueTask DeleteIfRevisionAsync(
        string path,
        long maximumBytes,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateMaximumBytes(maximumBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        if (!RekallAgeDocumentRevision.IsValid(expectedRevision))
        {
            throw new ArgumentException("Expected revision must be 'missing' or a lowercase SHA-256 token.", nameof(expectedRevision));
        }

        var fullPath = Path.GetFullPath(path);
        await using var documentLock = await AcquireLockAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var currentRevision = File.Exists(fullPath)
            ? (await RekallAgeBoundedFileSnapshot.ReadAsync(
                fullPath,
                maximumBytes,
                cancellationToken).ConfigureAwait(false)).Revision
            : RekallAgeDocumentRevision.Missing;
        if (!string.Equals(currentRevision, expectedRevision, StringComparison.Ordinal))
        {
            throw new RekallAgeDocumentRevisionException(
                "REKALL_DOCUMENT_REVISION_CONFLICT",
                fullPath,
                $"Document '{fullPath}' changed: expected revision '{expectedRevision}', current revision '{currentRevision}'. Reload the document, reapply the semantic change, and retry.",
                expectedRevision,
                currentRevision);
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public static string GetLockPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Document path '{fullPath}' has no parent directory.");
        return Path.Combine(directory, $".{Path.GetFileName(fullPath)}.lock");
    }

    private static PreparedDocument Prepare(
        string path,
        string contents,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return Prepare(path, PrepareBytes(contents, maximumBytes, cancellationToken), maximumBytes, cancellationToken);
    }

    private static byte[] PrepareBytes(
        string contents,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ValidateMaximumBytes(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Utf8WithoutBom.GetBytes(contents);
        if (bytes.LongLength > maximumBytes)
        {
            throw new InvalidDataException($"Document is {bytes.LongLength} bytes; the limit is {maximumBytes} bytes.");
        }
        return bytes;
    }

    private static PreparedDocument Prepare(
        string path,
        ReadOnlyMemory<byte> contents,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateMaximumBytes(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        var bytes = contents.ToArray();
        if (bytes.LongLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"Document for '{fullPath}' is {bytes.LongLength} bytes; the limit is {maximumBytes} bytes.");
        }

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Document path '{fullPath}' has no parent directory.");
        Directory.CreateDirectory(directory);
        return new PreparedDocument(fullPath, directory, bytes);
    }

    private static void ValidateMaximumBytes(long maximumBytes)
    {
        if (maximumBytes < 1 || maximumBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBytes),
                maximumBytes,
                $"Maximum document bytes must be between 1 and {int.MaxValue}.");
        }
    }

    private static async ValueTask WritePreparedAsync(
        PreparedDocument prepared,
        string? previousVersionPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(
            prepared.Directory,
            $".{Path.GetFileName(prepared.FullPath)}.tmp-{Guid.NewGuid():N}");
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
                await stream.WriteAsync(prepared.Bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await PublishAsync(
                temporaryPath,
                prepared.FullPath,
                previousVersionPath,
                cancellationToken).ConfigureAwait(false);
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

    private static async ValueTask<FileStream> AcquireLockAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 64;
        var lockPath = GetLockPath(destinationPath);
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException &&
                attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(attempt, 8)), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new RekallAgeDocumentRevisionException(
                    "REKALL_DOCUMENT_BUSY",
                    destinationPath,
                    $"Document '{destinationPath}' remained busy after {maximumAttempts} bounded lock attempts.",
                    RekallAgeDocumentRevision.Missing,
                    RekallAgeDocumentRevision.Missing);
            }
        }
    }

    private static async ValueTask PublishAsync(
        string temporaryPath,
        string destinationPath,
        string? previousVersionPath,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 16;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Replace(temporaryPath, destinationPath, previousVersionPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
                return;
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException &&
                attempt < maximumAttempts &&
                File.Exists(temporaryPath))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(attempt, 4)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private sealed record PreparedDocument(string FullPath, string Directory, byte[] Bytes);
}
