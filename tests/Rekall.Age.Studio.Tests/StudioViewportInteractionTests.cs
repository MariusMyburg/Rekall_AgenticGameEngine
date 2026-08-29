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

    [Fact]
    public void BuilderProjects2dEntityThroughTheActiveOrthographicCamera()
    {
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            960,
            540,
            new(
                "camera",
                "Camera",
                "Camera2D",
                true,
                X: 2.5,
                Y: 6.9,
                Z: -1,
                ProjectionMode: "orthographic",
                OrthographicSize: 6),
            [],
            [new("rover", "Rover", "mesh", null, 1, 5.9, -0.002, 120,
                GeometryMesh: Rectangle(2.4, 1.1))],
            0,
            new(false, 0),
            []);

        var snapshot = RekallAgeStudioViewportInteractionBuilder.Build(frame, [Entity("rover", visible: true)]);

        Assert.Equal("rover", snapshot.Pick(345, 360));
        Assert.Null(snapshot.Pick(498, 164));
    }

    [Fact]
    public void BuilderUsesRotated2dGeometryRatherThanTransformScaleAsPickBounds()
    {
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main", 0, 0, 400, 200,
            new("camera", "Camera", "Camera2D", true, Z: -1, ProjectionMode: "orthographic", OrthographicSize: 10),
            [],
            [new("terrain", "Terrain", "mesh", null, 0, 0, 0, 100,
                RotationZ: 30, GeometryMesh: Rectangle(12, 1))],
            0, new(false, 0), []);

        var snapshot = RekallAgeStudioViewportInteractionBuilder.Build(frame, [Entity("terrain", true)]);

        Assert.Equal("terrain", snapshot.Pick(300, 42));
        Assert.Null(snapshot.Pick(300, 142));
    }

    [Fact]
    public void BuilderClips2dGeometryPickingToTheActiveCameraViewport()
    {
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main", 0, 0, 400, 200,
            new("camera", "Camera", "Camera2D", true,
                Z: -1, ProjectionMode: "orthographic", OrthographicSize: 10,
                ViewportX: 0.5, ViewportY: 0, ViewportWidth: 0.5, ViewportHeight: 1),
            [],
            [new("wide", "Wide", "mesh", null, 0, 0, 0, 100, GeometryMesh: Rectangle(20, 8))],
            0, new(false, 0), []);

        var snapshot = RekallAgeStudioViewportInteractionBuilder.Build(frame, [Entity("wide", true)]);

        Assert.Null(snapshot.Pick(199, 100));
        Assert.Equal("wide", snapshot.Pick(250, 100));
    }

    private static RekallAgeRuntimeViewportGeometryMesh Rectangle(double width, double height) => new(
        [
            new(-width / 2, -height / 2, 0),
            new(width / 2, -height / 2, 0),
            new(width / 2, height / 2, 0),
            new(-width / 2, height / 2, 0)
        ],
        [0, 1, 2, 0, 2, 3]);

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
