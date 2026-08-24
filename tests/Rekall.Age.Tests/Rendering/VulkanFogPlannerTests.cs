using System.Numerics;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanFogPlannerTests
{
    [Theory]
    [InlineData("Performance", "analytic", 0, 0, 0)]
    [InlineData("Low", "analytic", 0, 0, 0)]
    [InlineData("Medium", "froxel-low", 120, 67, 32)]
    [InlineData("High", "froxel", 160, 90, 48)]
    [InlineData("Ultra", "froxel-high", 240, 135, 64)]
    [InlineData("Epic", "froxel-epic", 320, 180, 96)]
    public void ResolvedQualityControlsAnalyticOrBoundedFroxelGrid(
        string preset,
        string mode,
        int width,
        int height,
        int depth)
    {
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent(preset),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("planner-test"),
            2560,
            1440);

        var plan = new RekallAgeVulkanFogPlanner().Plan(Frame(), quality.Fog);

        Assert.Equal(mode, plan.Mode);
        Assert.Equal(new RekallAgeVulkanFogGrid(width, height, depth), plan.Grid);
        Assert.Equal(mode != "analytic", plan.UsesFroxelGrid);
        Assert.True(plan.Grid.CellCount <= 8_640_000);
    }

    [Fact]
    public void VolumePackingClampsOpticalFactsOrdersPriorityAndReportsOverflowIds()
    {
        var frame = Frame() with
        {
            FogVolumes =
            [
                Volume("global", "global", density: double.PositiveInfinity, priority: -10) with
                {
                    Albedo = "not-a-color",
                    Emission = "#ff8000",
                    Anisotropy = double.NaN,
                    HeightFalloff = -1
                },
                Volume("sphere-b", "sphere", density: 0.3, priority: 8) with
                {
                    Albedo = "#80c0ff",
                    Anisotropy = 4,
                    Transform = Transform(3, 4, 5, 2, 3, 4)
                },
                Volume("box-a", "box", density: 0.2, priority: 8) with
                {
                    BlendDistance = 99,
                    Transform = Transform(-2, 1, 8, 1, 2, 3)
                },
                Volume("sphere-c", "sphere", density: -4, priority: 4)
            ]
        };
        var quality = new RekallAgeResolvedFogQuality("froxel", 160, 90, 48);

        var plan = new RekallAgeVulkanFogPlanner().Plan(frame, quality, maximumLocalVolumes: 2);

        Assert.Equal(["box-a", "sphere-b", "global"], plan.Volumes.Select(item => item.EntityId));
        Assert.Equal(["sphere-c"], plan.DroppedEntityIds);
        var box = plan.Volumes[0];
        Assert.Equal("box", box.Shape);
        Assert.Equal(new Vector3(-2, 1, 8), box.Position);
        Assert.Equal(new Vector3(1, 2, 3), box.HalfExtents);
        Assert.Equal(3f, box.BlendDistance);
        var sphere = plan.Volumes[1];
        Assert.Equal(0.95f, sphere.Anisotropy);
        Assert.Equal(0.5019608f, sphere.Albedo.X, 5);
        Assert.Equal(0.7529412f, sphere.Albedo.Y, 5);
        Assert.Equal(1f, sphere.Albedo.Z, 5);
        var global = plan.Volumes[2];
        Assert.Equal(0f, global.Density);
        Assert.Equal(Vector3.One, global.Albedo);
        Assert.Equal(1f, global.Emission.X, 5);
        Assert.Equal(0.5019608f, global.Emission.Y, 5);
        Assert.Equal(0f, global.Emission.Z, 5);
        Assert.Equal(0f, global.Anisotropy);
        Assert.Equal(0f, global.HeightFalloff);
        Assert.All(plan.Volumes, volume =>
        {
            Assert.True(float.IsFinite(volume.Density));
            Assert.True(IsFinite(volume.Scattering));
            Assert.True(IsFinite(volume.Emission));
        });
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_FOG_VOLUME_LIMIT_CLAMPED"
            && item.EntityIds.SequenceEqual(["sphere-c"]));
    }

    [Fact]
    public void CameraCutAndGridChangesResetTemporalHistoryDeterministically()
    {
        var planner = new RekallAgeVulkanFogPlanner();
        var quality = new RekallAgeResolvedFogQuality("froxel", 160, 90, 48);
        var first = planner.Plan(Frame(frameIndex: 10), quality);
        var continued = planner.Plan(Frame(frameIndex: 11), quality, first.NextHistory);
        var cutFrame = Frame(frameIndex: 12) with
        {
            ActiveCamera = Frame().ActiveCamera! with { X = 12 },
            Cameras = [Frame().ActiveCamera! with { X = 12 }]
        };
        var cut = planner.Plan(cutFrame, quality, continued.NextHistory);
        var resized = planner.Plan(
            Frame(frameIndex: 12),
            new RekallAgeResolvedFogQuality("froxel-high", 240, 135, 64),
            continued.NextHistory);

        Assert.True(first.HistoryReset);
        Assert.False(first.TemporalReprojection);
        Assert.False(continued.HistoryReset);
        Assert.True(continued.TemporalReprojection);
        Assert.True(cut.HistoryReset);
        Assert.False(cut.TemporalReprojection);
        Assert.Contains(cut.Diagnostics, item => item.Code == "REKALL_FOG_HISTORY_CAMERA_CUT");
        Assert.True(resized.HistoryReset);
        Assert.Contains(resized.Diagnostics, item => item.Code == "REKALL_FOG_HISTORY_GRID_CHANGED");
    }

    [Fact]
    public void GlobalVolumePackingIsBoundedAndReportsDeterministicOverflowIds()
    {
        var frame = Frame() with
        {
            FogVolumes = Enumerable.Range(0, RekallAgeVulkanFogPlanner.DefaultMaximumGlobalVolumes + 2)
                .Select(index => Volume($"global-{index:D2}", "global", density: 0.1, priority: 0))
                .ToArray()
        };

        var plan = new RekallAgeVulkanFogPlanner().Plan(
            frame,
            new RekallAgeResolvedFogQuality("froxel", 160, 90, 48));

        Assert.Equal(RekallAgeVulkanFogPlanner.DefaultMaximumGlobalVolumes, plan.Volumes.Count);
        Assert.Equal(["global-08", "global-09"], plan.DroppedEntityIds);
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_FOG_VOLUME_LIMIT_CLAMPED"
            && item.EntityIds.SequenceEqual(["global-08", "global-09"]));
    }

    private static RekallAgeRuntimeViewportFrame Frame(int frameIndex = 0)
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Camera3D",
            true,
            0,
            2,
            -6,
            FieldOfViewDegrees: 60,
            NearClip: 0.1,
            FarClip: 100);
        return new RekallAgeRuntimeViewportFrame(
            "Fog Planner",
            frameIndex,
            frameIndex / 60.0,
            256,
            144,
            camera,
            [camera],
            [],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
    }

    private static RekallAgeRuntimeViewportFogVolume Volume(
        string id,
        string shape,
        double density,
        int priority) => new(
            id,
            id,
            shape,
            density,
            "#ffffff",
            "#000000",
            0,
            0,
            0,
            priority,
            RekallAgeRuntimeViewportTransform.Identity);

    private static RekallAgeRuntimeViewportTransform Transform(
        double x,
        double y,
        double z,
        double scaleX,
        double scaleY,
        double scaleZ) => new(x, y, z, 0, 0, 0, scaleX, scaleY, scaleZ);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
