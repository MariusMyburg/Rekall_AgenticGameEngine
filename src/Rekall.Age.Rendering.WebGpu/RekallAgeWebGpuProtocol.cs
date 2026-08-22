using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering.WebGpu;

public interface IRekallAgeWebGpuPacket
{
    int Version { get; }
}

public sealed record RekallAgeWebGpuCreatePacket(
    int Version,
    string ResourceType,
    RekallAgeGraphicsResourceHandle Handle,
    JsonElement Descriptor) : IRekallAgeWebGpuPacket;

public sealed class RekallAgeWebGpuProtocolException : Exception
{
    public RekallAgeWebGpuProtocolException(RekallAgeGraphicsDiagnostic diagnostic, Exception? innerException = null)
        : base(diagnostic.Message, innerException)
    {
        Diagnostic = diagnostic;
    }

    public RekallAgeGraphicsDiagnostic Diagnostic { get; }
}

public static class RekallAgeWebGpuProtocol
{
    public const int Version = 1;
    public const int MaximumPacketBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        EnsurePacketSize(json);
        EnsureSupportedVersion(json);
        return json;
    }

    public static T Deserialize<T>(string json) where T : IRekallAgeWebGpuPacket
    {
        ArgumentNullException.ThrowIfNull(json);
        EnsurePacketSize(json);
        EnsureSupportedVersion(json);

        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions)
                ?? throw InvalidPacket("The WebGPU protocol packet cannot be null.");
        }
        catch (JsonException exception)
        {
            throw InvalidJson(exception);
        }
    }

    private static void EnsurePacketSize(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumPacketBytes)
        {
            throw new RekallAgeWebGpuProtocolException(new(
                "REKALL_WEBGPU_PROTOCOL_PACKET_TOO_LARGE",
                $"WebGPU protocol packets must not exceed {MaximumPacketBytes} UTF-8 bytes."));
        }
    }

    private static void EnsureSupportedVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var parsedVersion))
            {
                throw InvalidPacket("WebGPU protocol packets must include an integer version.");
            }

            if (parsedVersion != Version)
            {
                throw new RekallAgeWebGpuProtocolException(new(
                    "REKALL_WEBGPU_PROTOCOL_VERSION_UNSUPPORTED",
                    $"WebGPU protocol version {parsedVersion} is not supported."));
            }
        }
        catch (JsonException exception)
        {
            throw InvalidJson(exception);
        }
    }

    private static RekallAgeWebGpuProtocolException InvalidJson(JsonException exception) => new(
        new("REKALL_WEBGPU_PROTOCOL_JSON_INVALID", "WebGPU protocol packets must be valid JSON."),
        exception);

    private static RekallAgeWebGpuProtocolException InvalidPacket(string message) => new(
        new("REKALL_WEBGPU_PROTOCOL_PACKET_INVALID", message));
}
