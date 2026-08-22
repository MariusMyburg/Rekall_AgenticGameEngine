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
    }

    private sealed class SequencedBridge(params RekallAgeWebGpuBridgeResult[] results) : IRekallAgeWebGpuBridge
    {
        private readonly Queue<RekallAgeWebGpuBridgeResult> _results = new(results);
        public RekallAgeWebGpuBridgeResult Execute(string packetJson) => _results.Count > 0 ? _results.Dequeue() : RekallAgeWebGpuBridgeResult.Success;
        public ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(RekallAgeWebGpuBridgeResult.Success);
    }

    private sealed class CancellingBridge : IRekallAgeWebGpuBridge
    {
        public RekallAgeWebGpuBridgeResult Execute(string packetJson) => RekallAgeWebGpuBridgeResult.Success;
        public ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default) => ValueTask.FromCanceled<RekallAgeWebGpuBridgeResult>(cancellationToken);
    }
}
