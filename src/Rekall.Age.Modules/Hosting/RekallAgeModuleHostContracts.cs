using System.Text.Json;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Modules.Hosting;

public static class RekallAgeModuleHostProtocol
{
    public const int Version = 1;
    public const int MaximumMessageBytes = 64 * 1024 * 1024;
    public const int MaximumJsonDepth = 128;
    public const int MaximumStandardErrorBytes = 64 * 1024;
    public const int MaximumModules = 256;
    public const int MaximumPendingRequests = 1;
    public static TimeSpan StartupTimeout { get; } = TimeSpan.FromSeconds(10);
    public static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(1);
}

public static class RekallAgeModuleHostOperations
{
    public const string Initialize = "host.initialize";
    public const string RuntimeUpdate = "runtime.update";
    public const string PlayableCreate = "playable.create";
    public const string PlayableTick = "playable.tick";
    public const string PlayableRender = "playable.render";
    public const string Shutdown = "host.shutdown";

    public static bool IsKnown(string? operation) => operation is
        Initialize or RuntimeUpdate or PlayableCreate or PlayableTick or PlayableRender or Shutdown;
}

public sealed record RekallAgeModuleHostError(string Code, string Type, string Message, string? ModuleId = null);

public sealed record RekallAgeModuleHostEnvelope(
    int ProtocolVersion,
    long Sequence,
    string Operation,
    JsonElement Payload)
{
    public bool? Ok { get; init; }

    public RekallAgeModuleHostError? Error { get; init; }

    public T DeserializePayload<T>()
    {
        try
        {
            return Payload.Deserialize<T>(RekallAgeModuleHostJson.Options)
                ?? throw PayloadInvalid<T>();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new RekallAgeModuleHostException(
                "REKALL_MODULE_HOST_PROTOCOL_INVALID",
                $"Module-host payload could not be decoded as '{typeof(T).Name}'.",
                innerException: ex);
        }
    }

    private static RekallAgeModuleHostException PayloadInvalid<T>() => new(
        "REKALL_MODULE_HOST_PROTOCOL_INVALID",
        $"Module-host payload could not be decoded as '{typeof(T).Name}'.");

    public static RekallAgeModuleHostEnvelope Request<T>(long sequence, string operation, T payload) => new(
        RekallAgeModuleHostProtocol.Version,
        sequence,
        operation,
        JsonSerializer.SerializeToElement(payload, RekallAgeModuleHostJson.Options));

    public static RekallAgeModuleHostEnvelope Success<T>(long sequence, string operation, T payload) =>
        Request(sequence, operation, payload) with { Ok = true };

    public static RekallAgeModuleHostEnvelope Failure(
        long sequence,
        string operation,
        RekallAgeModuleHostError error) =>
        Request(sequence, operation, new { }) with { Ok = false, Error = error };
}

public sealed record RekallAgeModuleHostInitializeRequest(string LoadPlanPath);

public sealed record RekallAgeModuleHostSystemDescriptor(string Id, int Priority, string ModuleId);

public sealed record RekallAgeModuleHostInitializeResponse(
    int ProtocolVersion,
    string TrustPosture,
    IReadOnlyList<RekallAgeModuleHostSystemDescriptor> Systems,
    IReadOnlyList<RekallAgeComponentSchema> ComponentSchemas,
    string? PlayableKind);

public sealed record RekallAgeModuleHostRuntimeUpdateRequest(
    string SystemId,
    RekallAgeRuntimeWorld World,
    int FrameIndex,
    TimeSpan DeltaTime,
    TimeSpan ElapsedTime,
    RekallAgeRuntimeInputState Input);

public sealed record RekallAgeModuleHostRuntimeUpdateResponse(RekallAgeRuntimeWorld World);

public sealed record RekallAgeModuleHostPlayableCreateRequest(RekallAgePlayableModuleContext Context);

public sealed record RekallAgeModuleHostPlayableCreateResponse(string Kind);

public sealed record RekallAgeModuleHostPlayableTickRequest(RekallAgePlayableModuleInput Input);

public sealed record RekallAgeModuleHostPlayableRenderResponse(RekallAgePlayableModuleFrame Frame);

internal static class RekallAgeModuleHostJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = RekallAgeModuleHostProtocol.MaximumJsonDepth,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };
}
