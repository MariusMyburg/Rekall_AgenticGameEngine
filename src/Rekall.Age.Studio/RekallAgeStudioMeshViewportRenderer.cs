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
    RekallAgeStudioMeshTransformGizmo? TransformGizmo,
    RekallAgeStudioViewportCamera Camera);

/// <summary>
/// Orbit camera state for the mesh-editing viewport. <see cref="Identity"/> reproduces the
/// engine's original fixed axonometric projection exactly (see
/// <see cref="RekallAgeStudioMeshViewportRenderer.DefaultRight"/>/<see cref="RekallAgeStudioMeshViewportRenderer.DefaultUp"/>);
/// <see cref="Yaw"/>/<see cref="Pitch"/> orbit relative to that default framing rather than to
/// world axes, so the well-tuned default view is always the starting point.
/// </summary>
internal readonly record struct RekallAgeStudioViewportCamera(double Yaw, double Pitch, double Zoom, double PanX, double PanY, bool Orthographic)
{
    public static RekallAgeStudioViewportCamera Identity { get; } = new(0, 0, 1, 0, 0, true);
}

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

    public BitmapSource RenderEmpty(int width, int height, RekallAgeStudioViewportCamera? camera = null)
    {
        if (width < 64 || width > 4096 || height < 64 || height > 4096)
            throw new ArgumentOutOfRangeException(nameof(width), "Mesh viewport dimensions must be 64-4096 pixels.");
        var activeCamera = camera ?? RekallAgeStudioViewportCamera.Identity;
        var center = new Point(width / 2d + activeCamera.PanX, height / 2d + activeCamera.PanY);
        var scale = 25 * activeCamera.Zoom;
        Point Screen(RekallAgeGeometryVector3 point)
        {
            var projected = Project(point, activeCamera);
            return new Point(center.X + projected.X * scale, center.Y + projected.Y * scale);
        }

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 16, 22)), null, new Rect(0, 0, width, height));
            for (var index = -12; index <= 12; index++)
            {
                var major = index % 5 == 0;
                var xPen = index == 0
                    ? new Pen(new SolidColorBrush(Color.FromRgb(92, 57, 61)), 1.4)
                    : new Pen(new SolidColorBrush(major ? Color.FromRgb(48, 57, 67) : Color.FromRgb(31, 38, 46)), 1);
                var zPen = index == 0
                    ? new Pen(new SolidColorBrush(Color.FromRgb(53, 94, 59)), 1.4)
                    : new Pen(new SolidColorBrush(major ? Color.FromRgb(48, 57, 67) : Color.FromRgb(31, 38, 46)), 1);
                drawing.DrawLine(xPen, Screen(new(-12, 0, index)), Screen(new(12, 0, index)));
                drawing.DrawLine(zPen, Screen(new(index, 0, -12)), Screen(new(index, 0, 12)));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public RekallAgeStudioMeshViewportFrame Render(
        RekallAgeMeshAsset mesh,
        RekallAgeGeometryDomain activeDomain,
        IReadOnlyCollection<ulong> selectedIds,
        int width,
        int height,
        bool preview,
        RekallAgeStudioViewportCamera? camera = null,
        RekallAgeStudioViewportRenderStyle style = RekallAgeStudioViewportRenderStyle.SmoothShaded)
    {
        ArgumentNullException.ThrowIfNull(mesh); ArgumentNullException.ThrowIfNull(selectedIds);
        if (width < 64 || width > 4096 || height < 64 || height > 4096) throw new ArgumentOutOfRangeException(nameof(width), "Mesh viewport dimensions must be 64-4096 pixels.");
        var activeCamera = camera ?? RekallAgeStudioViewportCamera.Identity;
        var raw = mesh.Topology.Positions.Select(point => Project(point, activeCamera)).ToArray();
        var minX = raw.Min(point => point.X); var maxX = raw.Max(point => point.X);
        var minY = raw.Min(point => point.Y); var maxY = raw.Max(point => point.Y);
        var spanX = Math.Max(maxX - minX, 1e-9); var spanY = Math.Max(maxY - minY, 1e-9);
        const double padding = 28;
        var scale = Math.Min((width - padding * 2) / spanX, (height - padding * 2) / spanY) * 0.55 * activeCamera.Zoom;
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var centerX = (minX + maxX) / 2; var centerY = (minY + maxY) / 2;
        var projected = raw.Select(point => new Point(
            width / 2d + (point.X - centerX) * scale + activeCamera.PanX,
            height / 2d + (point.Y - centerY) * scale + activeCamera.PanY)).ToArray();
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
                    GizmoAxis(RekallAgeStudioMeshTransformAxis.X, origin, activeCamera),
                    GizmoAxis(RekallAgeStudioMeshTransformAxis.Y, origin, activeCamera),
                    GizmoAxis(RekallAgeStudioMeshTransformAxis.Z, origin, activeCamera)
                });
            }
        }

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 16, 22)), null, new Rect(0, 0, width, height));
            var minorGrid = new Pen(new SolidColorBrush(Color.FromRgb(35, 42, 50)), 1);
            var majorGrid = new Pen(new SolidColorBrush(Color.FromRgb(48, 57, 67)), 1);
            for (var x = 40; x < width; x += 40)
                drawing.DrawLine(x % 200 == 0 ? majorGrid : minorGrid, new Point(x + 0.5, 0), new Point(x + 0.5, height));
            for (var y = 40; y < height; y += 40)
                drawing.DrawLine(y % 200 == 0 ? majorGrid : minorGrid, new Point(0, y + 0.5), new Point(width, y + 0.5));
            drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(91, 54, 58)), 1),
                new Point(0, height / 2d + 0.5), new Point(width, height / 2d + 0.5));
            drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52, 91, 57)), 1),
                new Point(width / 2d + 0.5, 0), new Point(width / 2d + 0.5, height));
            var faceBrush = new SolidColorBrush(style switch
            {
                RekallAgeStudioViewportRenderStyle.Textured => Color.FromArgb(225, 47, 78, 103),
                RekallAgeStudioViewportRenderStyle.FlatShaded => Color.FromArgb(255, 72, 91, 108),
                RekallAgeStudioViewportRenderStyle.Wireframe => Colors.Transparent,
                RekallAgeStudioViewportRenderStyle.Clay => Color.FromRgb(171, 157, 139),
                _ => preview ? Color.FromArgb(190, 57, 102, 112) : Color.FromArgb(205, 38, 67, 91)
            });
            var selectedFaceBrush = new SolidColorBrush(Color.FromArgb(205, 38, 157, 181));
            var outline = new Pen(new SolidColorBrush(style switch
            {
                RekallAgeStudioViewportRenderStyle.Wireframe => Color.FromRgb(221, 232, 244),
                RekallAgeStudioViewportRenderStyle.Clay => Color.FromRgb(111, 101, 91),
                RekallAgeStudioViewportRenderStyle.FlatShaded => Color.FromRgb(54, 66, 77),
                RekallAgeStudioViewportRenderStyle.Textured => Color.FromRgb(79, 132, 171),
                _ => Color.FromRgb(86, 108, 132)
            }), style == RekallAgeStudioViewportRenderStyle.Wireframe ? 2 : 1);
            foreach (var face in faces)
            {
                var brush = activeDomain == RekallAgeGeometryDomain.Face && selected.Contains(face.Id)
                    ? selectedFaceBrush
                    : style == RekallAgeStudioViewportRenderStyle.FlatShaded
                        ? new SolidColorBrush(FlatShade(face.Id))
                        : faceBrush;
                drawing.DrawGeometry(brush, outline, Polygon(face.Polygon));
                if (style == RekallAgeStudioViewportRenderStyle.Textured)
                {
                    var center = Center(face.Polygon);
                    drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(85, 89, 175, 196)), null, center, 7, 7);
                }
            }
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
        return new(bitmap, centers, preview, faces, edges, points, corners, scale, gizmo, activeCamera);
    }

    private static Color FlatShade(ulong faceId)
    {
        var step = (byte)(faceId % 4 * 13);
        return Color.FromRgb((byte)(77 + step), (byte)(91 + step), (byte)(104 + step));
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
        var projectedAxis = Project(AxisVector(gesture.Axis), frame.Camera);
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

    /// <summary>
    /// The original fixed axonometric view's screen-right basis vector — the exact coefficients
    /// of the legacy <c>(point.X - point.Z) / sqrt(2)</c> projection, expressed as a 3D axis so
    /// the identity camera (<see cref="RekallAgeStudioViewportCamera.Identity"/>) reproduces it.
    /// </summary>
    internal static readonly RekallAgeGeometryVector3 DefaultRight = new(1 / Math.Sqrt(2), 0, -1 / Math.Sqrt(2));

    /// <summary>The legacy projection's screen-up basis vector (the <c>(X + Z - 2Y) / sqrt(6)</c> coefficients).</summary>
    internal static readonly RekallAgeGeometryVector3 DefaultUp = new(1 / Math.Sqrt(6), -2 / Math.Sqrt(6), 1 / Math.Sqrt(6));

    /// <summary>The legacy view's line-of-sight axis, derived so (Right, Up, Forward) is orthonormal.</summary>
    internal static readonly RekallAgeGeometryVector3 DefaultForward = Cross(DefaultRight, DefaultUp);

    /// <summary>
    /// Pure camera-space projection: rotates <paramref name="point"/> by the camera's yaw/pitch
    /// and, in perspective mode, applies a depth divide using <see cref="RekallAgeStudioViewportCamera.Zoom"/>
    /// as the camera's distance from its pivot. Deliberately excludes <see cref="RekallAgeStudioViewportCamera.PanX"/>/<see cref="RekallAgeStudioViewportCamera.PanY"/>
    /// and does not scale orthographic output by <see cref="RekallAgeStudioViewportCamera.Zoom"/> —
    /// both are screen-space framing concerns applied by <see cref="Render"/> after auto-fit, so
    /// they are not silently cancelled out by auto-fit re-normalizing the projected bounds.
    /// </summary>
    internal static Point Project(RekallAgeGeometryVector3 point, RekallAgeStudioViewportCamera camera)
    {
        var (right, up, forward) = OrbitBasis(camera.Yaw, camera.Pitch);
        var x = Dot(point, right);
        var y = Dot(point, up);
        var depth = Dot(point, forward);
        var perspective = camera.Orthographic ? 1.0 : camera.Zoom / Math.Max(0.05, camera.Zoom - depth);
        return new Point(x * perspective, y * perspective);
    }

    /// <summary>
    /// Builds the camera's (right, up, forward) basis by orbiting the legacy default basis:
    /// yaw always turns around the world-vertical axis (Blender's default orbit convention,
    /// independent of the current pitch), then pitch turns around the resulting right axis.
    /// At yaw = pitch = 0 this returns the legacy basis unchanged.
    /// </summary>
    private static (RekallAgeGeometryVector3 Right, RekallAgeGeometryVector3 Up, RekallAgeGeometryVector3 Forward) OrbitBasis(double yaw, double pitch)
    {
        var worldUp = new RekallAgeGeometryVector3(0, 1, 0);
        var right = yaw == 0 ? DefaultRight : RotateAroundAxis(DefaultRight, worldUp, yaw);
        var forward = yaw == 0 ? DefaultForward : RotateAroundAxis(DefaultForward, worldUp, yaw);
        var up = DefaultUp;
        if (pitch != 0)
        {
            up = RotateAroundAxis(up, right, pitch);
            forward = RotateAroundAxis(forward, right, pitch);
        }
        return (right, up, forward);
    }

    /// <summary>Rodrigues' rotation formula: rotates <paramref name="v"/> by <paramref name="angle"/> radians around <paramref name="axis"/>.</summary>
    private static RekallAgeGeometryVector3 RotateAroundAxis(RekallAgeGeometryVector3 v, RekallAgeGeometryVector3 axis, double angle)
    {
        var a = Normalize(axis);
        var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        var dot = Dot(v, a);
        var cross = Cross(a, v);
        return new RekallAgeGeometryVector3(
            v.X * cos + cross.X * sin + a.X * dot * (1 - cos),
            v.Y * cos + cross.Y * sin + a.Y * dot * (1 - cos),
            v.Z * cos + cross.Z * sin + a.Z * dot * (1 - cos));
    }

    private static double Dot(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static RekallAgeGeometryVector3 Cross(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(
        a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static RekallAgeGeometryVector3 Normalize(RekallAgeGeometryVector3 v)
    {
        var length = Math.Sqrt(Dot(v, v));
        return length <= 1e-12 ? v : new(v.X / length, v.Y / length, v.Z / length);
    }

    private static RekallAgeGeometryVector3 AxisVector(RekallAgeStudioMeshTransformAxis axis) => axis switch
    {
        RekallAgeStudioMeshTransformAxis.X => new(1, 0, 0),
        RekallAgeStudioMeshTransformAxis.Y => new(0, 1, 0),
        RekallAgeStudioMeshTransformAxis.Z => new(0, 0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };
    private static RekallAgeStudioMeshTransformGizmoAxis GizmoAxis(RekallAgeStudioMeshTransformAxis axis, Point origin, RekallAgeStudioViewportCamera camera)
    {
        var projected = Project(AxisVector(axis), camera);
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
