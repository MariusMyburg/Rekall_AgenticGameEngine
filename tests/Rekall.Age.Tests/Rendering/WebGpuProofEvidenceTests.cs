using System.Text.Json;
using Rekall.Age.Player.Web;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;

namespace Rekall.Age.Tests.Rendering;

public sealed class WebGpuProofEvidenceTests
{
    [Fact]
    public async Task ExecutionFlushesBeforeReadbackAndDisposesCompiledResourcesAfterEvidence()
    {
        var bridge = new LifecycleBridge();
        using var device = new RekallAgeWebGpuRenderingDevice(bridge, RekallAgeRenderingDeviceCapabilities.DesktopBaseline("WebGPU"));
        var output = device.ImportCanvasOutput(640, 480, RekallAgeTextureFormat.Bgra8Unorm);
        Assert.True(output.Created);
        var readbackCalled = false;

        var evidence = await WebGpuProofExecution.ExecuteAsync(
            device,
            output.Handle,
            RekallAgeTextureFormat.Bgra8Unorm,
            _ =>
            {
                readbackCalled = true;
                Assert.True(bridge.Flushed);
                Assert.True(device.InspectResources().Count > 2);
                return ValueTask.FromResult(new WebGpuProofReadbackResult(true, [], PixelProof(passed: true)));
            });

        Assert.True(readbackCalled);
        Assert.Equal(1, evidence.SubmittedFrames);
        Assert.True(evidence.PixelProof!.Passed);
        Assert.Equal(2, device.InspectResources().Count);
    }

    [Fact]
    public async Task ExecutionRejectsSucceededFalseEvenWhenTheBrowserSelfAssertsAPassingProof()
    {
        var evidence = await ExecuteWithReadback(new(false, [], PixelProof(passed: true)));

        Assert.Equal("REKALL_WEBGPU_READBACK_FAILED", Assert.Single(evidence.Diagnostics).Code);
        Assert.True(evidence.PixelProof!.Passed);
    }

    [Fact]
    public async Task ExecutionRecomputesAllDarkSelfAssertedPassAsFailure()
    {
        var allDark = new WebGpuPixelSample(1, 1, 4, 6, 10, 255);
        var asserted = new WebGpuPixelProof(true, 640, 480, 2560, new(allDark, allDark, allDark, allDark));

        var evidence = await ExecuteWithReadback(new(true, [], asserted));

        Assert.False(evidence.PixelProof!.Passed);
        Assert.Equal("REKALL_WEBGPU_PIXEL_PROOF_FAILED", Assert.Single(evidence.Diagnostics).Code);
    }

    [Fact]
    public async Task ExecutionIgnoresTamperedPassedFlagAndAcceptsValidRawSamples()
    {
        var evidence = await ExecuteWithReadback(new(true, [], PixelProof(passed: false)));

        Assert.True(evidence.PixelProof!.Passed);
        Assert.Empty(evidence.Diagnostics);
    }

    [Fact]
    public void EvidenceSerializationPublishesExactlyTheSixMachineReadableFields()
    {
        var evidence = new WebGpuProofEvidence(
            "WebGPU",
            1,
            "proof.webgpu.asset-independent",
            1,
            [],
            PixelProof(passed: true));

        using var json = JsonDocument.Parse(WebGpuProofEvidenceJson.Serialize(evidence));

        Assert.Equal(
            ["backend", "protocolVersion", "workloadId", "submittedFrames", "diagnostics", "pixelProof"],
            json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("WebGPU", json.RootElement.GetProperty("backend").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.True(json.RootElement.GetProperty("pixelProof").GetProperty("passed").GetBoolean());
    }

    [Fact]
    public void ReadbackDeserializationPreservesLiteralRgbaSamplesAndRejectsMalformedResults()
    {
        const string literal =
            """
            {"succeeded":true,"diagnostics":[],"pixelProof":{"passed":true,"width":640,"height":480,"bytesPerRow":2560,"samples":{"background":{"x":51,"y":38,"r":4,"g":6,"b":10,"a":255},"cyan":{"x":176,"y":361,"r":55,"g":190,"b":231,"a":255},"blue":{"x":320,"y":151,"r":53,"g":81,"b":247,"a":255},"magenta":{"x":464,"y":361,"r":193,"g":56,"b":222,"a":255}}}}
            """;

        var result = WebGpuProofEvidenceJson.DeserializeReadback(literal);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.PixelProof);
        Assert.Equal(231, result.PixelProof.Samples.Cyan.B);
        Assert.Equal(193, result.PixelProof.Samples.Magenta.R);
        var malformed = WebGpuProofEvidenceJson.DeserializeReadback("{\"succeeded\":true,\"diagnostics\":[],\"pixelProof\":null}");
        Assert.False(malformed.Succeeded);
        Assert.Equal("REKALL_WEBGPU_READBACK_RESULT_INVALID", Assert.Single(malformed.Diagnostics).Code);
    }

    [Fact]
    public void ReadbackDeserializationRejectsSucceededFalseSelfAssertedPass()
    {
        var value = new WebGpuProofReadbackResult(false, [], PixelProof(passed: true));
        var json = JsonSerializer.Serialize(value, WebGpuProofJsonContext.Default.WebGpuProofReadbackResult);

        var result = WebGpuProofEvidenceJson.DeserializeReadback(json);

        Assert.False(result.Succeeded);
        Assert.Equal("REKALL_WEBGPU_READBACK_FAILED", Assert.Single(result.Diagnostics).Code);
    }

    private static async Task<WebGpuProofEvidence> ExecuteWithReadback(WebGpuProofReadbackResult readback)
    {
        var bridge = new LifecycleBridge();
        using var device = new RekallAgeWebGpuRenderingDevice(bridge, RekallAgeRenderingDeviceCapabilities.DesktopBaseline("WebGPU"));
        var output = device.ImportCanvasOutput(640, 480, RekallAgeTextureFormat.Bgra8Unorm);
        return await WebGpuProofExecution.ExecuteAsync(
            device,
            output.Handle,
            RekallAgeTextureFormat.Bgra8Unorm,
            _ => ValueTask.FromResult(readback));
    }

    private static WebGpuPixelProof PixelProof(bool passed) => new(
        passed,
        640,
        480,
        2560,
        new(
            new(51, 38, 4, 6, 10, 255),
            new(176, 361, 55, 190, 231, 255),
            new(320, 151, 53, 81, 247, 255),
            new(464, 361, 193, 56, 222, 255)));

    private sealed class LifecycleBridge : IRekallAgeWebGpuBridge
    {
        public bool Flushed { get; private set; }
        public RekallAgeWebGpuBridgeResult Execute(string packetJson) => RekallAgeWebGpuBridgeResult.Success;
        public ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            Flushed = true;
            return ValueTask.FromResult(RekallAgeWebGpuBridgeResult.Success);
        }
    }
}
