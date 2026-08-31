using Rekall.Age.Studio;
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

    private static RekallAgeStudioViewportInteractionSnapshot Snapshot() => new(
        320,
        180,
        [new("cube", RekallAgeStudioViewportRegionKind.World, 80, 60, 40, 40, 4, 0)]);
}
