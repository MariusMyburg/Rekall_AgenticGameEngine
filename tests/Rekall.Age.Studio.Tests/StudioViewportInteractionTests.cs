using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioViewportInteractionTests
{
    [Fact]
    public void MapDisplayPointRejectsLetterboxAndMapsImagePixelsExactly()
    {
        var snapshot = new RekallAgeStudioViewportInteractionSnapshot(320, 180, []);

        Assert.Equal(new RekallAgeStudioViewportPoint(160, 90), snapshot.MapDisplayPoint(400, 400, 200, 200));
        Assert.Null(snapshot.MapDisplayPoint(400, 400, 200, 50));
        Assert.Equal(new RekallAgeStudioViewportPoint(0, 0), snapshot.MapDisplayPoint(400, 400, 0, 87.5));
    }

    [Fact]
    public void PickPrioritizesUiThenNearestWorldRegionAndEmptySpaceClearsSelection()
    {
        var snapshot = new RekallAgeStudioViewportInteractionSnapshot(
            320,
            180,
            [
                new("far", RekallAgeStudioViewportRegionKind.World, 120, 60, 80, 60, 12, 0),
                new("near", RekallAgeStudioViewportRegionKind.World, 130, 70, 60, 40, 4, 0),
                new("button", RekallAgeStudioViewportRegionKind.Ui, 140, 75, 40, 30, 0, 400)
            ]);

        Assert.Equal("button", snapshot.Pick(160, 90));
        Assert.Equal("near", snapshot.Pick(135, 90));
        Assert.Equal("far", snapshot.Pick(125, 90));
        Assert.Null(snapshot.Pick(10, 10));
    }

    [Fact]
    public void BuilderProjectsVisible3dEntityAndExcludesHiddenEntityAndSyntheticSurfaces()
    {
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            320,
            180,
            new("camera", "Camera", "Camera3D", true, Z: -10),
            [],
            [
                new("cube", "Cube", "mesh", null, 0, 0, 0, 100),
                new("hidden", "Hidden", "mesh", null, 0, 0, 1, 101),
                new("cube:collider", "Cube Collider", "mesh", null, 0, 0, 0, 910)
            ],
            0,
            new(false, 0),
            []);
        var entities = new[]
        {
            Entity("cube", visible: true),
            Entity("hidden", visible: false)
        };

        var snapshot = RekallAgeStudioViewportInteractionBuilder.Build(frame, entities);

        Assert.Equal("cube", snapshot.Pick(160, 90));
        Assert.Single(snapshot.Regions, region => region.EntityId == "cube");
        Assert.DoesNotContain(snapshot.Regions, region => region.EntityId == "hidden");
        Assert.DoesNotContain(snapshot.Regions, region => region.EntityId.Contains(':', StringComparison.Ordinal));
    }

    private static RekallAgeRuntimeEntity Entity(string id, bool visible) => new(
        id,
        id,
        [],
        null,
        null,
        visible,
        false,
        RekallAgeRuntimeTransform.Identity,
        []);
}
