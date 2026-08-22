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
}
