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
            var colorTarget = output.Handle;
            BrowserHost.SetText("#state", "GAME RUNNING");
            BrowserHost.SetReady(true);
            BrowserHost.StartFrameLoop();
            while (true)
            {
                double elapsedSeconds;
                try
                {
                    elapsedSeconds = await BrowserHost.AwaitNextFrameAsync();
                }
                catch (Exception ex)
                {
                    // requestAnimationFrame itself does not throw; a rejected promise here means the JS side (or
                    // the WebAssembly host underneath it) is gone. Fail closed instead of leaving the last #state
                    // text lying about the game still running.
                    BrowserHost.SetText("#state", $"REKALL_WEB_FRAME_LOOP_FAILED / {ex.Message}");
                    BrowserHost.SetReady(false);
                    BrowserHost.StopFrameLoop();
                    break;
                }

                // Consume queued resize facts before rendering: the canvas cannot be resized in place, so a new,
                // correctly-sized color target must exist before this tick's frame is built and drawn into it.
                // Without this, every tick after a browser resize renders the new viewport into the old-sized
                // target (silent scale/aspect corruption on real WebGPU, not an error).
                if (RekallAgeWebPlayerLifecycleEventsJson.TryGetLatestResize(BrowserHost.PullInputLifecycleEvents())
                    is { } resize && resize.Width > 0 && resize.Height > 0)
                {
                    var resized = device.ImportCanvasOutput(resize.Width, resize.Height, canvasFormat);
                    if (resized.Created)
                    {
                        device.Destroy(colorTarget);
                        colorTarget = resized.Handle;
                    }
                    // A failed resize import (e.g. a dimension over the device's texture limit) keeps the
                    // previous, still-valid color target instead of losing rendering entirely; the next resize
                    // (or the next frame at the same size) gets another chance.
                }

                var tickInputSnapshot = RekallAgeWebInputSnapshotJson.Parse(BrowserHost.SnapshotInput());
                RekallAgeWebPlayerTickResult tick;
                Rekall.Age.Rendering.Abstractions.RekallAgeGraphicsValidationResult flush;
                try
                {
                    tick = await player.TickAsync(elapsedSeconds, tickInputSnapshot, colorTarget, canvasFormat, CancellationToken.None);
                    // Every tick's frame build records at least one WebGPU packet (typically many: resource
                    // creation, buffer/texture writes, the render pass submission) against the JS bridge's bounded
                    // pendingScopes/pendingCompilations queues (see webgpu-device.js MAX_PENDING); nothing else in
                    // this loop ever drains them. Without an explicit drain every tick, those queues fill up over a
                    // handful of frames and every subsequent WebGPU operation starts failing closed with
                    // REKALL_WEBGPU_PENDING_OVERFLOW -- observed directly from a real browser tick on a
                    // resource-heavy scene, not caught by any build or unit test (the in-memory test device has no
                    // such queue). DrainAsync (not FlushAsync) is used here deliberately: it still detects and
                    // reports the same validation errors and still bounds the same queues, but does not also block
                    // on device.queue.onSubmittedWorkDone() the way FlushAsync does, which would otherwise
                    // serialize CPU and GPU every single tick of an ordinarily-running game for no correctness gain.
                    flush = await device.DrainAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // Never let an unhandled exception (a device-lost fault, an out-of-bounds asset reference, a
                    // simulation bug) escape into an unattended page: fail closed, report why, and stop cleanly
                    // instead of the browser tab silently freezing on the last-rendered frame.
                    BrowserHost.SetText("#state", $"REKALL_WEB_PLAYER_TICK_EXCEPTION / {ex.Message}");
                    BrowserHost.SetReady(false);
                    BrowserHost.StopFrameLoop();
                    break;
                }

                var rendered = tick.Rendered && flush.Valid;
                string stateText;
                if (rendered)
                {
                    stateText = $"GAME RUNNING / tick {tick.TickSequence} / frame {tick.FrameIndex} / draws {tick.DrawCount}";
                }
                else
                {
                    // The Code alone (e.g. REKALL_WEBGPU_DEVICE_FAULTED) does not say why -- the actual native
                    // WebGPU validation message is carried in Diagnostic.Message. Surfacing both is the difference
                    // between an unattended page someone can debug from the live #state text and one that just
                    // says something failed.
                    var diagnostic = tick.Diagnostics.FirstOrDefault() ?? flush.Diagnostics.FirstOrDefault();
                    stateText = diagnostic is null
                        ? "REKALL_WEB_PLAYER_TICK_FAILED"
                        : $"{diagnostic.Code} / {diagnostic.Message}";
                }
                BrowserHost.SetText("#state", stateText);
                BrowserHost.SetReady(rendered);
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

    [JSImport("webgpu.drain", "main.js")]
    internal static partial Task<string> DrainWebGpuAsync();

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
