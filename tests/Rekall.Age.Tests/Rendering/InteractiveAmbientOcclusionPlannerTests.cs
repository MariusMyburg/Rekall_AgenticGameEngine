using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class InteractiveAmbientOcclusionPlannerTests
{
    [Fact]
    public void HighQualityEnablesBoundedInteractiveContactOcclusion()
    {
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High"),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("vulkan"),
            2560,
            1440);

        var result = new RekallAgeInteractiveAmbientOcclusionPlanner().Plan(quality);

        Assert.True(result.Enabled);
        Assert.InRange(result.SampleCount, 4, 12);
        Assert.InRange(result.Strength, 0.1f, 0.6f);
    }

    [Fact]
    public void PerformanceQualityKeepsInteractiveContactOcclusionDisabled()
    {
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("Performance"),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("vulkan"),
            1280,
            720);

        Assert.False(new RekallAgeInteractiveAmbientOcclusionPlanner().Plan(quality).Enabled);
    }

    [Fact]
    public void DiagnosticExecutionOverrideDisablesAuthoredAmbientOcclusion()
    {
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High"),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("vulkan"),
            1280,
            720);

        Assert.False(new RekallAgeInteractiveAmbientOcclusionPlanner()
            .Plan(quality, executionEnabled: false).Enabled);
    }
}
