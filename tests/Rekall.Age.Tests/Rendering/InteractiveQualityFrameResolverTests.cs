using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class InteractiveQualityFrameResolverTests
{
    [Fact]
    public void AuthoredHighIntentReachesInteractiveViewportFeaturePlan()
    {
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main", 0, 0, 1600, 900, null, [], [], 0,
            new RekallAgeRuntimeViewportOverlay(false, 0), []);
        var result = new RekallAgeInteractiveQualityFrameResolver().Resolve(
            frame,
            new RekallAgeRenderQualityIntent(Preset: "High"),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("vulkan"));

        Assert.NotNull(result.ResolvedQualityPlan);
        Assert.Equal("High", result.ResolvedQualityPlan!.ResolvedPreset);
        Assert.Equal(3, result.ResolvedQualityPlan.Shadows.CascadeCount);
        Assert.Equal(2048, result.ResolvedQualityPlan.Shadows.Resolution);
    }
}
