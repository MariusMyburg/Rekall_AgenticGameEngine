using System.Numerics;

namespace Rekall.Age.Rendering;

/// <summary>
/// Projects the backend-neutral directional shadow plan into the fixed four-cascade
/// contract consumed by interactive graphics backends.
/// </summary>
public sealed class RekallAgeInteractiveShadowFramePlanner
{
    public const int MaximumCascadeCount = 4;

    public RekallAgeInteractiveShadowFrame Plan(RekallAgeVulkanShadowPlan? shadowPlan)
    {
        if (shadowPlan is not { Enabled: true } || shadowPlan.Cascades.Count == 0)
        {
            return RekallAgeInteractiveShadowFrame.Disabled;
        }

        var cascades = shadowPlan.Cascades
            .OrderBy(cascade => cascade.Index)
            .Take(MaximumCascadeCount)
            .ToArray();
        var matrices = Enumerable.Repeat(Matrix4x4.Identity, MaximumCascadeCount).ToArray();
        var splits = new float[MaximumCascadeCount];
        for (var index = 0; index < cascades.Length; index++)
        {
            matrices[index] = cascades[index].ViewProjection;
            splits[index] = cascades[index].SplitFar;
        }

        for (var index = cascades.Length; index < splits.Length; index++)
        {
            splits[index] = splits[cascades.Length - 1];
        }

        return new RekallAgeInteractiveShadowFrame(
            true,
            Math.Max(1, shadowPlan.Resolution),
            cascades.Length,
            matrices,
            new Vector4(splits[0], splits[1], splits[2], splits[3]),
            Math.Max(0, shadowPlan.DepthBias),
            Math.Max(0, shadowPlan.NormalBias),
            Math.Max(1, shadowPlan.FilterTapCount));
    }
}

public sealed record RekallAgeInteractiveShadowFrame(
    bool Enabled,
    int Resolution,
    int CascadeCount,
    IReadOnlyList<Matrix4x4> ViewProjections,
    Vector4 SplitDepths,
    float DepthBias,
    float NormalBias,
    int FilterTapCount)
{
    public static RekallAgeInteractiveShadowFrame Disabled { get; } = new(
        false,
        1,
        0,
        Enumerable.Repeat(Matrix4x4.Identity, RekallAgeInteractiveShadowFramePlanner.MaximumCascadeCount).ToArray(),
        Vector4.Zero,
        0,
        0,
        1);
}
