using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;
using System.Text.Json;

internal sealed class WebGpuBrowserBridge : IRekallAgeWebGpuBridge
{
    public RekallAgeRenderingDeviceCapabilities? Capabilities { get; private set; }
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
        try
        {
            var json = await BrowserHost.InitializeWebGpuAsync("#viewport").WaitAsync(cancellationToken);
            var result = RekallAgeWebGpuProtocol.DeserializeBridgeResult(json);
            Capabilities = result.Succeeded ? ParseCapabilities(json) : null;
            return Capabilities is null && result.Succeeded
                ? Failed("REKALL_WEBGPU_CAPABILITIES_INVALID", "The browser WebGPU bridge did not report valid device capabilities.")
                : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return Failed("REKALL_WEBGPU_BRIDGE_INITIALIZE_FAILED", "The browser WebGPU bridge could not initialize.", exception.GetType().Name); }
    }

    private static RekallAgeWebGpuBridgeResult Failed(string code, string message, string? target = null) =>
        new(false, [new(code, message, target)]);

    private static RekallAgeRenderingDeviceCapabilities? ParseCapabilities(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var capabilities = document.RootElement.GetProperty("capabilities");
            var limits = capabilities.GetProperty("limits");
            int Integer(string name, int fallback) => limits.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;
            ulong Unsigned(string name, ulong fallback) => limits.TryGetProperty(name, out var value) && value.TryGetUInt64(out var parsed) ? parsed : fallback;
            var features = capabilities.TryGetProperty("features", out var values) && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal)
                : [];
            return new("WebGPU", Unsigned("maxBufferSize", 0), Integer("maxTextureDimension1D", 0), Integer("maxTextureDimension2D", 0), Integer("maxTextureDimension3D", 0), Integer("maxTextureArrayLayers", 0), Integer("maxColorAttachments", 1), Integer("maxBindingsPerBindGroup", 0), Integer("maxSamplerAnisotropy", 1), 1024 * 1024, true, true, true, true, features.Contains("timestamp-query"))
            { MaximumVertexBuffers = Integer("maxVertexBuffers", 0), MaximumVertexAttributes = Integer("maxVertexAttributes", 0), MaximumVertexBufferStrideBytes = Integer("maxVertexBufferArrayStride", 0), MaximumComputeWorkgroupsPerDimension = (uint)Integer("maxComputeWorkgroupsPerDimension", 0), SupportsIndirectDispatch = true };
        }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }
}
