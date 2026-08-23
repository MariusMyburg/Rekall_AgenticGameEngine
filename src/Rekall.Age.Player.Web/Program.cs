using System.Runtime.InteropServices.JavaScript;
using Rekall.Age.Rendering.Abstractions;

var webGpu = BrowserHost.HasWebGpu();
var profile = webGpu ? "WebGPU" : "WebGL 2 compatibility required";
BrowserHost.SetText("#runtime", $".NET {Environment.Version} / browser-wasm");
BrowserHost.SetText("#graphics", profile);

if (!webGpu)
{
    BrowserHost.SetText("#state", "COMPATIBILITY PATH REQUIRED");
    BrowserHost.SetReady(false);
}
else
{
    var bridge = new WebGpuBrowserBridge();
    var initialization = await bridge.InitializeAsync();
    if (!initialization.Succeeded)
    {
        BrowserHost.SetText("#state", initialization.Diagnostics.FirstOrDefault()?.Code ?? "WEBGPU INITIALIZATION FAILED");
        BrowserHost.SetReady(false);
    }
    else if (bridge.Initialization is { Succeeded: true, Capabilities: { } capabilities, PreferredCanvasFormat: { } canvasFormat })
    {
        using var device = new Rekall.Age.Rendering.WebGpu.RekallAgeWebGpuRenderingDevice(bridge, capabilities);
        var output = device.ImportCanvasOutput(BrowserHost.CanvasWidth(), BrowserHost.CanvasHeight(), canvasFormat);
        var flush = output.Created ? await device.FlushAsync() : new RekallAgeGraphicsValidationResult(output.Diagnostics);
        if (flush.Valid)
        {
            BrowserHost.SetText("#state", "WEBGPU DEVICE READY");
            BrowserHost.SetReady(true);
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        else
        {
            BrowserHost.SetText("#state", flush.Diagnostics.FirstOrDefault()?.Code ?? "REKALL_WEBGPU_CANVAS_IMPORT_FAILED");
            BrowserHost.SetReady(false);
        }
    }
    else { BrowserHost.SetText("#state", "REKALL_WEBGPU_CAPABILITIES_INVALID"); BrowserHost.SetReady(false); }
}

await Task.Delay(Timeout.InfiniteTimeSpan);

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

    [JSImport("webgpu.canvasWidth", "main.js")]
    internal static partial int CanvasWidth();

    [JSImport("webgpu.canvasHeight", "main.js")]
    internal static partial int CanvasHeight();

    [JSImport("dom.setText", "main.js")]
    internal static partial void SetText(string selector, string value);

    [JSImport("dom.setReady", "main.js")]
    internal static partial void SetReady(bool ready);
}
