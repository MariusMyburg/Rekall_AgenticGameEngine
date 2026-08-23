using System.Text.Json;
using System.Text.Json.Serialization;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering.WebGpu;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    IgnoreReadOnlyProperties = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(RekallAgeWebGpuCreatePacket))]
[JsonSerializable(typeof(RekallAgeWebGpuDestroyPacket))]
[JsonSerializable(typeof(RekallAgeWebGpuWriteBufferPacket))]
[JsonSerializable(typeof(RekallAgeWebGpuWriteTexturePacket))]
[JsonSerializable(typeof(RekallAgeWebGpuSubmitPacket))]
[JsonSerializable(typeof(RekallAgeWebGpuImportCanvasOutputPacket))]
[JsonSerializable(typeof(RekallAgeWebGpuBridgeEnvelope))]
[JsonSerializable(typeof(RekallAgeWebGpuBridgeDiagnosticEnvelope))]
[JsonSerializable(typeof(RekallAgeBufferDescriptor))]
[JsonSerializable(typeof(RekallAgeTextureDescriptor))]
[JsonSerializable(typeof(RekallAgeSamplerDescriptor))]
[JsonSerializable(typeof(RekallAgeShaderModuleDescriptor))]
[JsonSerializable(typeof(RekallAgeBindingLayoutDescriptor))]
[JsonSerializable(typeof(RekallAgeBindingSetDescriptor))]
[JsonSerializable(typeof(RekallAgeGraphicsPipelineDescriptor))]
[JsonSerializable(typeof(RekallAgeComputePipelineDescriptor))]
[JsonSerializable(typeof(RekallAgeRenderTargetDescriptor))]
[JsonSerializable(typeof(RekallAgeCopyBufferCommand))]
[JsonSerializable(typeof(RekallAgeBeginRenderPassCommand))]
[JsonSerializable(typeof(RekallAgeSetRenderPipelineCommand))]
[JsonSerializable(typeof(RekallAgeSetComputePipelineCommand))]
[JsonSerializable(typeof(RekallAgeSetBindingSetCommand))]
[JsonSerializable(typeof(RekallAgeSetVertexBufferCommand))]
[JsonSerializable(typeof(RekallAgeSetIndexBufferCommand))]
[JsonSerializable(typeof(RekallAgeDrawCommand))]
[JsonSerializable(typeof(RekallAgeDrawIndexedCommand))]
[JsonSerializable(typeof(RekallAgeDrawIndirectCommand))]
[JsonSerializable(typeof(RekallAgeDrawIndexedIndirectCommand))]
[JsonSerializable(typeof(RekallAgeEndRenderPassCommand))]
[JsonSerializable(typeof(RekallAgeBeginComputePassCommand))]
[JsonSerializable(typeof(RekallAgeDispatchCommand))]
[JsonSerializable(typeof(RekallAgeDispatchIndirectCommand))]
[JsonSerializable(typeof(RekallAgeEndComputePassCommand))]
internal sealed partial class RekallAgeWebGpuJsonContext : JsonSerializerContext
{
    internal static RekallAgeWebGpuJsonContext Strict { get; } = new(CreateOptions(allowIntegerEnums: false));

    internal static RekallAgeWebGpuJsonContext DescriptorCompatibility { get; } = new(CreateOptions(allowIntegerEnums: true, propertyNameCaseInsensitive: true));

    private static JsonSerializerOptions CreateOptions(bool allowIntegerEnums, bool propertyNameCaseInsensitive = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = propertyNameCaseInsensitive,
            IgnoreReadOnlyProperties = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeGraphicsResourceKind>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeBufferUsage>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeMemoryAccess>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeStorageBufferAccess>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeTextureDimension>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeTextureFormat>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeTextureUsage>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeFilter>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeMipmapFilter>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeAddressMode>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeCompareOperation>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeShaderStage>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeShaderSourceLanguage>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeBindingType>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeTextureSampleType>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeTextureViewDimension>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeStorageTextureAccess>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgePrimitiveTopology>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeCullMode>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeFrontFace>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeVertexStepMode>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeVertexFormat>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        options.Converters.Add(new JsonStringEnumConverter<RekallAgeIndexFormat>(JsonNamingPolicy.CamelCase, allowIntegerEnums));
        return options;
    }
}

internal sealed class RekallAgeWebGpuBridgeEnvelope
{
    [JsonRequired]
    public bool Succeeded { get; init; }

    [JsonRequired]
    public IReadOnlyList<RekallAgeWebGpuBridgeDiagnosticEnvelope>? Diagnostics { get; init; }

    public JsonElement? Capabilities { get; init; }
}

internal sealed class RekallAgeWebGpuBridgeDiagnosticEnvelope
{
    [JsonRequired]
    public string? Code { get; init; }

    [JsonRequired]
    public string? Message { get; init; }

    public string? Target { get; init; }
}
