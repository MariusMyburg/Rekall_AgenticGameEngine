using System.Text.Json;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;

namespace Rekall.Age.Tests.Rendering;

public sealed class WebGpuProtocolTests
{
    private static readonly Guid DeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void ProtocolRoundTripsAHandCheckedBufferPacket()
    {
        var packet = new RekallAgeWebGpuCreatePacket(
            1,
            "buffer",
            new(Guid.Parse("11111111-1111-1111-1111-111111111111"), RekallAgeGraphicsResourceKind.Buffer, 7, 1),
            JsonSerializer.SerializeToElement(new RekallAgeBufferDescriptor(16, RekallAgeBufferUsage.Vertex, Label: "triangle")));

        var json = RekallAgeWebGpuProtocol.Serialize(packet);
        var restored = RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(json);

        Assert.Equal(7, restored.Handle.Slot);
        Assert.Equal("buffer", restored.ResourceType);
    }

    [Fact]
    public void ProtocolDeserializesTheLiteralBrowserBufferFixture()
    {
        const string packetJson = """
            {"version":1,"resourceType":"buffer","handle":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":7,"generation":1},"descriptor":{"sizeBytes":16,"usage":"vertex","memoryAccess":"deviceLocal","label":"literal-browser-fixture"},"operation":"create"}
            """;

        var packet = RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(packetJson);

        Assert.Equal("buffer", packet.ResourceType);
        Assert.Equal(7, packet.Handle.Slot);
        Assert.Equal("literal-browser-fixture", packet.Descriptor.GetProperty("label").GetString());
    }

    [Fact]
    public void ProtocolRejectsUnknownVersionsAndOversizedPackets()
    {
        Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>("{\"version\":2}"));
        Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuWriteBufferPacket(
                1,
                Handle(RekallAgeGraphicsResourceKind.Buffer, 1),
                0,
                new string('x', RekallAgeWebGpuProtocol.MaximumPacketBytes))));
    }

    [Fact]
    public void ProtocolUsesCamelCaseStringEnumsAndTheCurrentVersion()
    {
        var json = RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuCreatePacket(
            1,
            "buffer",
            new(Guid.Parse("11111111-1111-1111-1111-111111111111"), RekallAgeGraphicsResourceKind.Buffer, 7, 1),
            JsonSerializer.SerializeToElement(new RekallAgeBufferDescriptor(16, RekallAgeBufferUsage.Vertex))));

        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"resourceType\":\"buffer\"", json);
        Assert.Contains("\"kind\":\"buffer\"", json);
        Assert.Contains("\"sizeBytes\":16", json);
        Assert.Contains("\"usage\":\"vertex\"", json);
        Assert.Contains("\"memoryAccess\":\"deviceLocal\"", json);
        Assert.DoesNotContain("\"SizeBytes\"", json);
    }

    [Fact]
    public void ProtocolExceptionsExposeStableDiagnostics()
    {
        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>("not json"));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_JSON_INVALID", exception.Diagnostic.Code);
    }

    [Fact]
    public void ProtocolRejectsNumericEnumValues()
    {
        const string packetJson = """
            {"version":1,"resourceType":"buffer","handle":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":999,"slot":7,"generation":1},"descriptor":{}}
            """;

        Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(packetJson));
    }

    [Fact]
    public void ProtocolRejectsMissingCreateResourceTypes()
    {
        const string packetJson = """
            {"version":1,"handle":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":7,"generation":1},"descriptor":{"sizeBytes":16,"usage":"vertex"}}
            """;

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(packetJson));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_RESOURCE_TYPE_INVALID", exception.Diagnostic.Code);
    }

    [Fact]
    public void ProtocolRejectsUnknownCreateResourceTypes()
    {
        const string packetJson = """
            {"version":1,"resourceType":"unknown","handle":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":7,"generation":1},"descriptor":{"sizeBytes":16,"usage":"vertex"}}
            """;

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(packetJson));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_RESOURCE_TYPE_INVALID", exception.Diagnostic.Code);
    }

    [Fact]
    public void ProtocolRejectsCreatePacketsWithMissingHandleKinds()
    {
        const string packetJson = """
            {"version":1,"resourceType":"buffer","handle":{"deviceId":"11111111-1111-1111-1111-111111111111","slot":7,"generation":1},"descriptor":{"sizeBytes":16,"usage":"vertex"}}
            """;

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(packetJson));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_RESOURCE_KIND_INVALID", exception.Diagnostic.Code);
    }

    [Fact]
    public void ProtocolRejectsCreateResourceTypesThatDisagreeWithTheirHandleKind()
    {
        const string packetJson = """
            {"version":1,"resourceType":"texture","handle":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":7,"generation":1},"descriptor":{"sizeBytes":16,"usage":"vertex"}}
            """;

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(packetJson));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_RESOURCE_TYPE_MISMATCH", exception.Diagnostic.Code);
    }

    [Fact]
    public void ProtocolRejectsCreatePacketsWithMissingDescriptors()
    {
        const string packetJson = """
            {"version":1,"resourceType":"buffer","handle":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":7,"generation":1}}
            """;

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(packetJson));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_DESCRIPTOR_INVALID", exception.Diagnostic.Code);
    }

    [Fact]
    public void ProtocolRejectsInvalidDescriptorEnumsDuringDeserialization()
    {
        const string packetJson = """
            {"version":1,"resourceType":"buffer","handle":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":7,"generation":1},"descriptor":{"sizeBytes":16,"usage":"unsupported"}}
            """;

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(packetJson));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_DESCRIPTOR_INVALID", exception.Diagnostic.Code);
    }

    [Fact]
    public void ProtocolRejectsUnsupportedDescriptorEnumsDuringSerialization()
    {
        var packet = new RekallAgeWebGpuCreatePacket(
            1,
            "buffer",
            new(Guid.Parse("11111111-1111-1111-1111-111111111111"), RekallAgeGraphicsResourceKind.Buffer, 7, 1),
            JsonSerializer.SerializeToElement(new RekallAgeBufferDescriptor(16, (RekallAgeBufferUsage)999)));

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Serialize(packet));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_DESCRIPTOR_INVALID", exception.Diagnostic.Code);
    }

    [Fact]
    public void ProtocolRoundTripsEverySupportedResourceDescriptor()
    {
        var buffer = Handle(RekallAgeGraphicsResourceKind.Buffer, 1);
        var texture = Handle(RekallAgeGraphicsResourceKind.Texture, 2);
        var sampler = Handle(RekallAgeGraphicsResourceKind.Sampler, 3);
        var vertexShader = Handle(RekallAgeGraphicsResourceKind.ShaderModule, 4);
        var fragmentShader = Handle(RekallAgeGraphicsResourceKind.ShaderModule, 5);
        var computeShader = Handle(RekallAgeGraphicsResourceKind.ShaderModule, 6);
        var layout = Handle(RekallAgeGraphicsResourceKind.BindingLayout, 7);

        var cases = new (string ResourceType, RekallAgeGraphicsResourceHandle Handle, object Descriptor, string ExpectedLabel)[]
        {
            ("buffer", buffer, new RekallAgeBufferDescriptor(64, RekallAgeBufferUsage.Vertex | RekallAgeBufferUsage.TransferDestination, Label: "buffer-descriptor"), "buffer-descriptor"),
            ("texture", texture, new RekallAgeTextureDescriptor(RekallAgeTextureDimension.Texture2D, 8, 4, 1, 1, 1, 1, RekallAgeTextureFormat.Rgba8Unorm, RekallAgeTextureUsage.Sampled, "texture-descriptor"), "texture-descriptor"),
            ("sampler", sampler, new RekallAgeSamplerDescriptor(Label: "sampler-descriptor"), "sampler-descriptor"),
            ("shaderModule", vertexShader, new RekallAgeShaderModuleDescriptor(RekallAgeShaderStage.Vertex, RekallAgeShaderSourceLanguage.Wgsl, "@vertex fn main() {}", Label: "shader-descriptor"), "shader-descriptor"),
            ("bindingLayout", layout, new RekallAgeBindingLayoutDescriptor([new(0, RekallAgeBindingType.SampledTexture, RekallAgeShaderStage.Fragment, Texture: new())], "layout-descriptor"), "layout-descriptor"),
            ("bindingSet", Handle(RekallAgeGraphicsResourceKind.BindingSet, 8), new RekallAgeBindingSetDescriptor(layout, [new(0, texture)], "set-descriptor"), "set-descriptor"),
            ("renderPipeline", Handle(RekallAgeGraphicsResourceKind.RenderPipeline, 9), new RekallAgeGraphicsPipelineDescriptor(vertexShader, fragmentShader, [layout], [new(RekallAgeTextureFormat.Rgba8Unorm)], Label: "render-pipeline-descriptor") { VertexBuffers = [new(8, RekallAgeVertexStepMode.Vertex, [new("position", 0, RekallAgeVertexFormat.Float32x2, 0)])] }, "render-pipeline-descriptor"),
            ("computePipeline", Handle(RekallAgeGraphicsResourceKind.ComputePipeline, 10), new RekallAgeComputePipelineDescriptor(computeShader, [layout], "compute-pipeline-descriptor"), "compute-pipeline-descriptor"),
            ("renderTarget", Handle(RekallAgeGraphicsResourceKind.RenderTarget, 11), new RekallAgeRenderTargetDescriptor([new(texture)], null, 8, 4, "target-descriptor"), "target-descriptor")
        };

        foreach (var item in cases)
        {
            var descriptor = RekallAgeWebGpuProtocol.ToJsonElement(item.Descriptor);
            var json = RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuCreatePacket(1, item.ResourceType, item.Handle, descriptor));
            var restored = RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(json);

            Assert.Equal(item.ResourceType, restored.ResourceType);
            Assert.Equal(item.ExpectedLabel, restored.Descriptor.GetProperty("label").GetString());
        }
    }

    [Fact]
    public void ProtocolRoundTripsEverySupportedCommandPayloadInOrder()
    {
        var buffer = Handle(RekallAgeGraphicsResourceKind.Buffer, 1);
        var renderTarget = Handle(RekallAgeGraphicsResourceKind.RenderTarget, 2);
        var renderPipeline = Handle(RekallAgeGraphicsResourceKind.RenderPipeline, 3);
        var computePipeline = Handle(RekallAgeGraphicsResourceKind.ComputePipeline, 4);
        var bindingSet = Handle(RekallAgeGraphicsResourceKind.BindingSet, 5);
        RekallAgeGraphicsCommand[] commands =
        [
            new RekallAgeCopyBufferCommand(buffer, 0, buffer, 4, 4),
            new RekallAgeBeginRenderPassCommand(new(renderTarget, [new(0.1f, 0.2f, 0.3f, 1f)], Label: "render-pass")),
            new RekallAgeSetRenderPipelineCommand(renderPipeline),
            new RekallAgeSetBindingSetCommand(0, bindingSet),
            new RekallAgeSetVertexBufferCommand(0, buffer, 0, 16),
            new RekallAgeSetIndexBufferCommand(buffer, RekallAgeIndexFormat.UInt32, 0, 16),
            new RekallAgeDrawCommand(3, 1, 0, 0),
            new RekallAgeDrawIndexedCommand(3, 1, 0, 0, 0),
            new RekallAgeDrawIndirectCommand(buffer, 0, 1, 16),
            new RekallAgeDrawIndexedIndirectCommand(buffer, 0, 1, 20),
            new RekallAgeEndRenderPassCommand(),
            new RekallAgeBeginComputePassCommand("compute-pass"),
            new RekallAgeSetComputePipelineCommand(computePipeline),
            new RekallAgeSetBindingSetCommand(0, bindingSet),
            new RekallAgeDispatchCommand(2, 3, 4),
            new RekallAgeDispatchIndirectCommand(buffer, 0),
            new RekallAgeEndComputePassCommand()
        ];
        string[] kinds =
        [
            "copyBuffer", "beginRenderPass", "setRenderPipeline", "setBindingSet", "setVertexBuffer",
            "setIndexBuffer", "draw", "drawIndexed", "drawIndirect", "drawIndexedIndirect", "endRenderPass",
            "beginComputePass", "setComputePipeline", "setBindingSet", "dispatch", "dispatchIndirect", "endComputePass"
        ];
        var packet = new RekallAgeWebGpuSubmitPacket(1, "all-commands",
            commands.Zip(kinds, (command, kind) => new RekallAgeWebGpuCommandPacket(kind, RekallAgeWebGpuProtocol.ToJsonElement(command))).ToArray());

        var restored = RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(RekallAgeWebGpuProtocol.Serialize(packet));

        Assert.Equal(kinds, restored.Commands.Select(command => command.Kind));
        Assert.Equal("render-pass", restored.Commands[1].Data.GetProperty("descriptor").GetProperty("label").GetString());
        Assert.Equal("uInt32", restored.Commands[5].Data.GetProperty("format").GetString());
    }

    [Fact]
    public void ProtocolRoundTripsEverySupportedPacketAndBridgeResultShape()
    {
        var buffer = Handle(RekallAgeGraphicsResourceKind.Buffer, 1);
        var texture = Handle(RekallAgeGraphicsResourceKind.Texture, 2);
        var target = Handle(RekallAgeGraphicsResourceKind.RenderTarget, 3);

        Assert.Equal("destroy", RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuDestroyPacket>(
            RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuDestroyPacket(1, buffer))).Operation);
        Assert.Equal("AAECAw==", RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuWriteBufferPacket>(
            RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuWriteBufferPacket(1, buffer, 4, "AAECAw=="))).DataBase64);
        Assert.Equal(2, RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuWriteTexturePacket>(
            RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuWriteTexturePacket(1, texture, 1, 2, "AAECAw=="))).ArrayLayer);
        Assert.Equal(RekallAgeTextureFormat.Bgra8Unorm, RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuImportCanvasOutputPacket>(
            RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuImportCanvasOutputPacket(1, texture, target, 640, 360, RekallAgeTextureFormat.Bgra8Unorm, "output"))).Format);

        const string initializationJson = """
            {"succeeded":true,"diagnostics":[],"capabilities":{"preferredCanvasFormat":"bgra8unorm","limits":{"maxBufferSize":1024},"features":["timestamp-query"]}}
            """;
        Assert.True(RekallAgeWebGpuProtocol.DeserializeBridgeResult(initializationJson).Succeeded);
    }

    [Fact]
    public void ProtocolRejectsUnsupportedPayloadTypes()
    {
        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.ToJsonElement(new object()));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_PAYLOAD_TYPE_INVALID", exception.Diagnostic.Code);
    }

    private static RekallAgeGraphicsResourceHandle Handle(RekallAgeGraphicsResourceKind kind, int slot) =>
        new(DeviceId, kind, slot, 1);
}
