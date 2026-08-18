using System.Text.Json;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Core.Compatibility;

public sealed record RekallAgeDocumentSchema(
    int DetectedVersion,
    int CurrentVersion,
    bool IsLegacy);

public sealed record RekallAgeDocumentSnapshot(
    RekallAgeDocumentSchema Schema,
    RekallAgeBoundedFileSnapshot File)
{
    public T Deserialize<T>(JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(File.Bytes, options)
        ?? throw new InvalidOperationException($"Document '{File.Path}' could not be deserialized as {typeof(T).Name}.");
}

public static class RekallAgeDocumentSchemaProbe
{
    public const long MaximumDocumentBytes = 64L * 1024L * 1024L;
    public const int MaximumDocumentDepth = 128;

    public static async ValueTask<RekallAgeDocumentSchema> ReadAsync(
        string documentPath,
        string documentKind,
        int currentVersion,
        CancellationToken cancellationToken) =>
        (await ReadSnapshotAsync(
            documentPath,
            documentKind,
            currentVersion,
            cancellationToken).ConfigureAwait(false)).Schema;

    public static async ValueTask<RekallAgeDocumentSnapshot> ReadSnapshotAsync(
        string documentPath,
        string documentKind,
        int currentVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentVersion, 1);

        var fullPath = Path.GetFullPath(documentPath);
        RekallAgeBoundedFileSnapshot snapshot;
        try
        {
            snapshot = await RekallAgeBoundedFileSnapshot.ReadAsync(
                fullPath,
                MaximumDocumentBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RekallAgeBoundedFileSnapshotException error)
        {
            var code = error.Code == "REKALL_FILE_SNAPSHOT_TOO_LARGE"
                ? "REKALL_DOCUMENT_TOO_LARGE"
                : "REKALL_DOCUMENT_READ_CHANGED";
            throw Failure(code, documentKind, fullPath, null, currentVersion, error.Message, error);
        }

        JsonDocument document;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            document = JsonDocument.Parse(
                snapshot.Bytes,
                new JsonDocumentOptions { MaxDepth = MaximumDocumentDepth });
        }
        catch (JsonException error)
        {
            throw Failure(
                "REKALL_DOCUMENT_JSON_MALFORMED",
                documentKind,
                fullPath,
                null,
                currentVersion,
                $"{documentKind} document '{fullPath}' is not valid bounded JSON.",
                error);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Failure(
                    "REKALL_DOCUMENT_SCHEMA_INVALID",
                    documentKind,
                    fullPath,
                    null,
                    currentVersion,
                    $"{documentKind} document '{fullPath}' must have a JSON object root.");
            }

            var schemaProperties = document.RootElement
                .EnumerateObject()
                .Where(property => property.NameEquals("schemaVersion"))
                .ToArray();
            if (schemaProperties.Length == 0)
            {
                return new RekallAgeDocumentSnapshot(
                    new RekallAgeDocumentSchema(0, currentVersion, IsLegacy: true),
                    snapshot);
            }

            if (schemaProperties.Length != 1 ||
                schemaProperties[0].Value.ValueKind != JsonValueKind.Number ||
                !schemaProperties[0].Value.TryGetInt32(out var detectedVersion) ||
                detectedVersion < 0)
            {
                throw Failure(
                    "REKALL_DOCUMENT_SCHEMA_INVALID",
                    documentKind,
                    fullPath,
                    null,
                    currentVersion,
                    $"{documentKind} document '{fullPath}' must contain one non-negative integer schemaVersion.");
            }

            if (detectedVersion > currentVersion)
            {
                throw Failure(
                    "REKALL_DOCUMENT_SCHEMA_FUTURE",
                    documentKind,
                    fullPath,
                    detectedVersion,
                    currentVersion,
                    $"{documentKind} document '{fullPath}' uses future schema {detectedVersion}; this engine supports through schema {currentVersion}.");
            }

            return new RekallAgeDocumentSnapshot(
                new RekallAgeDocumentSchema(
                    detectedVersion,
                    currentVersion,
                    IsLegacy: detectedVersion < currentVersion),
                snapshot);
        }
    }

    private static RekallAgeDocumentCompatibilityException Failure(
        string code,
        string documentKind,
        string documentPath,
        int? detectedVersion,
        int currentVersion,
        string message,
        Exception? innerException = null)
    {
        return new RekallAgeDocumentCompatibilityException(
            code,
            documentKind,
            documentPath,
            detectedVersion,
            currentVersion,
            message,
            innerException);
    }
}
