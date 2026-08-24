using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering.WebGpu;

public interface IRekallAgeWebGpuBridge
{
    RekallAgeWebGpuBridgeResult Execute(string packetJson);

    ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default);

    // Lighter-weight than FlushAsync: drains only the queued validation error-scope/shader-compilation work
    // (bounding the same pendingScopes/pendingCompilations queues) without also awaiting GPU completion. Intended
    // for a real per-tick drain of an ordinarily-running game loop, where blocking on GPU completion every frame
    // would serialize CPU and GPU for no correctness benefit.
    ValueTask<RekallAgeWebGpuBridgeResult> DrainAsync(CancellationToken cancellationToken = default);
}

public sealed record RekallAgeWebGpuBridgeResult(bool Succeeded, IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics)
{
    public static RekallAgeWebGpuBridgeResult Success { get; } = new(true, []);
}
