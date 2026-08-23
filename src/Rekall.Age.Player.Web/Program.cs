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

// Prove the browser input bridge round-trip (JS raw snapshot -> RekallAgeWebInputSnapshotJson -> the same
// RekallAgeRuntimeInputState the Windows player produces) as part of ordinary bootstrap, not as dead code.
var inputBridge = new RekallAgeWebInputBridge();
var inputSnapshot = RekallAgeWebInputSnapshotJson.Parse(BrowserHost.SnapshotInput());
var inputState = inputBridge.Capture(inputSnapshot);

BrowserHost.SetText(
    "#runtime",
    gameBootstrap.Evidence.Succeeded
        ? $".NET {Environment.Version} / browser-wasm / world frame {gameBootstrap.Evidence.RuntimeFrameIndex} / entities {gameBootstrap.Evidence.Entities.Count} / static modules {publishedModules.Count} / input {inputState.ViewportWidth:0}x{inputState.ViewportHeight:0}"
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
        else if (gameBootstrap.Session is { } session)
        {
            // A real published AGE project booted: run it, not the compatibility proof workload. Startup already
            // failed closed above for every earlier missing prerequisite (WebGPU, device init, canvas import).
            var player = new RekallAgeWebPlayer(session.World, session.ExecutionLoop, device);
            BrowserHost.SetText("#state", "GAME RUNNING");
            BrowserHost.SetReady(true);
            BrowserHost.StartFrameLoop();
            while (true)
            {
                var elapsedSeconds = await BrowserHost.AwaitNextFrameAsync();
                var tickInputSnapshot = RekallAgeWebInputSnapshotJson.Parse(BrowserHost.SnapshotInput());
                var tick = await player.TickAsync(elapsedSeconds, tickInputSnapshot, output.Handle, canvasFormat, CancellationToken.None);
                BrowserHost.SetText(
                    "#state",
                    tick.Rendered
                        ? $"GAME RUNNING / tick {tick.TickSequence} / frame {tick.FrameIndex} / draws {tick.DrawCount}"
                        : tick.Diagnostics.FirstOrDefault()?.Code ?? "REKALL_WEB_PLAYER_TICK_FAILED");
                BrowserHost.SetReady(tick.Rendered);
            }
        }
        else
        {
            // No published project manifest is present (the standalone proof page); keep the bounded WebGPU
            // triangle proof as the compatibility demonstration until a real project replaces it.
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

    [JSImport("input.snapshot", "main.js")]
    internal static partial string SnapshotInput();

    [JSImport("input.pullLifecycleEvents", "main.js")]
    internal static partial string PullInputLifecycleEvents();

    [JSImport("frame.start", "main.js")]
    internal static partial void StartFrameLoop();

    [JSImport("frame.stop", "main.js")]
    internal static partial void StopFrameLoop();

    [JSImport("frame.pause", "main.js")]
    internal static partial void PauseFrameLoop();

    [JSImport("frame.resume", "main.js")]
    internal static partial void ResumeFrameLoop();

    [JSImport("frame.awaitNext", "main.js")]
    internal static partial Task<double> AwaitNextFrameAsync();
}
