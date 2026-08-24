using System.Numerics;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanShadowCascadePlannerTests
{
    [Fact]
    public void PracticalSplitsAreDeterministicIncreasingAndEndAtShadowDistance()
    {
        var plan = new RekallAgeVulkanShadowCascadePlanner().Plan(
            Camera(),
            Light(maximumDistance: 80),
            Casters(),
            new RekallAgeResolvedShadowQuality(4, 2048, 16));

        Assert.True(plan.Enabled, string.Join(Environment.NewLine, plan.Diagnostics.Select(item => item.Message)));
        Assert.Equal(4, plan.Cascades.Count);
        Assert.Equal(7.3719f, plan.Cascades[0].SplitFar, 3);
        Assert.Equal(15.8559f, plan.Cascades[1].SplitFar, 3);
        Assert.Equal(30.7863f, plan.Cascades[2].SplitFar, 3);
        Assert.Equal(80f, plan.Cascades[3].SplitFar, 4);
        Assert.All(plan.Cascades, cascade => Assert.True(IsFinite(cascade.ViewProjection)));
    }

    [Theory]
    [InlineData(1, 512, 2)]
    [InlineData(2, 1024, 8)]
    [InlineData(3, 2048, 12)]
    [InlineData(4, 4096, 24)]
    public void ResolvedQualityControlsCascadeCountArrayViewportAndFiltering(
        int cascadeCount,
        int resolution,
        int filterTaps)
    {
        var plan = new RekallAgeVulkanShadowCascadePlanner().Plan(
            Camera(),
            Light(),
            Casters(),
            new RekallAgeResolvedShadowQuality(cascadeCount, resolution, filterTaps));

        Assert.Equal(cascadeCount, plan.Cascades.Count);
        Assert.Equal(filterTaps, plan.FilterTapCount);
        Assert.All(plan.Cascades, cascade =>
        {
            Assert.Equal(new RekallAgeVulkanShadowAtlasViewport(0, 0, resolution, resolution, cascade.Index), cascade.AtlasViewport);
            Assert.Equal((long)resolution * resolution * 4, cascade.AtlasBytes);
        });
    }

    [Fact]
    public void CasterIntentAndLayerMasksSelectOnlyEligibleCasters()
    {
        var casters = new[]
        {
            Caster("world", new Vector3(0, 0, 8), layerMask: 0b0001),
            Caster("effects", new Vector3(0, 0, 9), layerMask: 0b0010),
            Caster("disabled", new Vector3(0, 0, 10), layerMask: 0b0001, castShadows: false)
        };

        var plan = new RekallAgeVulkanShadowCascadePlanner().Plan(
            Camera(receiverMask: 0b0001),
            Light(casterMask: 0b0001, receiverMask: 0b0001),
            casters,
            new RekallAgeResolvedShadowQuality(2, 1024, 8));

        Assert.Contains(plan.Cascades, cascade => cascade.CasterIds.SequenceEqual(["world"]));
        Assert.All(plan.Cascades, cascade =>
        {
            Assert.DoesNotContain("effects", cascade.CasterIds);
            Assert.DoesNotContain("disabled", cascade.CasterIds);
        });
        Assert.Equal(1, plan.SelectedCasterCount);
        Assert.Equal(2, plan.CulledCasterCount);
    }

    [Fact]
    public void CascadeBoundsCullCastersOutsideTheFittedLightFrustum()
    {
        var plan = new RekallAgeVulkanShadowCascadePlanner().Plan(
            Camera(),
            Light(),
            [
                Caster("inside", new Vector3(0, 0, 8)),
                Caster("outside", new Vector3(500, 0, 8))
            ],
            new RekallAgeResolvedShadowQuality(1, 1024, 4));

        var cascade = Assert.Single(plan.Cascades);
        Assert.Equal(["inside"], cascade.CasterIds);
        Assert.Equal(1, cascade.CulledCasterCount);
        Assert.Equal(1, plan.SelectedCasterCount);
        Assert.Equal(1, plan.CulledCasterCount);
    }

    [Fact]
    public void TexelStabilizationKeepsSubTexelCameraMotionBitStable()
    {
        var planner = new RekallAgeVulkanShadowCascadePlanner();
        var camera = Camera();
        var quality = new RekallAgeResolvedShadowQuality(3, 2048, 12);

        var first = planner.Plan(camera, Light(), Casters(), quality);
        var moved = planner.Plan(camera with { Position = camera.Position + new Vector3(0.0001f, 0, 0) }, Light(), Casters(), quality);

        Assert.Equal(first.Cascades[0].ViewProjection, moved.Cascades[0].ViewProjection);
    }

    [Fact]
    public void NonFiniteCameraPoseDisablesShadowsWithStableDiagnostic()
    {
        var plan = new RekallAgeVulkanShadowCascadePlanner().Plan(
            Camera() with { Position = new Vector3(float.NaN, 0, 0) },
            Light(),
            Casters(),
            new RekallAgeResolvedShadowQuality(3, 2048, 12));

        Assert.False(plan.Enabled);
        Assert.Empty(plan.Cascades);
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_SHADOW_CAMERA_INVALID");
    }

    [Fact]
    public void ParallelCameraBasisDisablesShadowsInsteadOfProducingNonFiniteMatrices()
    {
        var plan = new RekallAgeVulkanShadowCascadePlanner().Plan(
            Camera() with { Up = Vector3.UnitZ },
            Light(),
            Casters(),
            new RekallAgeResolvedShadowQuality(3, 2048, 12));

        Assert.False(plan.Enabled);
        Assert.Empty(plan.Cascades);
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_SHADOW_CAMERA_INVALID");
    }

    [Fact]
    public void OrthographicCameraUsesItsAuthoredSizeAndProducesFiniteCascades()
    {
        var plan = new RekallAgeVulkanShadowCascadePlanner().Plan(
            Camera() with
            {
                ProjectionMode = "orthographic",
                OrthographicSize = 18,
                VerticalFieldOfViewRadians = float.NaN
            },
            Light(),
            Casters(),
            new RekallAgeResolvedShadowQuality(2, 1024, 8));

        Assert.True(plan.Enabled, string.Join(Environment.NewLine, plan.Diagnostics.Select(item => item.Message)));
        Assert.Equal(2, plan.Cascades.Count);
        Assert.All(plan.Cascades, cascade => Assert.True(IsFinite(cascade.ViewProjection)));
    }

    private static RekallAgeVulkanShadowCamera Camera(uint receiverMask = uint.MaxValue) => new(
        new Vector3(0, 1, 0),
        Vector3.UnitZ,
        Vector3.UnitY,
        VerticalFieldOfViewRadians: MathF.PI / 3,
        AspectRatio: 16f / 9f,
        NearClip: 0.1f,
        FarClip: 250,
        ReceiverMask: receiverMask);

    private static RekallAgeVulkanDirectionalShadowLight Light(
        float maximumDistance = 100,
        uint casterMask = uint.MaxValue,
        uint receiverMask = uint.MaxValue) => new(
            Vector3.Normalize(new Vector3(-0.5f, -1, -0.35f)),
            CastShadows: true,
            CasterMask: casterMask,
            ReceiverMask: receiverMask,
            MaximumDistance: maximumDistance,
            DepthBias: 0.0015f,
            NormalBias: 0.02f,
            Priority: 10);

    private static IReadOnlyList<RekallAgeVulkanShadowCaster> Casters() =>
        [Caster("near", new Vector3(0, 0, 8)), Caster("far", new Vector3(1, 0, 28))];

    private static RekallAgeVulkanShadowCaster Caster(
        string id,
        Vector3 center,
        uint layerMask = uint.MaxValue,
        bool castShadows = true) => new(
            id,
            center - Vector3.One,
            center + Vector3.One,
            layerMask,
            castShadows);

    private static bool IsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
}
