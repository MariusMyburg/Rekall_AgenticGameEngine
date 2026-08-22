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
    JsonElement Descriptor,
    string Operation = "create") : IRekallAgeWebGpuPacket;

public sealed record RekallAgeWebGpuDestroyPacket(
    int Version,
    RekallAgeGraphicsResourceHandle Handle,
    string Operation = "destroy") : IRekallAgeWebGpuPacket;

public sealed record RekallAgeWebGpuWriteBufferPacket(
    int Version,
    RekallAgeGraphicsResourceHandle Handle,
    ulong Offset,
    string DataBase64,
    string Operation = "writeBuffer") : IRekallAgeWebGpuPacket;

public sealed record RekallAgeWebGpuWriteTexturePacket(
    int Version,
    RekallAgeGraphicsResourceHandle Handle,
    int MipLevel,
    int ArrayLayer,
    string DataBase64,
    string Operation = "writeTexture") : IRekallAgeWebGpuPacket;

public sealed record RekallAgeWebGpuCommandPacket(string Kind, JsonElement Data);

public sealed record RekallAgeWebGpuSubmitPacket(
    int Version,
    string? Label,
    IReadOnlyList<RekallAgeWebGpuCommandPacket> Commands,
    string Operation = "submit") : IRekallAgeWebGpuPacket;

public sealed record RekallAgeWebGpuImportCanvasOutputPacket(
    int Version,
    RekallAgeGraphicsResourceHandle Texture,
    RekallAgeGraphicsResourceHandle RenderTarget,
    int Width,
    int Height,
    RekallAgeTextureFormat Format,
    string? Label,
    string Operation = "importCanvasOutput") : IRekallAgeWebGpuPacket;

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

    private static readonly JsonSerializerOptions DescriptorInputOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly IReadOnlyDictionary<RekallAgeGraphicsResourceKind, Type> CreateDescriptorTypes =
        new Dictionary<RekallAgeGraphicsResourceKind, Type>
        {
            [RekallAgeGraphicsResourceKind.Buffer] = typeof(RekallAgeBufferDescriptor),
            [RekallAgeGraphicsResourceKind.Texture] = typeof(RekallAgeTextureDescriptor),
            [RekallAgeGraphicsResourceKind.Sampler] = typeof(RekallAgeSamplerDescriptor),
            [RekallAgeGraphicsResourceKind.ShaderModule] = typeof(RekallAgeShaderModuleDescriptor),
            [RekallAgeGraphicsResourceKind.BindingLayout] = typeof(RekallAgeBindingLayoutDescriptor),
            [RekallAgeGraphicsResourceKind.BindingSet] = typeof(RekallAgeBindingSetDescriptor),
            [RekallAgeGraphicsResourceKind.RenderPipeline] = typeof(RekallAgeGraphicsPipelineDescriptor),
            [RekallAgeGraphicsResourceKind.ComputePipeline] = typeof(RekallAgeComputePipelineDescriptor),
            [RekallAgeGraphicsResourceKind.RenderTarget] = typeof(RekallAgeRenderTargetDescriptor)
        };

    public static string Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(NormalizeForSerialization(value), SerializerOptions);
        EnsurePacketSize(json);
        EnsureSupportedVersion(json);
        return json;
    }

    public static JsonElement ToJsonElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(T), SerializerOptions);

    public static T Deserialize<T>(string json) where T : IRekallAgeWebGpuPacket
    {
        ArgumentNullException.ThrowIfNull(json);
        EnsurePacketSize(json);
        EnsureSupportedVersion(json);
        EnsurePacketShape<T>(json);

        try
        {
            var packet = JsonSerializer.Deserialize<T>(json, SerializerOptions)
                ?? throw InvalidPacket("The WebGPU protocol packet cannot be null.");
            return NormalizePacket(packet, allowNumericDescriptorEnums: false);
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

    private static object? NormalizeForSerialization<T>(T value) =>
        value is IRekallAgeWebGpuPacket packet
            ? NormalizePacket(packet, allowNumericDescriptorEnums: true)
            : value;

    private static T NormalizePacket<T>(T packet, bool allowNumericDescriptorEnums)
        where T : IRekallAgeWebGpuPacket
    {
        if (packet.Version != Version)
        {
            throw new RekallAgeWebGpuProtocolException(new(
                "REKALL_WEBGPU_PROTOCOL_VERSION_UNSUPPORTED",
                $"WebGPU protocol version {packet.Version} is not supported."));
        }

        return packet switch
        {
            RekallAgeWebGpuCreatePacket createPacket => (T)(object)NormalizeCreatePacket(createPacket, allowNumericDescriptorEnums),
            RekallAgeWebGpuDestroyPacket destroyPacket => (T)(object)ValidateOperation(destroyPacket, destroyPacket.Operation, "destroy"),
            RekallAgeWebGpuWriteBufferPacket writeBufferPacket => (T)(object)ValidateOperation(writeBufferPacket, writeBufferPacket.Operation, "writeBuffer"),
            RekallAgeWebGpuWriteTexturePacket writeTexturePacket => (T)(object)ValidateOperation(writeTexturePacket, writeTexturePacket.Operation, "writeTexture"),
            RekallAgeWebGpuSubmitPacket submitPacket => (T)(object)ValidateSubmission(submitPacket),
            RekallAgeWebGpuImportCanvasOutputPacket importPacket => (T)(object)ValidateOperation(importPacket, importPacket.Operation, "importCanvasOutput"),
            _ => throw InvalidPacket("WebGPU protocol packets must use a known packet type.")
        };
    }

    private static RekallAgeWebGpuCreatePacket NormalizeCreatePacket(
        RekallAgeWebGpuCreatePacket packet,
        bool allowNumericDescriptorEnums)
    {
        if (string.IsNullOrWhiteSpace(packet.ResourceType)
            || !TryGetResourceKind(packet.ResourceType, out var resourceKind))
        {
            throw new RekallAgeWebGpuProtocolException(new(
                "REKALL_WEBGPU_PROTOCOL_RESOURCE_TYPE_INVALID",
                "WebGPU create packets must use a known canonical resource type."));
        }

        if (packet.Handle.Kind != resourceKind)
        {
            throw new RekallAgeWebGpuProtocolException(new(
                "REKALL_WEBGPU_PROTOCOL_RESOURCE_TYPE_MISMATCH",
                "WebGPU create packet resource type must match the handle kind."));
        }

        if (!CreateDescriptorTypes.TryGetValue(resourceKind, out var descriptorType))
        {
            throw new RekallAgeWebGpuProtocolException(new(
                "REKALL_WEBGPU_PROTOCOL_RESOURCE_TYPE_INVALID",
                "WebGPU create packets must use a resource kind with a public AGE descriptor."));
        }

        var options = allowNumericDescriptorEnums ? DescriptorInputOptions : SerializerOptions;
        if (packet.Descriptor.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw InvalidDescriptor();
        }

        try
        {
            var descriptor = JsonSerializer.Deserialize(packet.Descriptor.GetRawText(), descriptorType, options)
                ?? throw InvalidPacket("WebGPU create packet descriptors must not be null.");
            return ValidateOperation(packet with { Descriptor = JsonSerializer.SerializeToElement(descriptor, SerializerOptions) }, packet.Operation, "create", allowMissingOperation: true);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw InvalidDescriptor(exception);
        }
    }

    private static RekallAgeWebGpuSubmitPacket ValidateSubmission(RekallAgeWebGpuSubmitPacket packet)
    {
        ValidateOperation(packet, packet.Operation, "submit");
        if (packet.Commands is null || packet.Commands.Any(command => !IsKnownCommand(command.Kind) || command.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null))
        {
            throw new RekallAgeWebGpuProtocolException(new(
                "REKALL_WEBGPU_PROTOCOL_COMMAND_KIND_INVALID",
                "WebGPU submission packets must contain only known command kinds with payload data."));
        }

        return packet;
    }

    private static T ValidateOperation<T>(T packet, string? actual, string expected, bool allowMissingOperation = false)
    {
        if ((allowMissingOperation && string.IsNullOrEmpty(actual)) || string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return packet;
        }

        throw new RekallAgeWebGpuProtocolException(new(
            "REKALL_WEBGPU_PROTOCOL_OPERATION_INVALID",
            "WebGPU protocol packets must use the expected operation name."));
    }

    private static bool IsKnownCommand(string? kind) => kind is
        "copyBuffer" or "beginRenderPass" or "setRenderPipeline" or "setComputePipeline"
        or "setBindingSet" or "setVertexBuffer" or "setIndexBuffer" or "draw" or "drawIndexed"
        or "drawIndirect" or "drawIndexedIndirect" or "endRenderPass" or "beginComputePass"
        or "dispatch" or "dispatchIndirect" or "endComputePass";

    private static bool TryGetResourceKind(string resourceType, out RekallAgeGraphicsResourceKind resourceKind)
    {
        foreach (var kind in CreateDescriptorTypes.Keys)
        {
            if (string.Equals(resourceType, JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()), StringComparison.Ordinal))
            {
                resourceKind = kind;
                return true;
            }
        }

        resourceKind = default;
        return false;
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

    private static void EnsurePacketShape<T>(string json) where T : IRekallAgeWebGpuPacket
    {
        if (typeof(T) != typeof(RekallAgeWebGpuCreatePacket))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("resourceType", out var resourceTypeElement)
                || resourceTypeElement.ValueKind != JsonValueKind.String
                || !TryGetResourceKind(resourceTypeElement.GetString()!, out var resourceType))
            {
                throw new RekallAgeWebGpuProtocolException(new(
                    "REKALL_WEBGPU_PROTOCOL_RESOURCE_TYPE_INVALID",
                    "WebGPU create packets must declare a known canonical resource type."));
            }

            if (!root.TryGetProperty("handle", out var handleElement)
                || handleElement.ValueKind != JsonValueKind.Object
                || !handleElement.TryGetProperty("kind", out var kindElement)
                || kindElement.ValueKind != JsonValueKind.String
                || !TryGetResourceKind(kindElement.GetString()!, out var handleKind))
            {
                throw new RekallAgeWebGpuProtocolException(new(
                    "REKALL_WEBGPU_PROTOCOL_RESOURCE_KIND_INVALID",
                    "WebGPU create packets must declare a known canonical handle kind."));
            }

            if (resourceType != handleKind)
            {
                throw new RekallAgeWebGpuProtocolException(new(
                    "REKALL_WEBGPU_PROTOCOL_RESOURCE_TYPE_MISMATCH",
                    "WebGPU create packet resource type must match the handle kind."));
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

    private static RekallAgeWebGpuProtocolException InvalidDescriptor(Exception? exception = null) => new(
        new("REKALL_WEBGPU_PROTOCOL_DESCRIPTOR_INVALID", "WebGPU create packet descriptors must be present, valid, and supported."),
        exception);

    private static RekallAgeWebGpuProtocolException InvalidPacket(string message) => new(
        new("REKALL_WEBGPU_PROTOCOL_PACKET_INVALID", message));
}
