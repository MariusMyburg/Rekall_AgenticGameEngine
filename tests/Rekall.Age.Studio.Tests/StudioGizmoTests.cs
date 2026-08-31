using Rekall.Age.Studio;
using Rekall.Age.Rendering.Abstractions;
using System.IO;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioGizmoTests
{
    [Fact]
    public void EntitySelectionPresentsTheNewEditorOnlyGizmoFrame()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "RekallAgeStudioViewModel.cs"));
        var selection = source[source.IndexOf("public async Task SelectEntityAsync", StringComparison.Ordinal)..
            source.IndexOf("public bool BeginSceneTransform", StringComparison.Ordinal)];

        Assert.Equal(2, selection.Split("refreshPreviewAfter: true", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ProjectedGizmoHitTestsAxesAndMoveUsesLiteralSnappedWorldDelta()
    {
        var snapshot = Snapshot();
        var gizmo = Assert.IsType<RekallAgeStudioSceneGizmo>(
            RekallAgeStudioSceneGizmo.Create(snapshot, "cube", locked: false));

        Assert.Equal(new RekallAgeStudioViewportPoint(100, 80), gizmo.Origin);
        Assert.Equal(RekallAgeStudioTransformAxis.X, gizmo.HitTest(135, 80));
        Assert.Equal(RekallAgeStudioTransformAxis.Y, gizmo.HitTest(100, 45));
        Assert.Equal(RekallAgeStudioTransformAxis.Z, gizmo.HitTest(125, 55));
        Assert.Null(gizmo.HitTest(20, 20));

        var gesture = gizmo.Begin(
            RekallAgeStudioTransformTool.Move,
            RekallAgeStudioTransformAxis.X,
            100,
            80,
            initialValue: 10,
            snap: 0.25);
        var update = gesture.Update(126, 80);

        Assert.Equal("x", update.PropertyName);
        Assert.Equal(10.5, update.Value, 6);
    }

    [Theory]
    [InlineData(RekallAgeStudioTransformTool.Rotate, RekallAgeStudioTransformAxis.Y, "yaw", 5.0, 20.0, 15.0)]
    [InlineData(RekallAgeStudioTransformTool.Scale, RekallAgeStudioTransformAxis.Z, "scaleZ", 1.0, 1.3, 0.1)]
    public void RotateAndScaleProduceCanonicalTransform3dProperties(
        RekallAgeStudioTransformTool tool,
        RekallAgeStudioTransformAxis axis,
        string expectedProperty,
        double initial,
        double expected,
        double snap)
    {
        var gizmo = Assert.IsType<RekallAgeStudioSceneGizmo>(
            RekallAgeStudioSceneGizmo.Create(Snapshot(), "cube", locked: false));
        var gesture = gizmo.Begin(tool, axis, 100, 80, initial, snap);

        var update = gesture.Update(130, 50);

        Assert.Equal(expectedProperty, update.PropertyName);
        Assert.Equal(expected, update.Value, 6);
    }

    [Fact]
    public void LockedEntityDoesNotExposeAnEditableGizmo()
    {
        Assert.Null(RekallAgeStudioSceneGizmo.Create(Snapshot(), "cube", locked: true));
    }

    [Theory]
    [InlineData(RekallAgeStudioTransformTool.Move, 4)]
    [InlineData(RekallAgeStudioTransformTool.Scale, 4)]
    [InlineData(RekallAgeStudioTransformTool.Rotate, 48)]
    public void VulkanSceneGizmoUsesEditorOnlyAxisGeometry(
        RekallAgeStudioTransformTool tool,
        int expectedSegmentCount)
    {
        var renderables = RekallAgeStudioSceneGizmoRenderables.Create(
            tool, RekallAgeStudioTransformSpace.Local, 1, 2, 3, 10, 20, 30);

        Assert.Equal(3, renderables.Count);
        Assert.All(renderables, renderable =>
        {
            Assert.Equal("studio-editor", renderable.Layer);
            Assert.Equal("mesh", renderable.Kind);
            Assert.Equal((1d, 2d, 3d), (renderable.X, renderable.Y, renderable.Z));
            Assert.Equal((10d, 20d, 30d), (renderable.RotationX, renderable.RotationY, renderable.RotationZ));
            Assert.Equal(expectedSegmentCount, renderable.LineSegments!.Segments.Count);
            Assert.False(renderable.CastShadows);
        });
    }

    [Fact]
    public void VulkanEditorGridSeparatesMinorLinesFromWorldOriginAxes()
    {
        var renderables = RekallAgeStudioViewportOverlayRenderables.CreateGrid(2, 1);

        Assert.Equal(3, renderables.Count);
        var grid = Assert.Single(renderables, item => item.EntityId == "__studio_grid");
        Assert.Equal(8, grid.LineSegments!.Segments.Count);
        Assert.DoesNotContain(grid.LineSegments.Segments, line =>
            line.FromX == 0 && line.ToX == 0 || line.FromZ == 0 && line.ToZ == 0);
        Assert.Equal("#d34b4b", Assert.Single(renderables, item => item.EntityId == "__studio_grid_x").MaterialColor);
        Assert.Equal("#4c79d8", Assert.Single(renderables, item => item.EntityId == "__studio_grid_z").MaterialColor);
        Assert.All(renderables, item => Assert.Equal("studio-editor", item.Layer));
    }

    [Fact]
    public void VulkanSelectionOutlineUsesRenderableLocalGeometryBoundsAndTransform()
    {
        var selected = new RekallAgeRuntimeViewportRenderable(
            "crate", "Crate", "mesh", null, 4, 5, 6, 0,
            RotationX: 10,
            RotationY: 20,
            RotationZ: 30,
            ScaleX: 2,
            ScaleY: 3,
            ScaleZ: 4,
            GeometryMesh: new RekallAgeRuntimeViewportGeometryMesh(
            [
                new(-2, -1, -3),
                new(4, 5, 7),
                new(0, 2, 1)
            ], [0, 1, 2]));

        var outline = Assert.IsType<RekallAgeRuntimeViewportRenderable>(
            RekallAgeStudioViewportOverlayRenderables.CreateSelectionOutline(selected));

        Assert.Equal("__studio_selection_crate", outline.EntityId);
        Assert.Equal((4d, 5d, 6d), (outline.X, outline.Y, outline.Z));
        Assert.Equal((10d, 20d, 30d), (outline.RotationX, outline.RotationY, outline.RotationZ));
        Assert.Equal((2d, 3d, 4d), (outline.ScaleX, outline.ScaleY, outline.ScaleZ));
        Assert.Equal(12, outline.LineSegments!.Segments.Count);
        Assert.Contains(outline.LineSegments.Segments, line =>
            line.FromX == -2 && line.FromY == -1 && line.FromZ == -3
            && line.ToX == 4 && line.ToY == -1 && line.ToZ == -3);
        Assert.Equal("#ff9f32", outline.EmissiveColor);
        Assert.False(outline.CastShadows);
    }

    [Fact]
    public void VulkanSelectionOutlineRejectsNonFiniteOrMissingGeometry()
    {
        var missing = new RekallAgeRuntimeViewportRenderable("empty", "Empty", "mesh", null, 0, 0, 0, 0);
        var invalid = missing with
        {
            GeometryMesh = new RekallAgeRuntimeViewportGeometryMesh([new(double.NaN, 0, 0)], [])
        };

        Assert.Null(RekallAgeStudioViewportOverlayRenderables.CreateSelectionOutline(missing));
        Assert.Null(RekallAgeStudioViewportOverlayRenderables.CreateSelectionOutline(invalid));
    }

    [Fact]
    public void VulkanSelectionOutlineSupportsBuiltInPrimitivesWithoutAuthoredGeometry()
    {
        var cube = new RekallAgeRuntimeViewportRenderable(
            "cube", "Cube", "mesh", null, 0, 0, 0, 0, Variant: "cube");

        var outline = Assert.IsType<RekallAgeRuntimeViewportRenderable>(
            RekallAgeStudioViewportOverlayRenderables.CreateSelectionOutline(cube));

        Assert.Equal(12, outline.LineSegments!.Segments.Count);
        Assert.Contains(outline.LineSegments.Segments, line =>
            line.FromX == -0.5 && line.FromY == -0.5 && line.FromZ == -0.5
            && line.ToX == 0.5 && line.ToY == -0.5 && line.ToZ == -0.5);
    }

    private static RekallAgeStudioViewportInteractionSnapshot Snapshot() => new(
        320,
        180,
        [new("cube", RekallAgeStudioViewportRegionKind.World, 80, 60, 40, 40, 4, 0)]);
}
