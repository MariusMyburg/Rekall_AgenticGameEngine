using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering.WebGpu;

public interface IRekallAgeWebGpuBridge
{
    RekallAgeWebGpuBridgeResult Execute(string packetJson);

    ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default);
}

public sealed record RekallAgeWebGpuBridgeResult(bool Succeeded, IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics)
{
    public static RekallAgeWebGpuBridgeResult Success { get; } = new(true, []);
}
