using System.Text.Json;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;

namespace Rekall.Age.Tests.Rendering;

public sealed class WebGpuProtocolTests
{
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
    public void ProtocolRejectsUnknownVersionsAndOversizedPackets()
    {
        Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>("{\"version\":2}"));
        Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Serialize(new
            {
                Version = 1,
                Data = new string('x', RekallAgeWebGpuProtocol.MaximumPacketBytes)
            }));
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
}
