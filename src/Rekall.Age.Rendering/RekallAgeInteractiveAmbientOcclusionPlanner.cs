using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Resolves the bounded screen-space contact-occlusion work used by interactive
/// backends from the same generic post-quality plan as native capture.
/// </summary>
public sealed class RekallAgeInteractiveAmbientOcclusionPlanner
{
    public RekallAgeInteractiveAmbientOcclusionPlan Plan(
        RekallAgeResolvedRenderFeaturePlan? quality,
        bool executionEnabled = true)
    {
        if (!executionEnabled || quality?.Post.Ssao != true)
        {
            return RekallAgeInteractiveAmbientOcclusionPlan.Disabled;
        }

        var epic = quality.ResolvedPreset.Equals("Epic", StringComparison.OrdinalIgnoreCase)
            || quality.ResolvedPreset.Equals("Ultra", StringComparison.OrdinalIgnoreCase);
        return new RekallAgeInteractiveAmbientOcclusionPlan(
            true,
            epic ? 12 : 8,
            epic ? 7f : 5f,
            epic ? 0.48f : 0.38f,
            0.035f);
    }
}

public sealed record RekallAgeInteractiveAmbientOcclusionPlan(
    bool Enabled,
    int SampleCount,
    float RadiusPixels,
    float Strength,
    float Bias)
{
    public static RekallAgeInteractiveAmbientOcclusionPlan Disabled { get; } = new(false, 0, 0, 0, 0);
}
