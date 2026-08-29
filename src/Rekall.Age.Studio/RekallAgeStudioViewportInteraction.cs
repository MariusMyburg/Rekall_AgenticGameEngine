using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Studio;

internal readonly record struct RekallAgeStudioViewportPoint(double X, double Y);

internal enum RekallAgeStudioViewportRegionKind
{
    World,
    Ui
}

internal sealed record RekallAgeStudioViewportPickRegion(
    string EntityId,
    RekallAgeStudioViewportRegionKind Kind,
    double X,
    double Y,
    double Width,
    double Height,
    double Depth,
    int SortKey,
    IReadOnlyList<RekallAgeStudioViewportPoint>? Vertices = null,
    IReadOnlyList<uint>? Indices = null)
{
    public bool Contains(double x, double y)
    {
        if (x < X || x > X + Width || y < Y || y > Y + Height)
        {
            return false;
        }
        if (Vertices is null || Indices is null || Indices.Count < 3)
        {
            return true;
        }

        for (var index = 0; index + 2 < Indices.Count; index += 3)
        {
            if (Indices[index] >= Vertices.Count || Indices[index + 1] >= Vertices.Count || Indices[index + 2] >= Vertices.Count)
            {
                continue;
            }
            if (PointInTriangle(new(x, y), Vertices[(int)Indices[index]], Vertices[(int)Indices[index + 1]], Vertices[(int)Indices[index + 2]]))
            {
                return true;
            }
        }
        return false;
    }

    private static bool PointInTriangle(
        RekallAgeStudioViewportPoint point,
        RekallAgeStudioViewportPoint a,
        RekallAgeStudioViewportPoint b,
        RekallAgeStudioViewportPoint c)
    {
        static double Sign(RekallAgeStudioViewportPoint p1, RekallAgeStudioViewportPoint p2, RekallAgeStudioViewportPoint p3) =>
            (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        var d1 = Sign(point, a, b);
        var d2 = Sign(point, b, c);
        var d3 = Sign(point, c, a);
        const double epsilon = 0.000001;
        return !((d1 < -epsilon || d2 < -epsilon || d3 < -epsilon)
            && (d1 > epsilon || d2 > epsilon || d3 > epsilon));
    }
}

internal sealed record RekallAgeStudioViewportInteractionSnapshot(
    int FrameWidth,
    int FrameHeight,
    IReadOnlyList<RekallAgeStudioViewportPickRegion> Regions)
{
    public RekallAgeStudioViewportPoint? MapDisplayPoint(
        double displayWidth,
        double displayHeight,
        double displayX,
        double displayY)
    {
        if (FrameWidth <= 0 || FrameHeight <= 0
            || !double.IsFinite(displayWidth) || !double.IsFinite(displayHeight)
            || !double.IsFinite(displayX) || !double.IsFinite(displayY)
            || displayWidth <= 0 || displayHeight <= 0)
        {
            return null;
        }

        var scale = Math.Min(displayWidth / FrameWidth, displayHeight / FrameHeight);
        var renderedWidth = FrameWidth * scale;
        var renderedHeight = FrameHeight * scale;
        var offsetX = (displayWidth - renderedWidth) * 0.5;
        var offsetY = (displayHeight - renderedHeight) * 0.5;
        if (displayX < offsetX || displayX > offsetX + renderedWidth
            || displayY < offsetY || displayY > offsetY + renderedHeight)
        {
            return null;
        }

        return new(
            Math.Clamp((displayX - offsetX) / scale, 0, FrameWidth - 1),
            Math.Clamp((displayY - offsetY) / scale, 0, FrameHeight - 1));
    }

    public string? Pick(double frameX, double frameY) => Regions
        .Where(region => region.Contains(frameX, frameY))
        .OrderByDescending(region => region.Kind)
        .ThenByDescending(region => region.Kind == RekallAgeStudioViewportRegionKind.Ui ? region.SortKey : int.MinValue)
        .ThenBy(region => region.Depth)
        .ThenBy(region => region.Width * region.Height)
        .ThenBy(region => region.EntityId, StringComparer.Ordinal)
        .Select(region => region.EntityId)
        .FirstOrDefault();
}

internal static class RekallAgeStudioViewportInteractionBuilder
{
    public static RekallAgeStudioViewportInteractionSnapshot Build(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeRuntimeEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(entities);
        var visibleEntities = entities
            .Where(entity => entity.Visible)
            .ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var regions = new List<RekallAgeStudioViewportPickRegion>();
        foreach (var renderable in frame.Renderables)
        {
            if (!visibleEntities.ContainsKey(renderable.EntityId))
            {
                continue;
            }

            if (renderable.UiVisual is { } ui)
            {
                var left = Math.Max(ui.X, ui.ClipX);
                var top = Math.Max(ui.Y, ui.ClipY);
                var right = Math.Min(ui.X + ui.Width, ui.ClipX + ui.ClipWidth);
                var bottom = Math.Min(ui.Y + ui.Height, ui.ClipY + ui.ClipHeight);
                if (right > left && bottom > top)
                {
                    regions.Add(new(renderable.EntityId, RekallAgeStudioViewportRegionKind.Ui,
                        left, top, right - left, bottom - top, 0, renderable.SortKey));
                }
                continue;
            }

            var geometryRegion = Project2DGeometry(frame, renderable);
            if (geometryRegion is not null)
            {
                regions.Add(geometryRegion);
                continue;
            }

            var projection = Project(frame, renderable);
            if (projection is null)
            {
                continue;
            }
            var (x, y, depth, radius) = projection.Value;
            regions.Add(new(renderable.EntityId, RekallAgeStudioViewportRegionKind.World,
                x - radius, y - radius, radius * 2, radius * 2, depth, renderable.SortKey));
        }

        return new(frame.Width, frame.Height, regions);
    }

    private static RekallAgeStudioViewportPickRegion? Project2DGeometry(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable)
    {
        var camera = frame.ActiveCamera;
        var mesh = renderable.GeometryMesh;
        if (!renderable.Kind.Equals("mesh", StringComparison.Ordinal)
            || camera is null
            || !camera.Kind.Equals("Camera2D", StringComparison.OrdinalIgnoreCase)
            || mesh is null
            || mesh.Vertices.Count == 0
            || mesh.Indices.Count < 3)
        {
            return null;
        }

        var rect = RekallAgeRuntimeViewportCameraRect.FromFrame(frame);
        var pixelsPerUnit = rect.Height / Math.Max(0.001, camera.OrthographicSize);
        var cameraRadians = ToRadians(camera.RotationZ);
        var objectRadians = ToRadians(renderable.RotationZ);
        var projected = mesh.Vertices.Select(vertex =>
        {
            var localX = vertex.X * renderable.ScaleX;
            var localY = vertex.Y * renderable.ScaleY;
            var rotatedX = localX * Math.Cos(objectRadians) - localY * Math.Sin(objectRadians);
            var rotatedY = localX * Math.Sin(objectRadians) + localY * Math.Cos(objectRadians);
            var deltaX = renderable.X + rotatedX - camera.X;
            var deltaY = renderable.Y + rotatedY - camera.Y;
            var cameraX = deltaX * Math.Cos(cameraRadians) + deltaY * Math.Sin(cameraRadians);
            var cameraY = -deltaX * Math.Sin(cameraRadians) + deltaY * Math.Cos(cameraRadians);
            return new RekallAgeStudioViewportPoint(
                rect.X + rect.Width * 0.5 + cameraX * pixelsPerUnit,
                rect.Y + rect.Height * 0.5 - cameraY * pixelsPerUnit);
        }).ToArray();
        var left = projected.Min(point => point.X);
        var right = projected.Max(point => point.X);
        var top = projected.Min(point => point.Y);
        var bottom = projected.Max(point => point.Y);
        var viewportRight = rect.X + rect.Width;
        var viewportBottom = rect.Y + rect.Height;
        if (right < rect.X || bottom < rect.Y || left > viewportRight || top > viewportBottom)
        {
            return null;
        }
        left = Math.Max(left, rect.X);
        right = Math.Min(right, viewportRight);
        top = Math.Max(top, rect.Y);
        bottom = Math.Min(bottom, viewportBottom);

        return new(
            renderable.EntityId,
            RekallAgeStudioViewportRegionKind.World,
            left,
            top,
            Math.Max(0.001, right - left),
            Math.Max(0.001, bottom - top),
            renderable.Z - camera.Z,
            renderable.SortKey,
            projected,
            mesh.Indices);
    }

    private static (double X, double Y, double Depth, double Radius)? Project(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable)
    {
        var camera = frame.ActiveCamera;
        if (renderable.Kind.Equals("mesh", StringComparison.Ordinal)
            && camera is not null
            && camera.Kind.Equals("Camera2D", StringComparison.OrdinalIgnoreCase))
        {
            var rect = RekallAgeRuntimeViewportCameraRect.FromFrame(frame);
            var pixelsPerUnit = rect.Height / Math.Max(0.001, camera.OrthographicSize);
            var radians = ToRadians(camera.RotationZ);
            var deltaX = renderable.X - camera.X;
            var deltaY = renderable.Y - camera.Y;
            var cameraX = deltaX * Math.Cos(radians) + deltaY * Math.Sin(radians);
            var cameraY = -deltaX * Math.Sin(radians) + deltaY * Math.Cos(radians);
            var x = rect.X + rect.Width * 0.5 + cameraX * pixelsPerUnit;
            var y = rect.Y + rect.Height * 0.5 - cameraY * pixelsPerUnit;
            var extent = Math.Max(0.25, Math.Max(Math.Abs(renderable.ScaleX), Math.Abs(renderable.ScaleY)));
            var radius = Math.Clamp(pixelsPerUnit * extent * 0.6, 7,
                Math.Max(7, Math.Min(frame.Width, frame.Height) * 0.25));
            return x < -radius || y < -radius || x > frame.Width + radius || y > frame.Height + radius
                ? null
                : (x, y, renderable.Z - camera.Z, radius);
        }

        if (renderable.Kind.Equals("mesh", StringComparison.Ordinal)
            && camera is not null
            && camera.Kind.Equals("Camera3D", StringComparison.OrdinalIgnoreCase)
            && !IsDefaultCameraPose(camera))
        {
            var delta = new Vector3(renderable.X - camera.X, renderable.Y - camera.Y, renderable.Z - camera.Z);
            var forward = Normalize(Rotate(new(0, 0, 1), camera.RotationX, camera.RotationY, camera.RotationZ));
            var right = Normalize(Rotate(new(1, 0, 0), camera.RotationX, camera.RotationY, camera.RotationZ));
            var up = Normalize(Rotate(new(0, 1, 0), camera.RotationX, camera.RotationY, camera.RotationZ));
            var cameraX = Dot(delta, right);
            var cameraY = Dot(delta, up);
            var depth = Dot(delta, forward);
            var near = Math.Max(0.001, camera.NearClip);
            var far = Math.Max(near + 0.001, camera.FarClip);
            if (!double.IsFinite(depth) || depth <= near || depth > far)
            {
                return null;
            }
            var rect = RekallAgeRuntimeViewportCameraRect.FromFrame(frame);
            double x;
            double y;
            double pixelsPerUnit;
            if (camera.ProjectionMode.Equals("orthographic", StringComparison.OrdinalIgnoreCase))
            {
                pixelsPerUnit = rect.Height / Math.Max(0.001, camera.OrthographicSize);
                x = rect.X + rect.Width * 0.5 + cameraX * pixelsPerUnit;
                y = rect.Y + rect.Height * 0.5 - cameraY * pixelsPerUnit;
            }
            else
            {
                var focal = rect.Height / (2 * Math.Tan(ToRadians(Math.Clamp(camera.FieldOfViewDegrees, 1, 179)) * 0.5));
                pixelsPerUnit = focal / depth;
                x = rect.X + rect.Width * 0.5 + cameraX * pixelsPerUnit;
                y = rect.Y + rect.Height * 0.5 - cameraY * pixelsPerUnit;
            }
            var extent = Math.Max(0.25, Math.Max(Math.Abs(renderable.ScaleX), Math.Max(Math.Abs(renderable.ScaleY), Math.Abs(renderable.ScaleZ))));
            var radius = Math.Clamp(pixelsPerUnit * extent * 0.6, 7, Math.Max(7, Math.Min(frame.Width, frame.Height) * 0.25));
            return x < -radius || y < -radius || x > frame.Width + radius || y > frame.Height + radius
                ? null
                : (x, y, depth, radius);
        }

        double fallbackX;
        double fallbackY;
        if (renderable.Kind.Equals("mesh", StringComparison.Ordinal))
        {
            fallbackX = Math.Clamp(frame.Width * 0.5 + renderable.X * 18, 16, Math.Max(16, frame.Width - 17));
            fallbackY = Math.Clamp(frame.Height * 0.5 - renderable.Y * 18, 16, Math.Max(16, frame.Height - 17));
        }
        else
        {
            var seed = Math.Abs(renderable.EntityId.GetHashCode(StringComparison.Ordinal));
            fallbackX = 12 + (seed + (int)Math.Round(renderable.X * 7)) % Math.Max(1, frame.Width - 24);
            fallbackY = 16 + (seed / 17 + (int)Math.Round(renderable.Y * 7)) % Math.Max(1, frame.Height - 28);
        }
        var fallbackRadius = Math.Max(8, Math.Min(frame.Width, frame.Height) * 0.09
            * Math.Max(0.25, Math.Max(Math.Abs(renderable.ScaleX), Math.Abs(renderable.ScaleY))));
        return (fallbackX, fallbackY, -renderable.Z, fallbackRadius);
    }

    private static bool IsDefaultCameraPose(RekallAgeRuntimeViewportCamera camera) =>
        Math.Abs(camera.X) < 0.0001 && Math.Abs(camera.Y) < 0.0001 && Math.Abs(camera.Z) < 0.0001
        && Math.Abs(camera.RotationX) < 0.0001 && Math.Abs(camera.RotationY) < 0.0001 && Math.Abs(camera.RotationZ) < 0.0001;

    private static double ToRadians(double value) => value * Math.PI / 180.0;
    private static double Dot(Vector3 left, Vector3 right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;
    private static Vector3 Normalize(Vector3 value)
    {
        var length = Math.Sqrt(Dot(value, value));
        return length <= 0.0000001 ? value : new(value.X / length, value.Y / length, value.Z / length);
    }

    private static Vector3 Rotate(Vector3 point, double pitchDegrees, double yawDegrees, double rollDegrees)
    {
        var pitch = ToRadians(pitchDegrees); var yaw = ToRadians(yawDegrees); var roll = ToRadians(rollDegrees);
        var x1 = point.X;
        var y1 = point.Y * Math.Cos(pitch) - point.Z * Math.Sin(pitch);
        var z1 = point.Y * Math.Sin(pitch) + point.Z * Math.Cos(pitch);
        var x2 = x1 * Math.Cos(yaw) + z1 * Math.Sin(yaw);
        var y2 = y1;
        var z2 = -x1 * Math.Sin(yaw) + z1 * Math.Cos(yaw);
        return new(x2 * Math.Cos(roll) - y2 * Math.Sin(roll), x2 * Math.Sin(roll) + y2 * Math.Cos(roll), z2);
    }

    private readonly record struct Vector3(double X, double Y, double Z);
}
