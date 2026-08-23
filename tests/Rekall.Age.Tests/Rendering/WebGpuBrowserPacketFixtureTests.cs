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

    [Fact]
    public void BrowserBridgeResultsRejectOversizedUtf8ResponsesAndDiagnostics()
    {
        var response = "{\"succeeded\":false,\"diagnostics\":[{\"code\":\"X\",\"message\":\""
            + new string('\u20ac', RekallAgeWebGpuProtocol.MaximumBridgeDiagnosticMessageBytes)
            + "\"}]}";

        var result = RekallAgeWebGpuProtocol.DeserializeBridgeResult(response);

        Assert.False(result.Succeeded);
        Assert.Equal("REKALL_WEBGPU_BRIDGE_RESULT_INVALID", Assert.Single(result.Diagnostics).Code);
        Assert.Equal("REKALL_WEBGPU_BRIDGE_RESULT_INVALID", RekallAgeWebGpuProtocol.DeserializeBridgeResult(
            new string('x', RekallAgeWebGpuProtocol.MaximumBridgeResultBytes + 1)).Diagnostics.Single().Code);
    }

    [Fact]
    public void BrowserInitializationRequiresEveryDeviceLimitAndRetainsPreferredCanvasFormat()
    {
        const string complete = """
            {"succeeded":true,"diagnostics":[],"capabilities":{"preferredCanvasFormat":"rgba8unorm","limits":{"maxBufferSize":268435456,"maxTextureDimension1D":8192,"maxTextureDimension2D":8192,"maxTextureDimension3D":2048,"maxTextureArrayLayers":256,"maxColorAttachments":8,"maxBindingsPerBindGroup":1000,"maxVertexBuffers":8,"maxVertexAttributes":16,"maxVertexBufferArrayStride":2048,"maxComputeWorkgroupsPerDimension":65535},"features":["timestamp-query"]}}
            """;
        const string missing = """
            {"succeeded":true,"diagnostics":[],"capabilities":{"preferredCanvasFormat":"bgra8unorm","limits":{"maxBufferSize":268435456},"features":[]}}
            """;

        var initialized = RekallAgeWebGpuProtocol.DeserializeInitializationResult(complete);
        var invalid = RekallAgeWebGpuProtocol.DeserializeInitializationResult(missing);

        Assert.True(initialized.Succeeded, string.Join(Environment.NewLine, initialized.Diagnostics.Select(item => item.Message)));
        Assert.Equal(RekallAgeTextureFormat.Rgba8Unorm, initialized.PreferredCanvasFormat);
        Assert.Equal(268435456UL, initialized.Capabilities!.MaximumBufferSizeBytes);
        Assert.Equal(1000, initialized.Capabilities.MaximumBindingsPerLayout);
        Assert.Equal(8, initialized.Capabilities.MaximumVertexBuffers);
        Assert.True(initialized.Capabilities.SupportsTimestampQueries);
        Assert.False(invalid.Succeeded);
        Assert.Null(invalid.Capabilities);
        Assert.Equal("REKALL_WEBGPU_CAPABILITIES_INVALID", Assert.Single(invalid.Diagnostics).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("depth24plus")]
    [InlineData("bgra8unorm-srgb")]
    public void BrowserInitializationRejectsMissingOrUnsupportedPreferredCanvasFormats(string format)
    {
        var json = "{\"succeeded\":true,\"diagnostics\":[],\"capabilities\":{\"preferredCanvasFormat\":\"" + format
            + "\",\"limits\":{\"maxBufferSize\":1,\"maxTextureDimension1D\":1,\"maxTextureDimension2D\":1,\"maxTextureDimension3D\":1,\"maxTextureArrayLayers\":1,\"maxColorAttachments\":1,\"maxBindingsPerBindGroup\":1,\"maxVertexBuffers\":1,\"maxVertexAttributes\":1,\"maxVertexBufferArrayStride\":1,\"maxComputeWorkgroupsPerDimension\":1},\"features\":[]}}";

        var result = RekallAgeWebGpuProtocol.DeserializeInitializationResult(json);

        Assert.False(result.Succeeded);
        Assert.Equal("REKALL_WEBGPU_CAPABILITIES_INVALID", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void StrictV1ThreeDimensionalWritePacketHasNoAdditiveDepthField()
    {
        var texture = new RekallAgeGraphicsResourceHandle(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), RekallAgeGraphicsResourceKind.Texture, 9, 1);

        var json = RekallAgeWebGpuProtocol.Serialize(new RekallAgeWebGpuWriteTexturePacket(
            1, texture, 1, 0, Convert.ToBase64String(new byte[64])));
        var nodeFixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "webgpu-write-texture-3d-v1.json")).Trim();

        using var document = JsonDocument.Parse(json);
        Assert.Equal(nodeFixture, json);
        Assert.Equal(1, document.RootElement.GetProperty("mipLevel").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("arrayLayer").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("depth", out _));
        Assert.Equal("writeTexture", document.RootElement.GetProperty("operation").GetString());
    }

    [Theory]
    [InlineData("{\"version\":1,\"operation\":\"writeBuffer\",\"handle\":{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"texture\",\"slot\":1,\"generation\":1},\"offset\":0,\"dataBase64\":\"AA==\"}", typeof(RekallAgeWebGpuWriteBufferPacket))]
    [InlineData("{\"version\":1,\"operation\":\"writeTexture\",\"handle\":{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"buffer\",\"slot\":1,\"generation\":1},\"mipLevel\":0,\"arrayLayer\":0,\"dataBase64\":\"AA==\"}", typeof(RekallAgeWebGpuWriteTexturePacket))]
    [InlineData("{\"version\":1,\"operation\":\"importCanvasOutput\",\"texture\":{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"buffer\",\"slot\":1,\"generation\":1},\"renderTarget\":{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"renderTarget\",\"slot\":2,\"generation\":1},\"width\":1,\"height\":1,\"format\":\"bgra8Unorm\"}", typeof(RekallAgeWebGpuImportCanvasOutputPacket))]
    public void LiteralWrongKindOperationPacketsFailClosed(string json, Type packetType)
    {
        var exception = packetType == typeof(RekallAgeWebGpuWriteBufferPacket)
            ? Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuWriteBufferPacket>(json))
            : packetType == typeof(RekallAgeWebGpuWriteTexturePacket)
                ? Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuWriteTexturePacket>(json))
                : Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuImportCanvasOutputPacket>(json));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_RESOURCE_TYPE_MISMATCH", exception.Diagnostic.Code);
    }

    [Fact]
    public void LiteralRenderTargetPacketRejectsWrongKindAttachmentTexture()
    {
        const string json = "{\"version\":1,\"operation\":\"create\",\"resourceType\":\"renderTarget\",\"handle\":{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"renderTarget\",\"slot\":2,\"generation\":1},\"descriptor\":{\"colorAttachments\":[{\"texture\":{\"deviceId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"buffer\",\"slot\":1,\"generation\":1},\"mipLevel\":0,\"arrayLayer\":0}],\"depthStencilAttachment\":null,\"width\":1,\"height\":1}}";

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(json));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_DESCRIPTOR_INVALID", exception.Diagnostic.Code);
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
