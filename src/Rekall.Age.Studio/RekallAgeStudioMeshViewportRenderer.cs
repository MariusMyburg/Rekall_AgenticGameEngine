using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioMeshViewportFrame(
    BitmapSource Image,
    IReadOnlyDictionary<(RekallAgeGeometryDomain Domain, ulong Id), Point> ElementCenters,
    bool IsPreview,
    IReadOnlyList<RekallAgeStudioMeshViewportFace> Faces,
    IReadOnlyList<RekallAgeStudioMeshViewportEdge> Edges,
    IReadOnlyList<RekallAgeStudioMeshViewportPoint> Points,
    IReadOnlyList<RekallAgeStudioMeshViewportPoint> Corners,
    double ProjectionScale,
    RekallAgeStudioMeshTransformGizmo? TransformGizmo);

internal sealed record RekallAgeStudioMeshViewportFace(ulong Id, IReadOnlyList<Point> Polygon);
internal sealed record RekallAgeStudioMeshViewportEdge(ulong Id, Point A, Point B);
internal sealed record RekallAgeStudioMeshViewportPoint(ulong Id, Point Position);
internal enum RekallAgeStudioMeshTransformAxis { X, Y, Z }
internal sealed record RekallAgeStudioMeshTransformGizmoAxis(RekallAgeStudioMeshTransformAxis Axis, Point End);
internal sealed record RekallAgeStudioMeshTransformGizmo(Point Origin, IReadOnlyList<RekallAgeStudioMeshTransformGizmoAxis> Axes);
internal sealed record RekallAgeStudioMeshTransformGesture(RekallAgeStudioMeshTransformAxis Axis, Point Start);

internal sealed class RekallAgeStudioMeshViewportRenderer
{
    private const double PointHitRadius = 9;
    private const double EdgeHitRadius = 7;

    public RekallAgeStudioMeshViewportFrame Render(
        RekallAgeMeshAsset mesh,
        RekallAgeGeometryDomain activeDomain,
        IReadOnlyCollection<ulong> selectedIds,
        int width,
        int height,
        bool preview)
    {
        ArgumentNullException.ThrowIfNull(mesh); ArgumentNullException.ThrowIfNull(selectedIds);
        if (width < 64 || width > 4096 || height < 64 || height > 4096) throw new ArgumentOutOfRangeException(nameof(width), "Mesh viewport dimensions must be 64-4096 pixels.");
        var raw = mesh.Topology.Positions.Select(Project).ToArray();
        var minX = raw.Min(point => point.X); var maxX = raw.Max(point => point.X);
        var minY = raw.Min(point => point.Y); var maxY = raw.Max(point => point.Y);
        var spanX = Math.Max(maxX - minX, 1e-9); var spanY = Math.Max(maxY - minY, 1e-9);
        const double padding = 28;
        var scale = Math.Min((width - padding * 2) / spanX, (height - padding * 2) / spanY);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var centerX = (minX + maxX) / 2; var centerY = (minY + maxY) / 2;
        var projected = raw.Select(point => new Point(width / 2d + (point.X - centerX) * scale, height / 2d + (point.Y - centerY) * scale)).ToArray();
        var selected = selectedIds.ToHashSet(); var centers = new Dictionary<(RekallAgeGeometryDomain, ulong), Point>();
        var faces = new List<RekallAgeStudioMeshViewportFace>();
        for (var face = 0; face < mesh.Topology.FaceIds.Count; face++)
        {
            var polygon = Enumerable.Range(mesh.Topology.FaceOffsets[face], mesh.Topology.FaceOffsets[face + 1] - mesh.Topology.FaceOffsets[face])
                .Select(corner => projected[mesh.Topology.CornerPointIndices[corner]]).ToArray();
            var id = mesh.Topology.FaceIds[face]; faces.Add(new(id, polygon)); centers[(RekallAgeGeometryDomain.Face, id)] = Center(polygon);
        }
        var edges = mesh.Topology.EdgeIds.Select((id, index) =>
        {
            var edge = mesh.Topology.EdgePointIndices[index]; var item = new RekallAgeStudioMeshViewportEdge(id, projected[edge.A], projected[edge.B]);
            centers[(RekallAgeGeometryDomain.Edge, id)] = Midpoint(item.A, item.B); return item;
        }).ToArray();
        var points = mesh.Topology.PointIds.Select((id, index) =>
        {
            var item = new RekallAgeStudioMeshViewportPoint(id, projected[index]); centers[(RekallAgeGeometryDomain.Point, id)] = item.Position; return item;
        }).ToArray();
        var corners = mesh.Topology.CornerIds.Select((id, index) =>
        {
            var item = new RekallAgeStudioMeshViewportPoint(id, projected[mesh.Topology.CornerPointIndices[index]]); centers[(RekallAgeGeometryDomain.Corner, id)] = item.Position; return item;
        }).ToArray();
        RekallAgeStudioMeshTransformGizmo? gizmo = null;
        if (activeDomain == RekallAgeGeometryDomain.Point && selected.Count > 0)
        {
            var selectedPointIndices = mesh.Topology.PointIds.Select((id, index) => (id, index)).Where(item => selected.Contains(item.id)).Select(item => item.index).ToArray();
            if (selectedPointIndices.Length > 0)
            {
                var origin = new Point(selectedPointIndices.Average(index => projected[index].X), selectedPointIndices.Average(index => projected[index].Y));
                gizmo = new(origin, new[]
                {
                    GizmoAxis(RekallAgeStudioMeshTransformAxis.X, origin),
                    GizmoAxis(RekallAgeStudioMeshTransformAxis.Y, origin),
                    GizmoAxis(RekallAgeStudioMeshTransformAxis.Z, origin)
                });
            }
        }

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 16, 22)), null, new Rect(0, 0, width, height));
            var faceBrush = new SolidColorBrush(preview ? Color.FromArgb(165, 57, 102, 112) : Color.FromArgb(165, 38, 67, 91));
            var selectedFaceBrush = new SolidColorBrush(Color.FromArgb(205, 38, 157, 181));
            var outline = new Pen(new SolidColorBrush(Color.FromRgb(86, 108, 132)), 1);
            foreach (var face in faces)
                drawing.DrawGeometry(activeDomain == RekallAgeGeometryDomain.Face && selected.Contains(face.Id) ? selectedFaceBrush : faceBrush, outline, Polygon(face.Polygon));
            foreach (var edge in edges)
            {
                var highlighted = activeDomain == RekallAgeGeometryDomain.Edge && selected.Contains(edge.Id);
                drawing.DrawLine(new Pen(new SolidColorBrush(highlighted ? Color.FromRgb(255, 190, 72) : Color.FromRgb(151, 169, 189)), highlighted ? 3 : 1.25), edge.A, edge.B);
            }
            foreach (var point in points)
            {
                var highlighted = activeDomain == RekallAgeGeometryDomain.Point && selected.Contains(point.Id);
                drawing.DrawEllipse(new SolidColorBrush(highlighted ? Color.FromRgb(255, 190, 72) : Color.FromRgb(211, 222, 234)), null, point.Position, highlighted ? 5 : 2.5, highlighted ? 5 : 2.5);
            }
            if (activeDomain == RekallAgeGeometryDomain.Corner)
                foreach (var corner in corners)
                    drawing.DrawEllipse(new SolidColorBrush(selected.Contains(corner.Id) ? Color.FromRgb(255, 190, 72) : Color.FromRgb(183, 126, 255)), null, corner.Position, 4, 4);
            if (gizmo is not null)
            {
                foreach (var axis in gizmo.Axes)
                {
                    var color = AxisColor(axis.Axis);
                    drawing.DrawLine(new Pen(new SolidColorBrush(color), 3), gizmo.Origin, axis.End);
                    drawing.DrawEllipse(new SolidColorBrush(color), null, axis.End, 4, 4);
                    drawing.DrawText(new FormattedText(axis.Axis.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Semibold"), 10, new SolidColorBrush(color), 1), axis.End + new Vector(5, -7));
                }
                drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(235, 239, 245)), null, gizmo.Origin, 4, 4);
            }
            if (preview)
                drawing.DrawText(new FormattedText("PREVIEW", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 11, new SolidColorBrush(Color.FromRgb(85, 214, 229)), 1), new Point(12, 10));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        return new(bitmap, centers, preview, faces, edges, points, corners, scale, gizmo);
    }

    public RekallAgeStudioMeshTransformGesture? BeginTransform(RekallAgeStudioMeshViewportFrame frame, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.TransformGizmo is null) return null;
        var point = new Point(x, y);
        return frame.TransformGizmo.Axes
            .Select(axis => (axis, distance: DistanceToSegment(point, frame.TransformGizmo.Origin, axis.End)))
            .Where(item => item.distance <= EdgeHitRadius)
            .OrderBy(item => item.distance)
            .ThenBy(item => item.axis.Axis)
            .Select(item => new RekallAgeStudioMeshTransformGesture(item.axis.Axis, point))
            .FirstOrDefault();
    }

    public RekallAgeGeometryVector3 ResolveTranslation(
        RekallAgeStudioMeshViewportFrame frame,
        RekallAgeStudioMeshTransformGesture gesture,
        double x,
        double y)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(gesture);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(frame.ProjectionScale) || frame.ProjectionScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(x), "Transform coordinates and projection scale must be finite and positive.");
        var projectedAxis = Project(AxisVector(gesture.Axis));
        var projectedLength = new Vector(projectedAxis.X, projectedAxis.Y).Length;
        var direction = new Vector(projectedAxis.X / projectedLength, projectedAxis.Y / projectedLength);
        var meshDistance = Vector.Multiply(new Point(x, y) - gesture.Start, direction) / (frame.ProjectionScale * projectedLength);
        return gesture.Axis switch
        {
            RekallAgeStudioMeshTransformAxis.X => new(meshDistance, 0, 0),
            RekallAgeStudioMeshTransformAxis.Y => new(0, meshDistance, 0),
            RekallAgeStudioMeshTransformAxis.Z => new(0, 0, meshDistance),
            _ => throw new ArgumentOutOfRangeException(nameof(gesture))
        };
    }

    public ulong? Pick(RekallAgeStudioMeshViewportFrame frame, RekallAgeGeometryDomain domain, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(frame); var point = new Point(x, y);
        return domain switch
        {
            RekallAgeGeometryDomain.Point => Nearest(frame.Points, point, PointHitRadius),
            RekallAgeGeometryDomain.Corner => Nearest(frame.Corners, point, PointHitRadius),
            RekallAgeGeometryDomain.Edge => frame.Edges.Select(edge => (edge.Id, DistanceToSegment(point, edge.A, edge.B))).Where(item => item.Item2 <= EdgeHitRadius).OrderBy(item => item.Item2).ThenBy(item => item.Id).Select(item => (ulong?)item.Id).FirstOrDefault(),
            RekallAgeGeometryDomain.Face => frame.Faces.AsEnumerable().Reverse().FirstOrDefault(face => Contains(face.Polygon, point))?.Id,
            _ => null
        };
    }

    private static Point Project(RekallAgeGeometryVector3 point) => new((point.X - point.Z) / Math.Sqrt(2), (point.X + point.Z - 2 * point.Y) / Math.Sqrt(6));
    private static RekallAgeGeometryVector3 AxisVector(RekallAgeStudioMeshTransformAxis axis) => axis switch
    {
        RekallAgeStudioMeshTransformAxis.X => new(1, 0, 0),
        RekallAgeStudioMeshTransformAxis.Y => new(0, 1, 0),
        RekallAgeStudioMeshTransformAxis.Z => new(0, 0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };
    private static RekallAgeStudioMeshTransformGizmoAxis GizmoAxis(RekallAgeStudioMeshTransformAxis axis, Point origin)
    {
        var projected = Project(AxisVector(axis));
        var direction = new Vector(projected.X, projected.Y); direction.Normalize();
        return new(axis, origin + direction * 52);
    }
    private static Color AxisColor(RekallAgeStudioMeshTransformAxis axis) => axis switch
    {
        RekallAgeStudioMeshTransformAxis.X => Color.FromRgb(244, 83, 91),
        RekallAgeStudioMeshTransformAxis.Y => Color.FromRgb(95, 205, 118),
        RekallAgeStudioMeshTransformAxis.Z => Color.FromRgb(82, 145, 246),
        _ => Colors.White
    };
    private static Point Center(IReadOnlyList<Point> points) => new(points.Average(item => item.X), points.Average(item => item.Y));
    private static Point Midpoint(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    private static StreamGeometry Polygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry(); using var context = geometry.Open(); context.BeginFigure(points[0], true, true); context.PolyLineTo(points.Skip(1).ToArray(), true, false); geometry.Freeze(); return geometry;
    }
    private static ulong? Nearest(IEnumerable<RekallAgeStudioMeshViewportPoint> values, Point point, double radius) => values
        .Select(item => (item.Id, Distance: (item.Position - point).Length)).Where(item => item.Distance <= radius)
        .OrderBy(item => item.Distance).ThenBy(item => item.Id).Select(item => (ulong?)item.Id).FirstOrDefault();
    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        var segment = b - a; var lengthSquared = segment.LengthSquared;
        if (lengthSquared <= 1e-12) return (point - a).Length;
        var projection = Math.Clamp(Vector.Multiply(point - a, segment) / lengthSquared, 0, 1);
        return (point - (a + segment * projection)).Length;
    }
    private static bool Contains(IReadOnlyList<Point> polygon, Point point)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            var a = polygon[current]; var b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}
