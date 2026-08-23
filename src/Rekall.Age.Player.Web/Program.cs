using System.Runtime.InteropServices.JavaScript;
using Rekall.Age.Player.Web;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;
using Rekall.Age.Workflows.Web;

var webGpu = BrowserHost.HasWebGpu();
var profile = webGpu ? "WebGPU" : "WebGL 2 compatibility required";
var publishedModules = RekallAgePublishedModules.Registrations;
using var contentClient = new HttpClient { BaseAddress = new Uri(BrowserHost.BaseUri()) };
var contentFetcher = new RekallAgeHttpWebContentFetcher(contentClient);
using var gameBootstrap = await new RekallAgeWebGameBootstrapper().BootAsync(
    contentFetcher.FetchAsync,
    publishedModules,
    CancellationToken.None);
BrowserHost.PublishGameBootstrapEvidence(WebGameBootstrapEvidenceJson.Serialize(gameBootstrap.Evidence));
BrowserHost.SetText(
    "#runtime",
    gameBootstrap.Evidence.Succeeded
        ? $".NET {Environment.Version} / browser-wasm / world frame {gameBootstrap.Evidence.RuntimeFrameIndex} / entities {gameBootstrap.Evidence.Entities.Count} / static modules {publishedModules.Count}"
        : $".NET {Environment.Version} / browser-wasm / {gameBootstrap.Evidence.Diagnostics.FirstOrDefault()?.Code ?? "BOOTSTRAP FAILED"}");
BrowserHost.SetText("#graphics", profile);

if (!webGpu)
{
    Publish(new("WebGPU", RekallAgeWebGpuProtocol.Version, WebGpuProofWorkload.WorkloadId, 0,
        [new("REKALL_WEBGPU_UNAVAILABLE", "WebGPU is not available in this browser.")], null));
    BrowserHost.SetText("#state", "COMPATIBILITY PATH REQUIRED");
    BrowserHost.SetReady(false);
}
else
{
    var bridge = new WebGpuBrowserBridge();
    var initialization = await bridge.InitializeAsync();
    if (!initialization.Succeeded)
    {
        Publish(new("WebGPU", RekallAgeWebGpuProtocol.Version, WebGpuProofWorkload.WorkloadId, 0, initialization.Diagnostics, null));
        BrowserHost.SetText("#state", initialization.Diagnostics.FirstOrDefault()?.Code ?? "WEBGPU INITIALIZATION FAILED");
        BrowserHost.SetReady(false);
    }
    else if (bridge.Initialization is { Succeeded: true, Capabilities: { } capabilities, PreferredCanvasFormat: { } canvasFormat })
    {
        using var device = new Rekall.Age.Rendering.WebGpu.RekallAgeWebGpuRenderingDevice(bridge, capabilities);
        var output = device.ImportCanvasOutput(BrowserHost.CanvasWidth(), BrowserHost.CanvasHeight(), canvasFormat);
        if (!output.Created)
        {
            Publish(new("WebGPU", RekallAgeWebGpuProtocol.Version, WebGpuProofWorkload.WorkloadId, 0, output.Diagnostics, null));
            BrowserHost.SetText("#state", output.Diagnostics.FirstOrDefault()?.Code ?? "REKALL_WEBGPU_CANVAS_IMPORT_FAILED");
            BrowserHost.SetReady(false);
        }
        else
        {
            var evidence = await WebGpuProofExecution.ExecuteAsync(device, output.Handle, canvasFormat, bridge.ReadPixelsAsync);
            Publish(evidence);
            if (evidence.SubmittedFrames > 0 && evidence.Diagnostics.Count == 0 && evidence.PixelProof is { Passed: true })
            {
                BrowserHost.SetText("#state", "GPU WORKLOAD EXECUTED");
                BrowserHost.SetReady(true);
            }
            else
            {
                BrowserHost.SetText("#state", evidence.Diagnostics.FirstOrDefault()?.Code ?? "REKALL_WEBGPU_PIXEL_PROOF_FAILED");
                BrowserHost.SetReady(false);
            }
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
    }
    else
    {
        var diagnostic = new RekallAgeGraphicsDiagnostic("REKALL_WEBGPU_CAPABILITIES_INVALID", "The browser returned incomplete WebGPU capabilities.");
        Publish(new("WebGPU", RekallAgeWebGpuProtocol.Version, WebGpuProofWorkload.WorkloadId, 0, [diagnostic], null));
        BrowserHost.SetText("#state", diagnostic.Code);
        BrowserHost.SetReady(false);
    }
}

await Task.Delay(Timeout.InfiniteTimeSpan);

static void Publish(WebGpuProofEvidence evidence) =>
    BrowserHost.PublishWebGpuEvidence(WebGpuProofEvidenceJson.Serialize(evidence));

internal static partial class BrowserHost
{
    [JSImport("web.hasWebGpu", "main.js")]
    internal static partial bool HasWebGpu();

    [JSImport("webgpu.initialize", "main.js")]
    internal static partial Task<string> InitializeWebGpuAsync(string canvasSelector);

    [JSImport("webgpu.execute", "main.js")]
    internal static partial string ExecuteWebGpu(string packetJson);

    [JSImport("webgpu.flush", "main.js")]
    internal static partial Task<string> FlushWebGpuAsync();

    [JSImport("webgpu.readPixels", "main.js")]
    internal static partial Task<string> ReadWebGpuPixelsAsync();

    [JSImport("webgpu.canvasWidth", "main.js")]
    internal static partial int CanvasWidth();

    [JSImport("webgpu.canvasHeight", "main.js")]
    internal static partial int CanvasHeight();

    [JSImport("dom.setText", "main.js")]
    internal static partial void SetText(string selector, string value);

    [JSImport("dom.setReady", "main.js")]
    internal static partial void SetReady(bool ready);

    [JSImport("dom.publishEvidence", "main.js")]
    internal static partial void PublishWebGpuEvidence(string json);

    [JSImport("dom.baseUri", "main.js")]
    internal static partial string BaseUri();

    [JSImport("dom.publishGameBootstrapEvidence", "main.js")]
    internal static partial void PublishGameBootstrapEvidence(string json);
}
