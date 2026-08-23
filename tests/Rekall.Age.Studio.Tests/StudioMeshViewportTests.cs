using System.Windows;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioMeshViewportTests
{
    [Fact]
    public void PointSelectionExposesAxisGizmoAndConvertsScreenDragToMeshTranslation()
    {
        var renderer = new RekallAgeStudioMeshViewportRenderer();
        var frame = renderer.Render(Quad(), RekallAgeGeometryDomain.Point, [1, 2], 640, 360, preview: false);

        Assert.NotNull(frame.TransformGizmo);
        var gizmo = frame.TransformGizmo!;
        var xAxis = Assert.Single(gizmo.Axes, axis => axis.Axis == RekallAgeStudioMeshTransformAxis.X);
        var start = new Point((gizmo.Origin.X + xAxis.End.X) / 2, (gizmo.Origin.Y + xAxis.End.Y) / 2);
        var gesture = renderer.BeginTransform(frame, start.X, start.Y);
        Assert.NotNull(gesture);
        var direction = xAxis.End - gizmo.Origin;
        direction.Normalize();

        var translation = renderer.ResolveTranslation(frame, gesture!, start.X + direction.X * frame.ProjectionScale, start.Y + direction.Y * frame.ProjectionScale);

        Assert.Equal(RekallAgeStudioMeshTransformAxis.X, gesture!.Axis);
        Assert.Equal(Math.Sqrt(3d / 2d), translation.X, precision: 8);
        Assert.Equal(0, translation.Y);
        Assert.Equal(0, translation.Z);
        Assert.Null(renderer.BeginTransform(frame, -100, -100));
    }

    [Fact]
    public void MeshViewportAutoFramesAndPicksStablePointEdgeFaceAndCornerIds()
    {
        var renderer = new RekallAgeStudioMeshViewportRenderer();
        var mesh = Quad();
        var frame = renderer.Render(mesh, RekallAgeGeometryDomain.Face, [21], 640, 360, preview: true);

        Assert.Equal(640, frame.Image.PixelWidth);
        Assert.Equal(360, frame.Image.PixelHeight);
        Assert.True(frame.IsPreview);
        Assert.Equal(21UL, renderer.Pick(frame, RekallAgeGeometryDomain.Face, frame.ElementCenters[(RekallAgeGeometryDomain.Face, 21)].X, frame.ElementCenters[(RekallAgeGeometryDomain.Face, 21)].Y));
        Assert.Equal(1UL, renderer.Pick(frame, RekallAgeGeometryDomain.Point, frame.ElementCenters[(RekallAgeGeometryDomain.Point, 1)].X, frame.ElementCenters[(RekallAgeGeometryDomain.Point, 1)].Y));
        Assert.Equal(11UL, renderer.Pick(frame, RekallAgeGeometryDomain.Edge, frame.ElementCenters[(RekallAgeGeometryDomain.Edge, 11)].X, frame.ElementCenters[(RekallAgeGeometryDomain.Edge, 11)].Y));
        Assert.Equal(31UL, renderer.Pick(frame, RekallAgeGeometryDomain.Corner, frame.ElementCenters[(RekallAgeGeometryDomain.Corner, 31)].X, frame.ElementCenters[(RekallAgeGeometryDomain.Corner, 31)].Y));
        Assert.Null(renderer.Pick(frame, RekallAgeGeometryDomain.Point, -100, -100));
    }

    private static RekallAgeMeshAsset Quad() => RekallAgeMeshAsset.Create("quad", "Quad",
        new(
            PointIds: [1, 2, 3, 4], Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13, 14], EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21], FaceOffsets: [0, 4], CornerIds: [31, 32, 33, 34],
            CornerPointIndices: [0, 1, 2, 3], CornerEdgeIndices: [0, 1, 2, 3]));
}
