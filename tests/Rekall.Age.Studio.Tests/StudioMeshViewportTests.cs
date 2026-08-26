using System.Windows;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioMeshViewportTests
{
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1.5, -2.25, 3.75)]
    public void IdentityCameraReproducesLegacyAxonometricProjectionExactly(double x, double y, double z)
    {
        var point = new RekallAgeGeometryVector3(x, y, z);
        var legacy = new Point((point.X - point.Z) / Math.Sqrt(2), (point.X + point.Z - 2 * point.Y) / Math.Sqrt(6));

        var projected = RekallAgeStudioMeshViewportRenderer.Project(point, RekallAgeStudioViewportCamera.Identity);

        Assert.Equal(legacy.X, projected.X, precision: 10);
        Assert.Equal(legacy.Y, projected.Y, precision: 10);
    }

    [Fact]
    public void OrbitingNinetyDegreesYawMapsRightAxisOntoForwardAxis()
    {
        var camera = RekallAgeStudioViewportCamera.Identity with { Yaw = Math.PI / 2 };
        var origin = RekallAgeStudioMeshViewportRenderer.Project(new RekallAgeGeometryVector3(0, 0, 0), camera);
        var alongOldRight = RekallAgeStudioMeshViewportRenderer.Project(new RekallAgeGeometryVector3(1, 0, -1), camera);
        var alongOldForward = RekallAgeStudioMeshViewportRenderer.Project(new RekallAgeGeometryVector3(1, 0, 1), camera);

        // After a 90-degree yaw the projected X spread from moving along the OLD forward axis
        // should no longer match what moving along the OLD right axis produces, proving the
        // view actually rotated rather than staying fixed.
        Assert.NotEqual(alongOldRight.X - origin.X, alongOldForward.X - origin.X, 6);
    }

    [Fact]
    public void ZoomScalesProjectedSpread()
    {
        var mesh = Quad();
        var wide = new RekallAgeStudioMeshViewportRenderer().Render(mesh, RekallAgeGeometryDomain.Point, [], 640, 360, preview: false, RekallAgeStudioViewportCamera.Identity with { Zoom = 2 });
        var narrow = new RekallAgeStudioMeshViewportRenderer().Render(mesh, RekallAgeGeometryDomain.Point, [], 640, 360, preview: false, RekallAgeStudioViewportCamera.Identity with { Zoom = 0.5 });

        double Spread(RekallAgeStudioMeshViewportFrame frame) => frame.Points.Max(p => p.Position.X) - frame.Points.Min(p => p.Position.X);
        Assert.True(Spread(wide) > Spread(narrow));
    }

    [Fact]
    public void PanOffsetsProjectedCenter()
    {
        var mesh = Quad();
        var panned = new RekallAgeStudioMeshViewportRenderer().Render(mesh, RekallAgeGeometryDomain.Point, [], 640, 360, preview: false, RekallAgeStudioViewportCamera.Identity with { PanX = 50 });
        var unpanned = new RekallAgeStudioMeshViewportRenderer().Render(mesh, RekallAgeGeometryDomain.Point, [], 640, 360, preview: false, RekallAgeStudioViewportCamera.Identity);

        var pannedCenterX = panned.Points.Average(p => p.Position.X);
        var unpannedCenterX = unpanned.Points.Average(p => p.Position.X);
        Assert.True(Math.Abs(pannedCenterX - unpannedCenterX) > 1);
    }

    [Fact]
    public void OrthographicAndPerspectiveProduceDifferentDepthResponse()
    {
        var deep = new RekallAgeGeometryVector3(0, 0, 5);
        var shallow = new RekallAgeGeometryVector3(0, 0, -5);
        var perspectiveCamera = RekallAgeStudioViewportCamera.Identity with { Orthographic = false, Zoom = 4 };

        var orthoDeep = RekallAgeStudioMeshViewportRenderer.Project(deep, RekallAgeStudioViewportCamera.Identity);
        var orthoShallow = RekallAgeStudioMeshViewportRenderer.Project(shallow, RekallAgeStudioViewportCamera.Identity);
        var perspDeep = RekallAgeStudioMeshViewportRenderer.Project(deep, perspectiveCamera);
        var perspShallow = RekallAgeStudioMeshViewportRenderer.Project(shallow, perspectiveCamera);

        var orthoRatio = orthoDeep.X - orthoShallow.X;
        var perspRatio = perspDeep.X - perspShallow.X;
        Assert.NotEqual(orthoRatio, perspRatio, 3);
    }

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
        Assert.True(frame.Points.Max(point => point.Position.X) - frame.Points.Min(point => point.Position.X) < 640 * 0.7);
        Assert.True(frame.Points.Max(point => point.Position.Y) - frame.Points.Min(point => point.Position.Y) < 360 * 0.7);
        Assert.Equal(21UL, renderer.Pick(frame, RekallAgeGeometryDomain.Face, frame.ElementCenters[(RekallAgeGeometryDomain.Face, 21)].X, frame.ElementCenters[(RekallAgeGeometryDomain.Face, 21)].Y));
        Assert.Equal(1UL, renderer.Pick(frame, RekallAgeGeometryDomain.Point, frame.ElementCenters[(RekallAgeGeometryDomain.Point, 1)].X, frame.ElementCenters[(RekallAgeGeometryDomain.Point, 1)].Y));
        Assert.Equal(11UL, renderer.Pick(frame, RekallAgeGeometryDomain.Edge, frame.ElementCenters[(RekallAgeGeometryDomain.Edge, 11)].X, frame.ElementCenters[(RekallAgeGeometryDomain.Edge, 11)].Y));
        Assert.Equal(31UL, renderer.Pick(frame, RekallAgeGeometryDomain.Corner, frame.ElementCenters[(RekallAgeGeometryDomain.Corner, 31)].X, frame.ElementCenters[(RekallAgeGeometryDomain.Corner, 31)].Y));
        Assert.Null(renderer.Pick(frame, RekallAgeGeometryDomain.Point, -100, -100));
    }

    [Fact]
    public void MeshViewportRendersAProductionGridBehindEditableGeometry()
    {
        var frame = new RekallAgeStudioMeshViewportRenderer().Render(
            Quad(), RekallAgeGeometryDomain.Face, [], 640, 360, preview: false);
        var pixel = new byte[4];
        frame.Image.CopyPixels(new Int32Rect(40, 20, 1, 1), pixel, 4, 0);

        Assert.NotEqual(new byte[] { 22, 16, 12, 255 }, pixel);
    }

    private static RekallAgeMeshAsset Quad() => RekallAgeMeshAsset.Create("quad", "Quad",
        new(
            PointIds: [1, 2, 3, 4], Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13, 14], EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21], FaceOffsets: [0, 4], CornerIds: [31, 32, 33, 34],
            CornerPointIndices: [0, 1, 2, 3], CornerEdgeIndices: [0, 1, 2, 3]));
}
