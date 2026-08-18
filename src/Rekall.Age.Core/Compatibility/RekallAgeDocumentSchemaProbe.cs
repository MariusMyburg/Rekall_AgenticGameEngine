using System.Text.Json;

namespace Rekall.Age.Core.Compatibility;

public sealed record RekallAgeDocumentSchema(
    int DetectedVersion,
    int CurrentVersion,
    bool IsLegacy);

public static class RekallAgeDocumentSchemaProbe
{
    public const long MaximumDocumentBytes = 64L * 1024L * 1024L;

    public static async ValueTask<RekallAgeDocumentSchema> ReadAsync(
        string documentPath,
        string documentKind,
        int currentVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentVersion, 1);

        var fullPath = Path.GetFullPath(documentPath);
        var length = new FileInfo(fullPath).Length;
        if (length > MaximumDocumentBytes)
        {
            throw Failure(
                "REKALL_DOCUMENT_TOO_LARGE",
                documentKind,
                fullPath,
                null,
                currentVersion,
                $"{documentKind} document '{fullPath}' is {length} bytes; the limit is {MaximumDocumentBytes} bytes.");
        }

        JsonDocument document;
        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions { MaxDepth = 128 },
                cancellationToken);
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
                return new RekallAgeDocumentSchema(0, currentVersion, IsLegacy: true);
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

            return new RekallAgeDocumentSchema(
                detectedVersion,
                currentVersion,
                IsLegacy: detectedVersion < currentVersion);
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
