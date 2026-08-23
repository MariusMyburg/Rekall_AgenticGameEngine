using System.Text.Json;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;

namespace Rekall.Age.Tests.Rendering;

public sealed class WebGpuBrowserPacketFixtureTests
{
    private static readonly RekallAgeGraphicsResourceHandle Buffer = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), RekallAgeGraphicsResourceKind.Buffer, 7, 1);

    [Fact]
    public void SerializerMatchesTheLiteralBrowserCreateUploadAndSubmitFixtures()
    {
        var create = RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuCreatePacket(
            1, "buffer", Buffer, JsonSerializer.SerializeToElement(new RekallAgeBufferDescriptor(16, RekallAgeBufferUsage.Vertex, Label: "literal-browser-fixture"))));
        var upload = RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuWriteBufferPacket(1, Buffer, 4, "AAECAw=="));
        var submit = RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuSubmitPacket(1, "literal-submit",
        [
            new("beginComputePass", RekallAgeWebGpuProtocol.ToJsonElement(new RekallAgeBeginComputePassCommand("literal-compute"))),
            new("dispatch", RekallAgeWebGpuProtocol.ToJsonElement(new RekallAgeDispatchCommand(2, 3, 4))),
            new("endComputePass", RekallAgeWebGpuProtocol.ToJsonElement(new RekallAgeEndComputePassCommand()))
        ]));

        using var createDocument = JsonDocument.Parse(create);
        using var uploadDocument = JsonDocument.Parse(upload);
        using var submitDocument = JsonDocument.Parse(submit);
        Assert.Equal("buffer", createDocument.RootElement.GetProperty("resourceType").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", createDocument.RootElement.GetProperty("handle").GetProperty("deviceId").GetString());
        Assert.Equal("vertex", createDocument.RootElement.GetProperty("descriptor").GetProperty("usage").GetString());
        Assert.Equal("AAECAw==", uploadDocument.RootElement.GetProperty("dataBase64").GetString());
        var commandKinds = submitDocument.RootElement.GetProperty("commands").EnumerateArray().Select(item => item.GetProperty("kind").GetString()).ToArray();
        Assert.Equal("beginComputePass", commandKinds[0]);
        Assert.Equal("dispatch", commandKinds[1]);
        Assert.Equal("endComputePass", commandKinds[2]);
    }

    [Fact]
    public void LiteralBrowserFixturesRejectMutatedVersionResourceAndCommandKinds()
    {
        const string version = "{\"version\":2,\"operation\":\"writeBuffer\",\"handle\":{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"buffer\",\"slot\":7,\"generation\":1},\"offset\":0,\"dataBase64\":\"AA==\"}";
        const string resource = "{\"version\":1,\"operation\":\"create\",\"resourceType\":\"texture\",\"handle\":{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"buffer\",\"slot\":7,\"generation\":1},\"descriptor\":{\"sizeBytes\":16,\"usage\":\"vertex\"}}";
        const string command = "{\"version\":1,\"operation\":\"submit\",\"commands\":[{\"kind\":\"unknown\",\"data\":{}}]}";

        Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuWriteBufferPacket>(version));
        Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(resource));
        Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(command));
    }

    [Fact]
    public void InvalidBrowserBridgeResultsBecomeAStableDiagnostic()
    {
        var result = RekallAgeWebGpuProtocol.DeserializeBridgeResult("not a bridge result");

        Assert.False(result.Succeeded);
        Assert.Equal("REKALL_WEBGPU_BRIDGE_RESULT_INVALID", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void BrowserBridgeResultsRejectMissingRequiredNestedDiagnosticFields()
    {
        var result = RekallAgeWebGpuProtocol.DeserializeBridgeResult(
            "{\"succeeded\":false,\"diagnostics\":[{\"message\":\"missing code\"}]}");

        Assert.False(result.Succeeded);
        Assert.Equal("REKALL_WEBGPU_BRIDGE_RESULT_INVALID", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData(RekallAgeIndexFormat.UInt16, "uint16")]
    [InlineData(RekallAgeIndexFormat.UInt32, "uint32")]
    public void IndexFormatsUseCanonicalLiteralBrowserSpellings(RekallAgeIndexFormat format, string expected)
    {
        var data = RekallAgeWebGpuProtocol.ToJsonElement(new RekallAgeSetIndexBufferCommand(Buffer, format, 0, 16));

        Assert.Equal(expected, data.GetProperty("format").GetString());

        var packet = new RekallAgeWebGpuSubmitPacket(1, null, [new("setIndexBuffer", data)]);
        var restored = RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(RekallAgeWebGpuProtocol.Serialize(packet));
        Assert.Equal(expected, restored.Commands[0].Data.GetProperty("format").GetString());
    }

    [Theory]
    [InlineData("\"uInt16\"")]
    [InlineData("\"uInt32\"")]
    [InlineData("\"unknown\"")]
    [InlineData("0")]
    [InlineData("1")]
    public void IndexFormatsRejectNoncanonicalBrowserSpellings(string formatJson)
    {
        var packet = "{\"version\":1,\"operation\":\"submit\",\"commands\":[{\"kind\":\"setIndexBuffer\",\"data\":{\"buffer\":{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"buffer\",\"slot\":7,\"generation\":1},\"format\":"
            + formatJson
            + ",\"offset\":0,\"sizeBytes\":16}}]}";

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(packet));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_COMMAND_PAYLOAD_INVALID", exception.Diagnostic.Code);
    }
}
