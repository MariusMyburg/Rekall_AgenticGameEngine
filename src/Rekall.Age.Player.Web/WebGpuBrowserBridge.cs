using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;

internal sealed class WebGpuBrowserBridge : IRekallAgeWebGpuBridge
{
    public RekallAgeWebGpuInitializationResult? Initialization { get; private set; }
    public RekallAgeRenderingDeviceCapabilities? Capabilities => Initialization?.Capabilities;
    public RekallAgeWebGpuBridgeResult Execute(string packetJson)
    {
        try { return RekallAgeWebGpuProtocol.DeserializeBridgeResult(BrowserHost.ExecuteWebGpu(packetJson)); }
        catch (Exception exception) { return Failed("REKALL_WEBGPU_BRIDGE_EXECUTION_FAILED", "The browser WebGPU bridge could not execute an AGE packet.", exception.GetType().Name); }
    }

    public async ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        try { return RekallAgeWebGpuProtocol.DeserializeBridgeResult(await BrowserHost.FlushWebGpuAsync().WaitAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return Failed("REKALL_WEBGPU_BRIDGE_FLUSH_FAILED", "The browser WebGPU bridge could not flush device work.", exception.GetType().Name); }
    }

    public async ValueTask<RekallAgeWebGpuBridgeResult> DrainAsync(CancellationToken cancellationToken = default)
    {
        try { return RekallAgeWebGpuProtocol.DeserializeBridgeResult(await BrowserHost.DrainWebGpuAsync().WaitAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return Failed("REKALL_WEBGPU_BRIDGE_DRAIN_FAILED", "The browser WebGPU bridge could not drain pending validation work.", exception.GetType().Name); }
    }

    public async ValueTask<RekallAgeWebGpuBridgeResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await BrowserHost.InitializeWebGpuAsync("#viewport").WaitAsync(cancellationToken);
            Initialization = RekallAgeWebGpuProtocol.DeserializeInitializationResult(json);
            return new(Initialization.Succeeded, Initialization.Diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return Failed("REKALL_WEBGPU_BRIDGE_INITIALIZE_FAILED", "The browser WebGPU bridge could not initialize.", exception.GetType().Name); }
    }

    public async ValueTask<Rekall.Age.Player.Web.WebGpuProofReadbackResult> ReadPixelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await BrowserHost.ReadWebGpuPixelsAsync().WaitAsync(cancellationToken);
            return Rekall.Age.Player.Web.WebGpuProofEvidenceJson.DeserializeReadback(json);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Rekall.Age.Player.Web.WebGpuProofEvidenceJson.DeserializeReadback(null); }
    }

    private static RekallAgeWebGpuBridgeResult Failed(string code, string message, string? target = null) =>
        new(false, [new(code, message, target)]);

}
