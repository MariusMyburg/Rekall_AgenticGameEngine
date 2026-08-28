using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderQualityProfileTests
{
    [Theory]
    [InlineData("Performance", 1)]
    [InlineData("Low", 1)]
    [InlineData("Medium", 2)]
    [InlineData("High", 8)]
    [InlineData("Ultra", 16)]
    [InlineData("Epic", 16)]
    public void ResolverScalesTextureAnisotropyByPresetAndDeviceLimit(string preset, int requested)
    {
        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent(preset),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test") with { MaximumSamplerAnisotropy = 4 },
            1920,
            1080);

        Assert.Equal(Math.Min(requested, 4), plan.Textures.MaximumAnisotropy);
        Assert.Equal(requested > 4, plan.Degradations.Any(item =>
            item.Feature == "textureAnisotropy"
            && item.RequestedValue == requested.ToString()
            && item.ResolvedValue == "4"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ParticleCapacityDegradesToZeroWithoutRequiredComputeAndStorageBufferCapabilities(
        bool supportsCompute,
        bool supportsStorageBuffers)
    {
        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High", MaximumActiveParticles: 64_000),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("particle-limited") with
            {
                SupportsCompute = supportsCompute,
                SupportsStorageBuffers = supportsStorageBuffers
            },
            1920,
            1080);

        Assert.Equal(0, plan.Particles.MaximumActiveParticles);
        Assert.Contains(plan.Degradations, item => item is
        {
            Code: "REKALL_RENDER_FEATURE_DEVICE_CLAMPED",
            Feature: "maximumActiveParticles",
            RequestedValue: "64000",
            ResolvedValue: "0"
        });
    }

    [Fact]
    public void FroxelGridIsProportionallyClampedToDevice3DAndComputeLimitsWithStableDegradation()
    {
        var capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline("limited") with
        {
            MaximumTextureDimension3D = 64,
            MaximumComputeWorkgroupsPerDimension = 12
        };

        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("Epic"),
            capabilities,
            2560,
            1440);

        Assert.Equal(new RekallAgeResolvedFogQuality("froxel-epic", 48, 27, 14), plan.Fog);
        Assert.Contains(plan.Degradations, item => item is
        {
            Code: "REKALL_RENDER_FEATURE_DEVICE_CLAMPED",
            Feature: "fogGrid",
            RequestedValue: "320x180x96",
            ResolvedValue: "48x27x14"
        });
    }

    public static TheoryData<string, double, int, int, string, int, int> Presets => new()
    {
        { "Performance", 0.50, 1, 512, "analytic", 2_000, 2 },
        { "Low", 0.67, 1, 1024, "analytic", 8_000, 4 },
        { "Medium", 0.75, 2, 1024, "froxel-low", 24_000, 8 },
        { "High", 1.00, 3, 2048, "froxel", 64_000, 16 },
        { "Ultra", 1.00, 4, 2048, "froxel-high", 128_000, 16 },
        { "Epic", 1.25, 4, 4096, "froxel-epic", 250_000, 16 }
    };

    [Theory]
    [MemberData(nameof(Presets))]
    public void ResolverProducesStablePresetDefaults(
        string preset, double scale, int cascades, int shadowResolution, string fogMode, int particles, int pointLights)
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
        Assert.Equal(pointLights, plan.Lighting.MaximumPointLights);
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
        Assert.Equal(0.4, plan.ResolutionScale, 3);
        Assert.Equal(1024, plan.Shadows.Resolution);
        Assert.False(plan.Post.GpuTimestamps);
        Assert.Contains(plan.Degradations, item => item.Code == "REKALL_RENDER_QUALITY_OVERRIDE_INVALID"
            && item.Feature == "resolutionScale" && item.RequestedValue == "NaN" && item.ResolvedValue == "1");
        Assert.Contains(plan.Degradations, item => item.Code == "REKALL_RENDER_FEATURE_DEVICE_CLAMPED"
            && item.Feature == "shadowResolution" && item.RequestedValue == "2048" && item.ResolvedValue == "1024");
        Assert.Contains(plan.Degradations, item => item.Code == "REKALL_RENDER_FEATURE_DEVICE_CLAMPED"
            && item.Feature == "gpuTimestamps" && item.RequestedValue == "true" && item.ResolvedValue == "false");
    }

    [Fact]
    public void ResolverClampsRenderResolutionToTheDeviceTextureLimit()
    {
        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High"),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test") with { MaximumTextureDimension2D = 2048 },
            2560,
            1440);

        Assert.Equal(2048, plan.RenderWidth);
        Assert.Equal(1152, plan.RenderHeight);
        Assert.Equal(0.8, plan.ResolutionScale, 3);
        Assert.Contains(plan.Degradations, item => item.Code == "REKALL_RENDER_FEATURE_DEVICE_CLAMPED"
            && item.Feature == "renderResolution" && item.RequestedValue == "2560x1440"
            && item.ResolvedValue == "2048x1152");
    }

    [Fact]
    public void ResolverReportsInvalidOutputDimensionsInsteadOfSilentlySubstitutingThem()
    {
        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High"),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
            0,
            -4);

        Assert.Equal(1, plan.OutputWidth);
        Assert.Equal(1, plan.OutputHeight);
        Assert.Contains(plan.Degradations, item => item.Code == "REKALL_RENDER_FEATURE_DEVICE_CLAMPED"
            && item.Feature == "outputResolution" && item.RequestedValue == "0x-4"
            && item.ResolvedValue == "1x1");
    }
}
