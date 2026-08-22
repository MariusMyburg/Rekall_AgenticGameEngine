using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeGpuAssetDataResolution(
    byte[]? Data,
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics)
{
    public bool Resolved => Data is { Length: > 0 } && Diagnostics.Count == 0;
}

public interface IRekallAgeGpuAssetDataResolver
{
    RekallAgeGpuAssetDataResolution Resolve(string assetId);
}
