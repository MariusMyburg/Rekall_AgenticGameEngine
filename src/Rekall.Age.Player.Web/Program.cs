using System.Runtime.InteropServices.JavaScript;
using Rekall.Age.Rendering.Abstractions;

var webGpu = BrowserHost.HasWebGpu();
var profile = webGpu ? "WebGPU" : "WebGL 2 compatibility required";
var capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline(profile) with
{
    Backend = profile,
    SupportsCompute = webGpu,
    SupportsStorageBuffers = webGpu,
    SupportsStorageTextures = webGpu,
    SupportsIndirectDrawing = webGpu,
    SupportsTimestampQueries = false
};

BrowserHost.SetText("#runtime", $".NET {Environment.Version} / browser-wasm");
BrowserHost.SetText("#graphics", capabilities.Backend);
BrowserHost.SetText("#state", webGpu ? "DEVICE CONTRACT READY" : "COMPATIBILITY PATH REQUIRED");
BrowserHost.SetReady(webGpu);

await Task.Delay(Timeout.InfiniteTimeSpan);

internal static partial class BrowserHost
{
    [JSImport("web.hasWebGpu", "main.js")]
    internal static partial bool HasWebGpu();

    [JSImport("dom.setText", "main.js")]
    internal static partial void SetText(string selector, string value);

    [JSImport("dom.setReady", "main.js")]
    internal static partial void SetReady(bool ready);
}
