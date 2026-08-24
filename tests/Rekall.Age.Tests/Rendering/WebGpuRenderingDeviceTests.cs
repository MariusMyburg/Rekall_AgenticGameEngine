using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;
using System.Text.Json;

namespace Rekall.Age.Tests.Rendering;

public sealed class WebGpuRenderingDeviceTests
{
    [Fact]
    public void BridgeFailureRollsBackResourceAndReturnsStableDiagnostic()
    {
        var bridge = new RecordingWebGpuBridge(new(false, [new("REKALL_WEBGPU_CREATE_FAILED", "rejected")]));
        using var device = CreateDevice(bridge);

        var result = device.CreateBuffer(new(16, RekallAgeBufferUsage.Vertex));

        Assert.False(result.Created);
        Assert.Empty(device.InspectResources());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REKALL_WEBGPU_CREATE_FAILED");
    }

    [Fact]
    public void BrowserBackendRejectsNonWgslShadersBeforeBridgeMutation()
    {
        var bridge = new RecordingWebGpuBridge();
        using var device = CreateDevice(bridge);

        var result = device.CreateShaderModule(new(
            RekallAgeShaderStage.Vertex,
            RekallAgeShaderSourceLanguage.Glsl,
            "void main(){}"));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REKALL_WEBGPU_SHADER_LANGUAGE_REQUIRED");
        Assert.Empty(bridge.Packets);
    }

    [Fact]
    public void ResourceUploadsAndDestroyEmitExactBoundedPackets()
    {
        var bridge = new RecordingWebGpuBridge();
        using var device = CreateDevice(bridge);
        var buffer = device.CreateBuffer(new(16, RekallAgeBufferUsage.Vertex | RekallAgeBufferUsage.TransferDestination));

        Assert.True(buffer.Created);
        Assert.True(device.WriteBuffer(buffer.Handle, 4, new byte[] { 1, 2, 3, 4 }).Valid);
        Assert.True(device.Destroy(buffer.Handle).Valid);

        using var create = JsonDocument.Parse(bridge.Packets[0]);
        Assert.Equal("create", create.RootElement.GetProperty("operation").GetString());
        Assert.Equal("buffer", create.RootElement.GetProperty("resourceType").GetString());
        using var write = JsonDocument.Parse(bridge.Packets[1]);
        Assert.Equal("writeBuffer", write.RootElement.GetProperty("operation").GetString());
        Assert.Equal(4UL, write.RootElement.GetProperty("offset").GetUInt64());
        Assert.Equal("AQIDBA==", write.RootElement.GetProperty("dataBase64").GetString());
        using var destroy = JsonDocument.Parse(bridge.Packets[2]);
        Assert.Equal("destroy", destroy.RootElement.GetProperty("operation").GetString());
        Assert.Equal(buffer.Handle.Slot, destroy.RootElement.GetProperty("handle").GetProperty("slot").GetInt32());
    }

    [Fact]
    public void SubmitDelegatesConformanceCommandsInTheirOriginalOrderAndPropagatesBridgeFailure()
    {
        var bridge = new RecordingWebGpuBridge(submitResult: new(false, [new("REKALL_WEBGPU_SUBMIT_FAILED", "device lost")]));
        using var device = CreateDevice(bridge);
        using var encoder = device.BeginCommandEncoder("ordered");
        Assert.True(encoder.CopyBuffer(CreateCopySource(device), 0, CreateCopyDestination(device), 0, 4).Valid);
        var commands = encoder.Finish();

        var result = device.Submit(commands);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REKALL_WEBGPU_SUBMIT_FAILED");
        using var submit = JsonDocument.Parse(bridge.Packets[^1]);
        Assert.Equal("submit", submit.RootElement.GetProperty("operation").GetString());
        Assert.Equal("copyBuffer", submit.RootElement.GetProperty("commands")[0].GetProperty("kind").GetString());
        Assert.Equal(4UL, submit.RootElement.GetProperty("commands")[0].GetProperty("data").GetProperty("sizeBytes").GetUInt64());
    }

    [Fact]
    public void SubmitValidationFailureDoesNotReachBridge()
    {
        var bridge = new RecordingWebGpuBridge();
        using var device = CreateDevice(bridge);

        var result = device.Submit(new(Guid.NewGuid(), "foreign", [], true));

        Assert.False(result.Valid);
        Assert.Empty(bridge.Packets);
    }

    [Fact]
    public async Task FlushFailureFaultsTheDeviceAndDisposeDestroysRetainedResources()
    {
        var bridge = new RecordingWebGpuBridge(flushResult: new(false, [new("REKALL_WEBGPU_DEVICE_LOST", "lost")]));
        var device = CreateDevice(bridge);
        var buffer = device.CreateBuffer(new(16, RekallAgeBufferUsage.Vertex));
        Assert.True(buffer.Created);

        var flush = await device.FlushAsync();
        var packetsBeforeMutation = bridge.Packets.Count;
        var afterFault = device.CreateBuffer(new(16, RekallAgeBufferUsage.Vertex));
        device.Dispose();

        Assert.False(flush.Valid);
        Assert.Contains(flush.Diagnostics, diagnostic => diagnostic.Code == "REKALL_WEBGPU_DEVICE_LOST");
        Assert.False(afterFault.Created);
        Assert.Contains(afterFault.Diagnostics, diagnostic => diagnostic.Code == "REKALL_WEBGPU_DEVICE_FAULTED");
        Assert.Equal(packetsBeforeMutation, bridge.Packets.Count - 1);
        using var destroy = JsonDocument.Parse(bridge.Packets[^1]);
        Assert.Equal("destroy", destroy.RootElement.GetProperty("operation").GetString());
    }

    [Fact]
    public void ImportCanvasOutputCreatesInspectableTextureAndTargetAndEmitsOneImportPacket()
    {
        var bridge = new RecordingWebGpuBridge();
        using var device = CreateDevice(bridge);

        var output = device.ImportCanvasOutput(640, 480, RekallAgeTextureFormat.Bgra8Unorm, "engine.output");

        Assert.True(output.Created);
        Assert.Equal(RekallAgeGraphicsResourceKind.RenderTarget, output.Handle.Kind);
        Assert.Equal(2, device.InspectResources().Count);
        using var import = JsonDocument.Parse(bridge.Packets.Single());
        Assert.Equal("importCanvasOutput", import.RootElement.GetProperty("operation").GetString());
        Assert.Equal(640, import.RootElement.GetProperty("width").GetInt32());
    }

    [Fact]
    public void ProtocolFailsClosedForUnknownSubmissionCommandKinds()
    {
        const string packet = """
            {"version":1,"label":"bad","commands":[{"kind":"unknown","data":{}}],"operation":"submit"}
            """;

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
            RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(packet));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_COMMAND_KIND_INVALID", exception.Diagnostic.Code);
    }

    [Fact]
    public void DisposedDeviceReturnsAClosedEncoderInsteadOfCallingTheConformanceBackend()
    {
        var bridge = new RecordingWebGpuBridge();
        var device = CreateDevice(bridge);
        device.Dispose();

        using var encoder = device.BeginCommandEncoder();
        var result = encoder.CopyBuffer(default, 0, default, 0, 4);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REKALL_WEBGPU_DEVICE_DISPOSED");
        Assert.Empty(bridge.Packets);
    }

    [Fact]
    public void OversizedUploadFailsBeforeConformanceMutationOrBridgeSerialization()
    {
        var bridge = new RecordingWebGpuBridge();
        using var device = CreateDevice(bridge, maximumBufferSize: 32UL * 1024 * 1024);
        var buffer = device.CreateBuffer(new(24UL * 1024 * 1024, RekallAgeBufferUsage.TransferDestination));
        var packets = bridge.Packets.Count;

        var result = device.WriteBuffer(buffer.Handle, 0, new byte[13 * 1024 * 1024]);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REKALL_WEBGPU_PROTOCOL_PACKET_TOO_LARGE");
        Assert.Equal(0UL, device.InspectResources().Single().UploadedBytes);
        Assert.Equal(packets, bridge.Packets.Count);
    }

    [Fact]
    public void DestroyBridgeRejectionRetainsTheResourceForRetry()
    {
        var bridge = new SequencedBridge(RekallAgeWebGpuBridgeResult.Success, new(false, [new("REKALL_WEBGPU_DESTROY_FAILED", "no")]), RekallAgeWebGpuBridgeResult.Success);
        using var device = CreateDevice(bridge);
        var buffer = device.CreateBuffer(new(16, RekallAgeBufferUsage.Vertex));

        Assert.False(device.Destroy(buffer.Handle).Valid);
        Assert.Single(device.InspectResources());
        Assert.True(device.Destroy(buffer.Handle).Valid);
        Assert.Empty(device.InspectResources());
    }

    [Fact]
    public void DestroyCanvasOutputRetriesHiddenTextureAfterPartialFailure()
    {
        var bridge = new HiddenTextureDestroyOnceBridge();
        using var device = CreateDevice(bridge);
        var output = device.ImportCanvasOutput(320, 180, RekallAgeTextureFormat.Bgra8Unorm);
        Assert.True(output.Created);

        var first = device.Destroy(output.Handle);

        Assert.False(first.Valid);
        Assert.Equal(RekallAgeGraphicsResourceKind.Texture, Assert.Single(device.InspectResources()).Handle.Kind);
        var packetsAfterFirstAttempt = bridge.Packets.Count;

        Assert.True(device.Destroy(output.Handle).Valid);
        Assert.Empty(device.InspectResources());
        Assert.Equal(packetsAfterFirstAttempt + 1, bridge.Packets.Count);

        Assert.True(device.Destroy(output.Handle).Valid);
        Assert.Equal(packetsAfterFirstAttempt + 1, bridge.Packets.Count);
    }

    [Fact]
    public async Task FlushCancellationDoesNotFaultTheDevice()
    {
        var bridge = new CancellingBridge();
        using var device = CreateDevice(bridge);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => device.FlushAsync(cancellation.Token).AsTask());
        Assert.True(device.CreateBuffer(new(16, RekallAgeBufferUsage.Vertex)).Created);
    }

    [Fact]
    public void ProtocolRejectsSubmissionPayloadMissingConcreteCommandFields()
    {
        const string packet = """{"version":1,"commands":[{"kind":"copyBuffer","data":{"sourceOffset":0}}],"operation":"submit"}""";

        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(packet));

        Assert.Equal("REKALL_WEBGPU_PROTOCOL_COMMAND_PAYLOAD_INVALID", exception.Diagnostic.Code);
    }

    [Theory]
    [InlineData("""{"version":1,"commands":[{"kind":"endRenderPass","data":{"extra":1}}],"operation":"submit"}""")]
    [InlineData("""{"version":1,"commands":[{"kind":"beginRenderPass","data":{"descriptor":null}}],"operation":"submit"}""")]
    [InlineData("""{"version":1,"commands":[{"kind":"setRenderPipeline","data":{"pipeline":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":0,"generation":1}}}],"operation":"submit"}""")]
    public void ProtocolRejectsUnknownNullAndWrongKindCommandPayloads(string packet)
    {
        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(packet));
        Assert.Equal("REKALL_WEBGPU_PROTOCOL_COMMAND_PAYLOAD_INVALID", exception.Diagnostic.Code);
    }

    [Fact]
    public void DestroyRejectsForeignHandlesButAcceptsOnlyItsOwnPreviouslyDestroyedHandle()
    {
        var bridge = new RecordingWebGpuBridge();
        using var device = CreateDevice(bridge);
        var buffer = device.CreateBuffer(new(16, RekallAgeBufferUsage.Vertex));
        Assert.True(device.Destroy(buffer.Handle).Valid);
        Assert.True(device.Destroy(buffer.Handle).Valid);
        Assert.False(device.Destroy(buffer.Handle with { Generation = buffer.Handle.Generation + 1 }).Valid);
        Assert.False(device.Destroy(buffer.Handle with { DeviceId = Guid.NewGuid() }).Valid);
    }

    [Fact]
    public void ProtocolAcceptsItsOwnComputePassPacket()
    {
        var packet = new RekallAgeWebGpuSubmitPacket(1, null, [new("beginComputePass", RekallAgeWebGpuProtocol.ToJsonElement(new RekallAgeBeginComputePassCommand("ok"))), new("endComputePass", RekallAgeWebGpuProtocol.ToJsonElement(new RekallAgeEndComputePassCommand()))]);
        Assert.Equal(2, RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(RekallAgeWebGpuProtocol.Serialize(packet)).Commands.Count);
    }

    [Theory]
    [InlineData("copyBuffer", "source", "texture")]
    [InlineData("copyBuffer", "destination", "texture")]
    public void ProtocolRejectsWrongCopyHandleKinds(string commandKind, string property, string kind)
    {
        var data = "{\"source\":{\"kind\":\"buffer\"},\"sourceOffset\":0,\"destination\":{\"kind\":\"buffer\"},\"destinationOffset\":0,\"sizeBytes\":4}".Replace($"\"{property}\":{{\"kind\":\"buffer\"}}", $"\"{property}\":{{\"kind\":\"{kind}\"}}");
        var json = $"{{\"version\":1,\"commands\":[{{\"kind\":\"{commandKind}\",\"data\":{data}}}],\"operation\":\"submit\"}}";
        Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(json));
    }

    [Fact]
    public void ProtocolRejectsWrongOrUnknownNestedRenderPassDescriptor()
    {
        const string wrong = """{"version":1,"commands":[{"kind":"beginRenderPass","data":{"descriptor":{"renderTarget":{"kind":"texture"},"colorClearValues":[]}}}],"operation":"submit"}""";
        const string extra = """{"version":1,"commands":[{"kind":"beginRenderPass","data":{"descriptor":{"renderTarget":{"kind":"renderTarget"},"colorClearValues":[],"extra":1}}}],"operation":"submit"}""";
        Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(wrong));
        Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(extra));
    }

    [Theory]
    [InlineData("""{"version":1,"commands":[{"kind":"beginRenderPass","data":{"descriptor":{"renderTarget":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"renderTarget","slot":0,"generation":1},"colorClearValues":{}}}}],"operation":"submit"}""")]
    [InlineData("""{"version":1,"commands":[{"kind":"beginRenderPass","data":{"descriptor":{"renderTarget":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"renderTarget","slot":0,"generation":1},"colorClearValues":[{"red":0,"green":0,"blue":0}]}}}],"operation":"submit"}""")]
    [InlineData("""{"version":1,"commands":[{"kind":"beginRenderPass","data":{"descriptor":{"renderTarget":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"renderTarget","slot":0,"generation":1},"colorClearValues":[{"red":0,"green":0,"blue":0,"alpha":1,"extra":0}]}}}],"operation":"submit"}""")]
    public void ProtocolRejectsNonArrayOrInexactRenderPassClearValues(string packet)
    {
        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(packet));
        Assert.Equal("REKALL_WEBGPU_PROTOCOL_COMMAND_PAYLOAD_INVALID", exception.Diagnostic.Code);
    }

    [Theory]
    [MemberData(nameof(WrongKindHandleCommandPackets))]
    public void ProtocolRejectsWrongKindsForEveryHandleBearingCommand(string packet)
    {
        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(packet));
        Assert.Equal("REKALL_WEBGPU_PROTOCOL_COMMAND_PAYLOAD_INVALID", exception.Diagnostic.Code);
    }

    [Theory]
    [InlineData("""{"version":1,"commands":[{"kind":"setVertexBuffer","data":{"slot":0,"buffer":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","generation":1},"offset":0,"sizeBytes":16}}],"operation":"submit"}""")]
    [InlineData("""{"version":1,"commands":[{"kind":"setIndexBuffer","data":{"buffer":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":0,"generation":1,"extra":true},"format":"uint16","offset":0,"sizeBytes":16}}],"operation":"submit"}""")]
    public void ProtocolRejectsIncompleteOrExtendedCommandHandles(string packet)
    {
        var exception = Assert.Throws<RekallAgeWebGpuProtocolException>(() => RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(packet));
        Assert.Equal("REKALL_WEBGPU_PROTOCOL_COMMAND_PAYLOAD_INVALID", exception.Diagnostic.Code);
    }

    public static TheoryData<string> WrongKindHandleCommandPackets => new()
    {
        """{"version":1,"commands":[{"kind":"copyBuffer","data":{"source":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"texture","slot":0,"generation":1},"sourceOffset":0,"destination":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":1,"generation":1},"destinationOffset":0,"sizeBytes":4}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"copyBuffer","data":{"source":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":0,"generation":1},"sourceOffset":0,"destination":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"texture","slot":1,"generation":1},"destinationOffset":0,"sizeBytes":4}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"beginRenderPass","data":{"descriptor":{"renderTarget":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"texture","slot":0,"generation":1},"colorClearValues":[]}}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"setRenderPipeline","data":{"pipeline":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"computePipeline","slot":0,"generation":1}}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"setComputePipeline","data":{"pipeline":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"renderPipeline","slot":0,"generation":1}}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"setBindingSet","data":{"index":0,"bindingSet":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"buffer","slot":0,"generation":1}}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"setVertexBuffer","data":{"slot":0,"buffer":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"texture","slot":0,"generation":1},"offset":0,"sizeBytes":16}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"setIndexBuffer","data":{"buffer":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"texture","slot":0,"generation":1},"format":"uint16","offset":0,"sizeBytes":16}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"drawIndirect","data":{"buffer":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"texture","slot":0,"generation":1},"offset":0,"drawCount":1,"strideBytes":16}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"drawIndexedIndirect","data":{"buffer":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"texture","slot":0,"generation":1},"offset":0,"drawCount":1,"strideBytes":20}}],"operation":"submit"}""",
        """{"version":1,"commands":[{"kind":"dispatchIndirect","data":{"buffer":{"deviceId":"11111111-1111-1111-1111-111111111111","kind":"texture","slot":0,"generation":1},"offset":0}}],"operation":"submit"}"""
    };

    [Fact]
    public void AdapterGeneratedRenderAndComputeCommandsRemainStrictProtocolPackets()
    {
        var bridge = new RecordingWebGpuBridge();
        using var device = CreateDevice(bridge);
        var layout = device.CreateBindingLayout(new(
            [new(0, RekallAgeBindingType.UniformBuffer, RekallAgeShaderStage.Vertex | RekallAgeShaderStage.Fragment | RekallAgeShaderStage.Compute, 16)]));
        var uniform = device.CreateBuffer(new(16, RekallAgeBufferUsage.Uniform));
        var bindingSet = device.CreateBindingSet(new(layout.Handle, [new(0, uniform.Handle, 0, 16)]));
        var vertexShader = device.CreateShaderModule(new(RekallAgeShaderStage.Vertex, RekallAgeShaderSourceLanguage.Wgsl, "@vertex fn main() -> @builtin(position) vec4f { return vec4f(); }"));
        var fragmentShader = device.CreateShaderModule(new(RekallAgeShaderStage.Fragment, RekallAgeShaderSourceLanguage.Wgsl, "@fragment fn main() -> @location(0) vec4f { return vec4f(1.0); }"));
        var renderPipeline = device.CreateGraphicsPipeline(new(vertexShader.Handle, fragmentShader.Handle, [layout.Handle], [new(RekallAgeTextureFormat.Bgra8Unorm)]));
        var output = device.ImportCanvasOutput(64, 64, RekallAgeTextureFormat.Bgra8Unorm);
        var vertexBuffer = device.CreateBuffer(new(16, RekallAgeBufferUsage.Vertex));
        var indexBuffer = device.CreateBuffer(new(16, RekallAgeBufferUsage.Index));
        var drawIndirectBuffer = device.CreateBuffer(new(64, RekallAgeBufferUsage.Indirect));
        Assert.All(new[] { layout, uniform, bindingSet, vertexShader, fragmentShader, renderPipeline, output, vertexBuffer, indexBuffer, drawIndirectBuffer }, created => Assert.True(created.Created));

        using (var encoder = device.BeginCommandEncoder("render"))
        {
            Assert.True(encoder.BeginRenderPass(new(output.Handle, [new(0, 0, 0, 1)], Label: "render pass")).Valid);
            Assert.True(encoder.SetRenderPipeline(renderPipeline.Handle).Valid);
            Assert.True(encoder.SetBindingSet(0, bindingSet.Handle).Valid);
            Assert.True(encoder.SetVertexBuffer(0, vertexBuffer.Handle, 0, 16).Valid);
            Assert.True(encoder.SetIndexBuffer(indexBuffer.Handle, RekallAgeIndexFormat.UInt16, 0, 16).Valid);
            Assert.True(encoder.DrawIndirect(drawIndirectBuffer.Handle, 0).Valid);
            Assert.True(encoder.DrawIndexedIndirect(drawIndirectBuffer.Handle, 16).Valid);
            Assert.True(encoder.EndRenderPass().Valid);
            var submission = device.Submit(encoder.Finish());
            Assert.True(submission.Valid, string.Join(Environment.NewLine, submission.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        }

        var computeShader = device.CreateShaderModule(new(RekallAgeShaderStage.Compute, RekallAgeShaderSourceLanguage.Wgsl, "@compute @workgroup_size(1) fn main() {}"));
        var computePipeline = device.CreateComputePipeline(new(computeShader.Handle, [layout.Handle]));
        var dispatchIndirectBuffer = device.CreateBuffer(new(16, RekallAgeBufferUsage.Indirect));
        Assert.All(new[] { computeShader, computePipeline, dispatchIndirectBuffer }, created => Assert.True(created.Created));

        using (var encoder = device.BeginCommandEncoder("compute"))
        {
            Assert.True(encoder.BeginComputePass("compute pass").Valid);
            Assert.True(encoder.SetComputePipeline(computePipeline.Handle).Valid);
            Assert.True(encoder.SetBindingSet(0, bindingSet.Handle).Valid);
            Assert.True(encoder.Dispatch(1).Valid);
            Assert.True(encoder.DispatchIndirect(dispatchIndirectBuffer.Handle, 0).Valid);
            Assert.True(encoder.EndComputePass().Valid);
            var submission = device.Submit(encoder.Finish());
            Assert.True(submission.Valid, string.Join(Environment.NewLine, submission.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        }

        var submissions = bridge.Packets.Where(packet => PacketOperation(packet) == "submit").ToArray();
        Assert.Equal(2, submissions.Length);
        Assert.All(submissions, packet => Assert.NotEmpty(RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuSubmitPacket>(packet).Commands));
    }

    private static string? PacketOperation(string packet)
    {
        using var document = JsonDocument.Parse(packet);
        return document.RootElement.GetProperty("operation").GetString();
    }

    [Fact]
    public void SubmitRejectsOversizedRenderPassLabelBeforeBridgeOrConformanceSubmission()
    {
        var bridge = new RecordingWebGpuBridge();
        using var device = CreateDevice(bridge);
        var output = device.ImportCanvasOutput(64, 64, RekallAgeTextureFormat.Bgra8Unorm);
        var packetCount = bridge.Packets.Count;

        var result = device.Submit(new(device.DeviceId, null,
        [
            new RekallAgeBeginRenderPassCommand(new(output.Handle, [], Label: new string('x', RekallAgeWebGpuProtocol.MaximumLabelBytes + 1))),
            new RekallAgeEndRenderPassCommand()
        ]));

        Assert.False(result.Valid);
        Assert.Equal(0, device.ConformanceSubmissionCount);
        Assert.Equal(packetCount, bridge.Packets.Count);
    }

    [Fact]
    public void SubmitRejectsOversizedComputePassLabelBeforeBridgeOrConformanceSubmission()
    {
        var bridge = new RecordingWebGpuBridge();
        using var device = CreateDevice(bridge);

        var result = device.Submit(new(device.DeviceId, null,
        [
            new RekallAgeBeginComputePassCommand(new string('x', RekallAgeWebGpuProtocol.MaximumLabelBytes + 1)),
            new RekallAgeEndComputePassCommand()
        ]));

        Assert.False(result.Valid);
        Assert.Equal(0, device.ConformanceSubmissionCount);
        Assert.Empty(bridge.Packets);
    }

    private static RekallAgeGraphicsResourceHandle CreateCopySource(RekallAgeWebGpuRenderingDevice device) =>
        device.CreateBuffer(new(16, RekallAgeBufferUsage.CopySource)).Handle;

    private static RekallAgeGraphicsResourceHandle CreateCopyDestination(RekallAgeWebGpuRenderingDevice device) =>
        device.CreateBuffer(new(16, RekallAgeBufferUsage.TransferDestination)).Handle;

    private static RekallAgeWebGpuRenderingDevice CreateDevice(IRekallAgeWebGpuBridge bridge, ulong? maximumBufferSize = null) =>
        new(bridge, RekallAgeRenderingDeviceCapabilities.DesktopBaseline("WebGPU") with { MaximumBufferSizeBytes = maximumBufferSize ?? (1UL << 30) });

    private sealed class RecordingWebGpuBridge(
        RekallAgeWebGpuBridgeResult? result = null,
        RekallAgeWebGpuBridgeResult? submitResult = null,
        RekallAgeWebGpuBridgeResult? flushResult = null) : IRekallAgeWebGpuBridge
    {
        private readonly RekallAgeWebGpuBridgeResult _result = result ?? RekallAgeWebGpuBridgeResult.Success;
        private readonly RekallAgeWebGpuBridgeResult _submitResult = submitResult ?? result ?? RekallAgeWebGpuBridgeResult.Success;
        private readonly RekallAgeWebGpuBridgeResult _flushResult = flushResult ?? RekallAgeWebGpuBridgeResult.Success;

        public List<string> Packets { get; } = [];

        public RekallAgeWebGpuBridgeResult Execute(string packetJson)
        {
            Packets.Add(packetJson);
            using var packet = JsonDocument.Parse(packetJson);
            return packet.RootElement.GetProperty("operation").GetString() == "submit" ? _submitResult : _result;
        }

        public ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_flushResult);

        public ValueTask<RekallAgeWebGpuBridgeResult> DrainAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_flushResult);
    }

    private sealed class SequencedBridge(params RekallAgeWebGpuBridgeResult[] results) : IRekallAgeWebGpuBridge
    {
        private readonly Queue<RekallAgeWebGpuBridgeResult> _results = new(results);
        public RekallAgeWebGpuBridgeResult Execute(string packetJson) => _results.Count > 0 ? _results.Dequeue() : RekallAgeWebGpuBridgeResult.Success;
        public ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(RekallAgeWebGpuBridgeResult.Success);
        public ValueTask<RekallAgeWebGpuBridgeResult> DrainAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(RekallAgeWebGpuBridgeResult.Success);
    }

    private sealed class HiddenTextureDestroyOnceBridge : IRekallAgeWebGpuBridge
    {
        private RekallAgeGraphicsResourceHandle _hiddenTexture;
        private bool _failedHiddenTextureDestroy;

        public List<string> Packets { get; } = [];

        public RekallAgeWebGpuBridgeResult Execute(string packetJson)
        {
            Packets.Add(packetJson);
            using var packet = JsonDocument.Parse(packetJson);
            if (packet.RootElement.GetProperty("operation").GetString() == "importCanvasOutput")
            {
                _hiddenTexture = RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuImportCanvasOutputPacket>(packetJson).Texture;
            }
            else if (packet.RootElement.GetProperty("operation").GetString() == "destroy"
                && RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuDestroyPacket>(packetJson).Handle == _hiddenTexture
                && !_failedHiddenTextureDestroy)
            {
                _failedHiddenTextureDestroy = true;
                return new(false, [new("REKALL_WEBGPU_DESTROY_FAILED", "hidden texture rejected once")]);
            }

            return RekallAgeWebGpuBridgeResult.Success;
        }

        public ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RekallAgeWebGpuBridgeResult.Success);

        public ValueTask<RekallAgeWebGpuBridgeResult> DrainAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RekallAgeWebGpuBridgeResult.Success);
    }

    private sealed class CancellingBridge : IRekallAgeWebGpuBridge
    {
        public RekallAgeWebGpuBridgeResult Execute(string packetJson) => RekallAgeWebGpuBridgeResult.Success;
        public ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default) => ValueTask.FromCanceled<RekallAgeWebGpuBridgeResult>(cancellationToken);
        public ValueTask<RekallAgeWebGpuBridgeResult> DrainAsync(CancellationToken cancellationToken = default) => ValueTask.FromCanceled<RekallAgeWebGpuBridgeResult>(cancellationToken);
    }
}
