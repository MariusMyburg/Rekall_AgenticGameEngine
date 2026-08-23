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
        Assert.Equal([3u, 1u, 0u, 0u], workload.Buffers.Single(item => item.Id == "draw.arguments").InitialDataUInt32);
        var layout = Assert.Single(workload.Pipelines).VertexBuffers.Single();
        Assert.Equal(8, layout.StrideBytes);
        Assert.Equal(RekallAgeRuntimeGpuVertexFormat.Uint32x2, Assert.Single(layout.Attributes).Format);
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
        Assert.Equal(
            workload.Buffers.SelectMany(item => item.InitialDataUInt32).ToArray(),
            writePackets.SelectMany(packet => DecodeUInt32(packet.DataBase64)).ToArray());
        var submitPacket = RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(
            Assert.Single(bridge.Packets, packet => Operation(packet) == "submit"));
        Assert.Equal(
            ["beginRenderPass", "setRenderPipeline", "setVertexBuffer", "drawIndirect", "endRenderPass"],
            submitPacket.Commands.Select(command => command.Kind));
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
    }
}
