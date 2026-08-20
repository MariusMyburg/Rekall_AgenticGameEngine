using System.Buffers.Binary;
using System.Text.Json;

namespace Rekall.Age.Modules.Hosting;

public sealed class RekallAgeModuleHostFrameCodec
{
    private readonly int _maximumMessageBytes;
    private readonly JsonSerializerOptions _jsonOptions;
    private long _lastReadSequence;

    public RekallAgeModuleHostFrameCodec(
        int maximumMessageBytes = RekallAgeModuleHostProtocol.MaximumMessageBytes,
        int maximumJsonDepth = RekallAgeModuleHostProtocol.MaximumJsonDepth)
    {
        if (maximumMessageBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
        }

        if (maximumJsonDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumJsonDepth));
        }

        _maximumMessageBytes = maximumMessageBytes;
        _jsonOptions = new JsonSerializerOptions(RekallAgeModuleHostJson.Options)
        {
            MaxDepth = maximumJsonDepth
        };
    }

    public async ValueTask WriteAsync(
        Stream stream,
        RekallAgeModuleHostEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateEnvelope(envelope, validateSequence: false);
        byte[] payload;
        try
        {
            payload = JsonSerializer.SerializeToUtf8Bytes(envelope, _jsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw Invalid("Module-host envelope could not be serialized safely.", ex);
        }

        if (payload.Length > _maximumMessageBytes)
        {
            throw new RekallAgeModuleHostException(
                "REKALL_MODULE_HOST_MESSAGE_TOO_LARGE",
                $"Module-host message exceeds the {_maximumMessageBytes}-byte limit.");
        }

        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async ValueTask<RekallAgeModuleHostEnvelope> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, prefix, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0)
        {
            throw Invalid("Module-host frame length must be positive.");
        }

        if (length > _maximumMessageBytes)
        {
            throw new RekallAgeModuleHostException(
                "REKALL_MODULE_HOST_MESSAGE_TOO_LARGE",
                $"Module-host message exceeds the {_maximumMessageBytes}-byte limit.");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken);
        RekallAgeModuleHostEnvelope? envelope;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                MaxDepth = _jsonOptions.MaxDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || HasDuplicateRootProperties(document.RootElement))
            {
                throw new JsonException("The module-host envelope must be one object with unique root properties.");
            }

            envelope = JsonSerializer.Deserialize<RekallAgeModuleHostEnvelope>(payload, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw Invalid("Module-host frame is not one bounded JSON envelope.", ex);
        }

        if (envelope is null)
        {
            throw Invalid("Module-host frame contains a null envelope.");
        }

        ValidateEnvelope(envelope, validateSequence: true);
        _lastReadSequence = envelope.Sequence;
        return envelope;
    }

    private void ValidateEnvelope(RekallAgeModuleHostEnvelope envelope, bool validateSequence)
    {
        if (envelope.ProtocolVersion != RekallAgeModuleHostProtocol.Version)
        {
            throw Invalid($"Unsupported module-host protocol version '{envelope.ProtocolVersion}'.");
        }

        if (!RekallAgeModuleHostOperations.IsKnown(envelope.Operation))
        {
            throw Invalid("Module-host operation is missing or unknown.");
        }

        if (envelope.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw Invalid("Module-host payload is required.");
        }

        if ((envelope.Ok is null && envelope.Error is not null)
            || (envelope.Ok is true && envelope.Error is not null)
            || (envelope.Ok is false && envelope.Error is null))
        {
            throw Invalid("Module-host success and error fields are inconsistent.");
        }

        if (validateSequence && envelope.Sequence != checked(_lastReadSequence + 1))
        {
            throw Invalid($"Module-host sequence '{envelope.Sequence}' is out of order.");
        }

        if (!validateSequence && envelope.Sequence < 1)
        {
            throw Invalid("Module-host sequence must be positive.");
        }
    }

    private static bool HasDuplicateRootProperties(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return root.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (count == 0)
            {
                throw Invalid(
                    "Module-host frame ended before its declared length.",
                    new EndOfStreamException("The module-host transport closed."));
            }

            read += count;
        }
    }

    private static RekallAgeModuleHostException Invalid(string message, Exception? innerException = null) => new(
        "REKALL_MODULE_HOST_PROTOCOL_INVALID",
        message,
        innerException: innerException);
}
