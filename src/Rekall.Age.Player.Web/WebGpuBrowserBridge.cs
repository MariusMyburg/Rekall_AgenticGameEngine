using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;

internal sealed class WebGpuBrowserBridge : IRekallAgeWebGpuBridge
{
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

    public async ValueTask<RekallAgeWebGpuBridgeResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try { return RekallAgeWebGpuProtocol.DeserializeBridgeResult(await BrowserHost.InitializeWebGpuAsync("#viewport").WaitAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return Failed("REKALL_WEBGPU_BRIDGE_INITIALIZE_FAILED", "The browser WebGPU bridge could not initialize.", exception.GetType().Name); }
    }

    private static RekallAgeWebGpuBridgeResult Failed(string code, string message, string? target = null) =>
        new(false, [new(code, message, target)]);
}
