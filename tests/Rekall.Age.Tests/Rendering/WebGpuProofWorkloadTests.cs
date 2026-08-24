using System.Buffers.Binary;
using System.Text.Json;
using Rekall.Age.Player.Web;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class WebGpuProofWorkloadTests
{
    private const string ExpectedVertexShader =
        """
        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) color: vec3<f32>,
        };

        @vertex
        fn main(@location(0) code: vec2<u32>) -> VertexOutput {
            let xCode = code.x & 255u;
            let yCode = code.y & 255u;
            var x = 0.0;
            if (xCode == 76u) {
                x = -0.72;
            } else if (xCode == 82u) {
                x = 0.72;
            }
            var y = 0.72;
            if (yCode == 68u) {
                y = -0.68;
            }
            var color = vec3<f32>(0.10, 0.25, 1.00);
            if (xCode == 76u) {
                color = vec3<f32>(0.10, 0.95, 0.90);
            } else if (xCode == 82u) {
                color = vec3<f32>(0.95, 0.10, 0.85);
            }
            var output: VertexOutput;
            output.position = vec4<f32>(x, y, 0.0, 1.0);
            output.color = color;
            return output;
        }
        """;

    private const string ExpectedFragmentShader =
        """
        @fragment
        fn main(@location(0) color: vec3<f32>) -> @location(0) vec4<f32> {
            return vec4<f32>(color, 1.0);
        }
        """;

    private static readonly byte[] ExpectedVertexBytes =
    [
        0x4C, 0x78, 0x78, 0x78, 0x44, 0x78, 0x78, 0x78,
        0x52, 0x78, 0x78, 0x78, 0x44, 0x78, 0x78, 0x78,
        0x43, 0x78, 0x78, 0x78, 0x55, 0x78, 0x78, 0x0A
    ];

    private static readonly byte[] ExpectedIndirectBytes =
    [
        0x03, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    ];

    [Fact]
    public void ProofCompilesAndSubmitsAnExactAssetIndependentIndirectTriangle()
    {
        var bridge = new RecordingBridge();
        using var device = new RekallAgeWebGpuRenderingDevice(
            bridge,
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("WebGPU"));
        var output = device.ImportCanvasOutput(640, 480, RekallAgeTextureFormat.Bgra8Unorm);
        Assert.True(output.Created);

        var workload = WebGpuProofWorkload.Create(RekallAgeTextureFormat.Bgra8Unorm);
        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(
            workload,
            device,
            new Dictionary<string, RekallAgeGraphicsResourceHandle>(StringComparer.Ordinal)
            {
                ["engine.output"] = output.Handle
            });

        Assert.True(compiled.Valid, string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
        Assert.Equal("proof.webgpu.asset-independent", workload.Id);
        Assert.Equal(24UL, workload.Buffers.Single(item => item.Id == "vertices").SizeBytes);
        Assert.Equal(
            [0x7878784Cu, 0x78787844u, 0x78787852u, 0x78787844u, 0x78787843u, 0x0A787855u],
            workload.Buffers.Single(item => item.Id == "vertices").InitialDataUInt32);
        var indirectArguments = workload.Buffers.Single(item => item.Id == "draw.arguments").InitialDataUInt32;
        Assert.Equal(3u, indirectArguments[0]); // vertexCount
        Assert.Equal(1u, indirectArguments[1]); // instanceCount
        Assert.Equal(0u, indirectArguments[2]); // firstVertex
        Assert.Equal(0u, indirectArguments[3]); // firstInstance

        var vertexShaderDefinition = workload.Shaders.Single(item => item.Id == "proof.vertex");
        Assert.Equal(RekallAgeRuntimeGpuShaderStage.Vertex, vertexShaderDefinition.Stage);
        Assert.Equal(RekallAgeRuntimeGpuShaderLanguage.Wgsl, vertexShaderDefinition.Language);
        Assert.Equal("main", vertexShaderDefinition.EntryPoint);
        Assert.Equal(ExpectedVertexShader, vertexShaderDefinition.Source);
        var fragmentShaderDefinition = workload.Shaders.Single(item => item.Id == "proof.fragment");
        Assert.Equal(RekallAgeRuntimeGpuShaderStage.Fragment, fragmentShaderDefinition.Stage);
        Assert.Equal(RekallAgeRuntimeGpuShaderLanguage.Wgsl, fragmentShaderDefinition.Language);
        Assert.Equal("main", fragmentShaderDefinition.EntryPoint);
        Assert.Equal(ExpectedFragmentShader, fragmentShaderDefinition.Source);

        var pipelineDefinition = Assert.Single(workload.Pipelines);
        Assert.Equal("proof.pipeline", pipelineDefinition.Id);
        Assert.Equal(RekallAgeRuntimeGpuPipelineKind.Render, pipelineDefinition.Kind);
        Assert.Equal("proof.vertex", pipelineDefinition.VertexShader);
        Assert.Equal("proof.fragment", pipelineDefinition.FragmentShader);
        Assert.Empty(pipelineDefinition.BindingLayouts);
        Assert.Equal(["bgra8-unorm"], pipelineDefinition.ColorFormats);
        Assert.Null(pipelineDefinition.DepthStencilFormat);
        Assert.Equal("triangle-list", pipelineDefinition.PrimitiveTopology);
        Assert.Equal("none", pipelineDefinition.CullMode);
        var layout = Assert.Single(pipelineDefinition.VertexBuffers);
        Assert.Equal(8, layout.StrideBytes);
        Assert.Equal(RekallAgeRuntimeGpuVertexStepMode.Vertex, layout.StepMode);
        var attribute = Assert.Single(layout.Attributes);
        Assert.Equal("Code", attribute.Name);
        Assert.Equal(0, attribute.Location);
        Assert.Equal(RekallAgeRuntimeGpuVertexFormat.Uint32x2, attribute.Format);
        Assert.Equal(0, attribute.OffsetBytes);
        Assert.Contains(compiled.CommandBuffer!.Commands, command => command is RekallAgeDrawIndirectCommand
        {
            Offset: 0,
            DrawCount: 1,
            StrideBytes: 16
        });

        var submit = device.Submit(compiled.CommandBuffer);
        Assert.True(submit.Valid, string.Join(Environment.NewLine, submit.Diagnostics.Select(item => item.Message)));

        var writePackets = bridge.Packets
            .Where(packet => Operation(packet) == "writeBuffer")
            .Select(RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuWriteBufferPacket>)
            .ToArray();
        Assert.Equal(2, writePackets.Length);
        Assert.Equal(ExpectedVertexBytes, Convert.FromBase64String(writePackets[0].DataBase64));
        Assert.Equal(ExpectedIndirectBytes, Convert.FromBase64String(writePackets[1].DataBase64));
        var submittedIndirectArguments = DecodeUInt32(writePackets[1].DataBase64).ToArray();
        Assert.Equal(3u, submittedIndirectArguments[0]); // vertexCount
        Assert.Equal(1u, submittedIndirectArguments[1]); // instanceCount
        Assert.Equal(0u, submittedIndirectArguments[2]); // firstVertex
        Assert.Equal(0u, submittedIndirectArguments[3]); // firstInstance

        var createPackets = bridge.Packets
            .Where(packet => Operation(packet) == "create")
            .Select(RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>)
            .ToArray();
        var shaderCreates = createPackets.Where(packet => packet.ResourceType == "shaderModule").ToArray();
        var vertexShaderCreate = shaderCreates.Single(packet =>
            packet.Descriptor.GetProperty("stage").GetString() == "vertex");
        Assert.Equal("wgsl", vertexShaderCreate.Descriptor.GetProperty("language").GetString());
        Assert.Equal(ExpectedVertexShader, vertexShaderCreate.Descriptor.GetProperty("source").GetString());
        Assert.Equal("main", vertexShaderCreate.Descriptor.GetProperty("entryPoint").GetString());
        Assert.Equal("proof.vertex", vertexShaderCreate.Descriptor.GetProperty("label").GetString());
        var fragmentShaderCreate = shaderCreates.Single(packet =>
            packet.Descriptor.GetProperty("stage").GetString() == "fragment");
        Assert.Equal("wgsl", fragmentShaderCreate.Descriptor.GetProperty("language").GetString());
        Assert.Equal(ExpectedFragmentShader, fragmentShaderCreate.Descriptor.GetProperty("source").GetString());
        Assert.Equal("main", fragmentShaderCreate.Descriptor.GetProperty("entryPoint").GetString());
        Assert.Equal("proof.fragment", fragmentShaderCreate.Descriptor.GetProperty("label").GetString());

        var pipelineCreate = createPackets.Single(packet => packet.ResourceType == "renderPipeline");
        AssertHandle(vertexShaderCreate.Handle, pipelineCreate.Descriptor.GetProperty("vertexShader"));
        AssertHandle(fragmentShaderCreate.Handle, pipelineCreate.Descriptor.GetProperty("fragmentShader"));
        Assert.Empty(pipelineCreate.Descriptor.GetProperty("bindingLayouts").EnumerateArray());
        Assert.Equal("triangleList", pipelineCreate.Descriptor.GetProperty("topology").GetString());
        Assert.Equal("none", pipelineCreate.Descriptor.GetProperty("cullMode").GetString());
        Assert.Equal("counterClockwise", pipelineCreate.Descriptor.GetProperty("frontFace").GetString());
        Assert.Equal(JsonValueKind.Null, pipelineCreate.Descriptor.GetProperty("depthStencil").ValueKind);
        Assert.Equal("proof.pipeline", pipelineCreate.Descriptor.GetProperty("label").GetString());
        var colorTarget = pipelineCreate.Descriptor.GetProperty("colorTargets")[0];
        Assert.Equal("bgra8Unorm", colorTarget.GetProperty("format").GetString());
        Assert.False(colorTarget.GetProperty("blendEnabled").GetBoolean());
        Assert.Equal(15UL, colorTarget.GetProperty("writeMask").GetUInt64());
        var submittedLayout = pipelineCreate.Descriptor.GetProperty("vertexBuffers")[0];
        Assert.Equal(8, submittedLayout.GetProperty("strideBytes").GetInt32());
        Assert.Equal("vertex", submittedLayout.GetProperty("stepMode").GetString());
        var submittedAttribute = submittedLayout.GetProperty("attributes")[0];
        Assert.Equal("Code", submittedAttribute.GetProperty("name").GetString());
        Assert.Equal(0, submittedAttribute.GetProperty("location").GetInt32());
        Assert.Equal("uint32x2", submittedAttribute.GetProperty("format").GetString());
        Assert.Equal(0, submittedAttribute.GetProperty("offsetBytes").GetInt32());

        var submitPacket = RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(
            Assert.Single(bridge.Packets, packet => Operation(packet) == "submit"));
        Assert.Equal(
            ["beginRenderPass", "setRenderPipeline", "setVertexBuffer", "drawIndirect", "endRenderPass"],
            submitPacket.Commands.Select(command => command.Kind));
        var submittedDraw = submitPacket.Commands.Single(command => command.Kind == "drawIndirect").Data;
        Assert.Equal(writePackets[1].Handle.DeviceId, submittedDraw.GetProperty("buffer").GetProperty("deviceId").GetGuid());
        Assert.Equal(writePackets[1].Handle.Slot, submittedDraw.GetProperty("buffer").GetProperty("slot").GetInt32());
        Assert.Equal(0UL, submittedDraw.GetProperty("offset").GetUInt64());
        Assert.Equal(1u, submittedDraw.GetProperty("drawCount").GetUInt32());
        Assert.Equal(16u, submittedDraw.GetProperty("strideBytes").GetUInt32());
    }

    [Theory]
    [InlineData(RekallAgeTextureFormat.Bgra8Unorm, "bgra8-unorm")]
    [InlineData(RekallAgeTextureFormat.Rgba8Unorm, "rgba8-unorm")]
    public void ProofUsesTheBrowserPreferredCanvasFormat(RekallAgeTextureFormat format, string expected)
    {
        var pipeline = Assert.Single(WebGpuProofWorkload.Create(format).Pipelines);
        Assert.Equal(expected, Assert.Single(pipeline.ColorFormats));
    }

    private static string Operation(string packet)
    {
        using var document = JsonDocument.Parse(packet);
        return document.RootElement.GetProperty("operation").GetString()!;
    }

    private static void AssertHandle(RekallAgeGraphicsResourceHandle expected, JsonElement actual)
    {
        Assert.Equal(expected.DeviceId, actual.GetProperty("deviceId").GetGuid());
        Assert.Equal(expected.Kind.ToString(), actual.GetProperty("kind").GetString(), ignoreCase: true);
        Assert.Equal(expected.Slot, actual.GetProperty("slot").GetInt32());
        Assert.Equal(expected.Generation, actual.GetProperty("generation").GetUInt32());
    }

    private static IEnumerable<uint> DecodeUInt32(string value)
    {
        var bytes = Convert.FromBase64String(value);
        for (var offset = 0; offset < bytes.Length; offset += sizeof(uint))
        {
            yield return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        }
    }

    private sealed class RecordingBridge : IRekallAgeWebGpuBridge
    {
        public List<string> Packets { get; } = [];

        public RekallAgeWebGpuBridgeResult Execute(string packetJson)
        {
            Packets.Add(packetJson);
            return RekallAgeWebGpuBridgeResult.Success;
        }

        public ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RekallAgeWebGpuBridgeResult.Success);

        public ValueTask<RekallAgeWebGpuBridgeResult> DrainAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RekallAgeWebGpuBridgeResult.Success);
    }
}
