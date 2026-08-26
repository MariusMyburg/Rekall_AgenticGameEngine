using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Attaches resolved generic quality intent to an interactive viewport frame so
/// realtime backends consume the same feature plan as deterministic capture.
/// </summary>
public sealed class RekallAgeInteractiveQualityFrameResolver
{
    public RekallAgeRuntimeViewportFrame Resolve(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRenderQualityIntent? authoredIntent,
        RekallAgeRenderingDeviceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(capabilities);
        var resolved = new RekallAgeRenderQualityProfileResolver().Resolve(
            authoredIntent ?? new RekallAgeRenderQualityIntent(),
            capabilities,
            frame.Width,
            frame.Height);
        return frame with { ResolvedQualityPlan = resolved };
    }
}
