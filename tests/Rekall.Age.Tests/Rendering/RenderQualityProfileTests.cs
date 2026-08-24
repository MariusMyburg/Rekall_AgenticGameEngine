using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderQualityProfileTests
{
    public static TheoryData<string, double, int, int, string, int> Presets => new()
    {
        { "Performance", 0.50, 1, 512, "analytic", 2_000 },
        { "Low", 0.67, 1, 1024, "analytic", 8_000 },
        { "Medium", 0.75, 2, 1024, "froxel-low", 24_000 },
        { "High", 1.00, 3, 2048, "froxel", 64_000 },
        { "Ultra", 1.00, 4, 2048, "froxel-high", 128_000 },
        { "Epic", 1.25, 4, 4096, "froxel-epic", 250_000 }
    };

    [Theory]
    [MemberData(nameof(Presets))]
    public void ResolverProducesStablePresetDefaults(
        string preset, double scale, int cascades, int shadowResolution, string fogMode, int particles)
    {
        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent(preset),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
            2560,
            1440);

        Assert.Equal(scale, plan.ResolutionScale, 2);
        Assert.Equal(cascades, plan.Shadows.CascadeCount);
        Assert.Equal(shadowResolution, plan.Shadows.Resolution);
        Assert.Equal(fogMode, plan.Fog.Mode);
        Assert.Equal(particles, plan.Particles.MaximumActiveParticles);
    }

    [Fact]
    public void ResolverUsesFiniteSupportedAuthoredOverrides()
    {
        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent(
                "High",
                ResolutionScale: 0.9,
                ShadowCascadeCount: 4,
                ShadowResolution: 1024,
                FogMode: "froxel-high",
                Bloom: false,
                Ssao: false,
                MaximumActiveParticles: 12_345),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
            2560,
            1440);

        Assert.Equal(0.9, plan.ResolutionScale, 3);
        Assert.Equal(4, plan.Shadows.CascadeCount);
        Assert.Equal(1024, plan.Shadows.Resolution);
        Assert.Equal("froxel-high", plan.Fog.Mode);
        Assert.False(plan.Post.Bloom);
        Assert.False(plan.Post.Ssao);
        Assert.Equal(12_345, plan.Particles.MaximumActiveParticles);
        Assert.Empty(plan.Degradations);
    }

    [Fact]
    public void ResolverReportsInvalidOverridesUnknownPresetsAndUnsupportedTimestamps()
    {
        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent(
                "not-a-preset",
                ResolutionScale: double.NaN,
                ShadowResolution: -1)
            {
                EnableGpuTimestamps = true
            },
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test") with
            {
                MaximumTextureDimension2D = 1024,
                SupportsTimestampQueries = false
            },
            2560,
            1440);

        Assert.Equal("High", plan.ResolvedPreset);
        Assert.Equal(1, plan.ResolutionScale);
        Assert.Equal(1024, plan.Shadows.Resolution);
        Assert.False(plan.Post.GpuTimestamps);
        Assert.Contains(plan.Degradations, item => item.Code == "REKALL_RENDER_QUALITY_OVERRIDE_INVALID"
            && item.Feature == "resolutionScale" && item.RequestedValue == "NaN" && item.ResolvedValue == "1");
        Assert.Contains(plan.Degradations, item => item.Code == "REKALL_RENDER_FEATURE_DEVICE_CLAMPED"
            && item.Feature == "shadowResolution" && item.RequestedValue == "2048" && item.ResolvedValue == "1024");
        Assert.Contains(plan.Degradations, item => item.Code == "REKALL_RENDER_FEATURE_DEVICE_CLAMPED"
            && item.Feature == "gpuTimestamps" && item.RequestedValue == "true" && item.ResolvedValue == "false");
    }
}
