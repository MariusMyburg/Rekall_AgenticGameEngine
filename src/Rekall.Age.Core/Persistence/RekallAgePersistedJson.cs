using System.Text.Json;

namespace Rekall.Age.Core.Persistence;

public static class RekallAgePersistedJson
{
    public const long MaximumDocumentBytes = 64L * 1024L * 1024L;
    public const int MaximumDocumentDepth = 128;

    public static async ValueTask<T> ReadAsync<T>(
        string path,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = await RekallAgeBoundedFileSnapshot.ReadAsync(
            path,
            MaximumDocumentBytes,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Deserialize<T>(snapshot.Bytes, options)
            ?? throw new InvalidOperationException(
                $"Document '{snapshot.Path}' could not be deserialized as {typeof(T).Name}.");
    }

    public static ValueTask WriteAllTextAsync(
        string path,
        string contents,
        CancellationToken cancellationToken) =>
        RekallAgeAtomicFile.WriteAllTextAsync(
            path,
            contents,
            MaximumDocumentBytes,
            cancellationToken);
}
