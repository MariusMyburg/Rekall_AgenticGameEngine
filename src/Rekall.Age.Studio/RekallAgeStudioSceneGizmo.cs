namespace Rekall.Age.Studio;

using Rekall.Age.Rendering.Abstractions;

public enum RekallAgeStudioTransformTool
{
    Select,
    Move,
    Rotate,
    Scale
}

public enum RekallAgeStudioTransformAxis
{
    X,
    Y,
    Z
}

public enum RekallAgeStudioTransformSpace
{
    World,
    Local
}

public sealed record RekallAgeStudioGizmoDisplayLine(
    RekallAgeStudioTransformAxis Axis,
    double X1,
    double Y1,
    double X2,
    double Y2);

internal sealed record RekallAgeStudioTransformUpdate(
    string ComponentType,
    string PropertyName,
    double Value);

internal sealed record RekallAgeStudioGizmoHandle(
    RekallAgeStudioTransformAxis Axis,
    RekallAgeStudioViewportPoint Start,
    RekallAgeStudioViewportPoint End);

internal sealed class RekallAgeStudioSceneGizmo
{
    private const double HandleLength = 60;
    private const double HitRadius = 7;

    private RekallAgeStudioSceneGizmo(string entityId, RekallAgeStudioViewportPoint origin)
    {
        EntityId = entityId;
        Origin = origin;
        Handles =
        [
            new(RekallAgeStudioTransformAxis.X, origin, new(origin.X + HandleLength, origin.Y)),
            new(RekallAgeStudioTransformAxis.Y, origin, new(origin.X, origin.Y - HandleLength)),
            new(RekallAgeStudioTransformAxis.Z, origin, new(origin.X + 42, origin.Y - 42))
        ];
    }

    public string EntityId { get; }

    public RekallAgeStudioViewportPoint Origin { get; }

    public IReadOnlyList<RekallAgeStudioGizmoHandle> Handles { get; }

    public static RekallAgeStudioSceneGizmo? Create(
        RekallAgeStudioViewportInteractionSnapshot snapshot,
        string entityId,
        bool locked)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        if (locked)
        {
            return null;
        }

        var region = snapshot.Regions
            .Where(candidate => string.Equals(candidate.EntityId, entityId, StringComparison.Ordinal))
            .OrderBy(candidate => candidate.Kind)
            .ThenBy(candidate => candidate.SortKey)
            .FirstOrDefault();
        return region is null
            ? null
            : new(entityId, new(region.X + (region.Width / 2), region.Y + (region.Height / 2)));
    }

    public RekallAgeStudioTransformAxis? HitTest(double x, double y)
    {
        var hit = Handles
            .Select(handle => new { handle.Axis, Distance = DistanceToSegment(x, y, handle.Start, handle.End) })
            .Where(candidate => candidate.Distance <= HitRadius)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Axis)
            .FirstOrDefault();
        return hit?.Axis;
    }

    public RekallAgeStudioTransformGesture Begin(
        RekallAgeStudioTransformTool tool,
        RekallAgeStudioTransformAxis axis,
        double startX,
        double startY,
        double initialValue,
        double snap)
    {
        if (tool is RekallAgeStudioTransformTool.Select)
        {
            throw new ArgumentOutOfRangeException(nameof(tool), tool, "Select does not create a transform gesture.");
        }

        if (!double.IsFinite(startX) || !double.IsFinite(startY) || !double.IsFinite(initialValue))
        {
            throw new ArgumentOutOfRangeException(nameof(startX), "Transform gesture values must be finite.");
        }

        if (!double.IsFinite(snap) || snap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(snap));
        }

        return new(tool, axis, startX, startY, initialValue, snap);
    }

    private static double DistanceToSegment(
        double x,
        double y,
        RekallAgeStudioViewportPoint start,
        RekallAgeStudioViewportPoint end)
    {
        var vx = end.X - start.X;
        var vy = end.Y - start.Y;
        var lengthSquared = (vx * vx) + (vy * vy);
        var t = lengthSquared <= double.Epsilon
            ? 0
            : Math.Clamp((((x - start.X) * vx) + ((y - start.Y) * vy)) / lengthSquared, 0, 1);
        var closestX = start.X + (t * vx);
        var closestY = start.Y + (t * vy);
        return Math.Sqrt(Math.Pow(x - closestX, 2) + Math.Pow(y - closestY, 2));
    }
}

internal sealed class RekallAgeStudioTransformGesture
{
    internal RekallAgeStudioTransformGesture(
        RekallAgeStudioTransformTool tool,
        RekallAgeStudioTransformAxis axis,
        double startX,
        double startY,
        double initialValue,
        double snap)
    {
        Tool = tool;
        Axis = axis;
        StartX = startX;
        StartY = startY;
        InitialValue = initialValue;
        Snap = snap;
    }

    public RekallAgeStudioTransformTool Tool { get; }

    public RekallAgeStudioTransformAxis Axis { get; }

    public double StartX { get; }

    public double StartY { get; }

    public double InitialValue { get; }

    public double Snap { get; }

    public RekallAgeStudioTransformUpdate Update(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Transform gesture values must be finite.");
        }

        var deltaX = x - StartX;
        var deltaY = y - StartY;
        var projectedPixels = Axis switch
        {
            RekallAgeStudioTransformAxis.X => deltaX,
            RekallAgeStudioTransformAxis.Y => -deltaY,
            RekallAgeStudioTransformAxis.Z => (deltaX - deltaY) / 2,
            _ => throw new ArgumentOutOfRangeException()
        };
        var sensitivity = Tool switch
        {
            RekallAgeStudioTransformTool.Move => 0.02,
            RekallAgeStudioTransformTool.Rotate => 0.5,
            RekallAgeStudioTransformTool.Scale => 0.01,
            _ => throw new ArgumentOutOfRangeException()
        };
        var delta = projectedPixels * sensitivity;
        if (Snap > 0)
        {
            delta = Math.Round(delta / Snap, MidpointRounding.AwayFromZero) * Snap;
        }

        var value = InitialValue + delta;
        if (Tool is RekallAgeStudioTransformTool.Scale)
        {
            value = Math.Max(0.001, value);
        }

        return new("Rekall.Transform3D", PropertyName(Tool, Axis), value);
    }

    private static string PropertyName(RekallAgeStudioTransformTool tool, RekallAgeStudioTransformAxis axis) =>
        (tool, axis) switch
        {
            (RekallAgeStudioTransformTool.Move, RekallAgeStudioTransformAxis.X) => "x",
            (RekallAgeStudioTransformTool.Move, RekallAgeStudioTransformAxis.Y) => "y",
            (RekallAgeStudioTransformTool.Move, RekallAgeStudioTransformAxis.Z) => "z",
            (RekallAgeStudioTransformTool.Rotate, RekallAgeStudioTransformAxis.X) => "pitch",
            (RekallAgeStudioTransformTool.Rotate, RekallAgeStudioTransformAxis.Y) => "yaw",
            (RekallAgeStudioTransformTool.Rotate, RekallAgeStudioTransformAxis.Z) => "roll",
            (RekallAgeStudioTransformTool.Scale, RekallAgeStudioTransformAxis.X) => "scaleX",
            (RekallAgeStudioTransformTool.Scale, RekallAgeStudioTransformAxis.Y) => "scaleY",
            (RekallAgeStudioTransformTool.Scale, RekallAgeStudioTransformAxis.Z) => "scaleZ",
            _ => throw new ArgumentOutOfRangeException()
        };
}

internal static class RekallAgeStudioSceneGizmoRenderables
{
    private const double AxisLength = 1.6;
    private const double Thickness = 0.075;

    public static IReadOnlyList<RekallAgeRuntimeViewportRenderable> Create(
        RekallAgeStudioTransformTool tool,
        RekallAgeStudioTransformSpace space,
        double x,
        double y,
        double z,
        double pitch,
        double yaw,
        double roll)
    {
        if (tool is RekallAgeStudioTransformTool.Select) return [];
        var rotationX = space is RekallAgeStudioTransformSpace.Local ? pitch : 0;
        var rotationY = space is RekallAgeStudioTransformSpace.Local ? yaw : 0;
        var rotationZ = space is RekallAgeStudioTransformSpace.Local ? roll : 0;
        return
        [
            Axis(RekallAgeStudioTransformAxis.X, "#ef4c56", tool, x, y, z, rotationX, rotationY, rotationZ),
            Axis(RekallAgeStudioTransformAxis.Y, "#53d175", tool, x, y, z, rotationX, rotationY, rotationZ),
            Axis(RekallAgeStudioTransformAxis.Z, "#4e91ff", tool, x, y, z, rotationX, rotationY, rotationZ)
        ];
    }

    private static RekallAgeRuntimeViewportRenderable Axis(
        RekallAgeStudioTransformAxis axis,
        string color,
        RekallAgeStudioTransformTool tool,
        double x,
        double y,
        double z,
        double rotationX,
        double rotationY,
        double rotationZ)
    {
        var segments = tool is RekallAgeStudioTransformTool.Rotate
            ? Ring(axis)
            : Straight(axis, tool is RekallAgeStudioTransformTool.Scale);
        return new RekallAgeRuntimeViewportRenderable(
            $"__studio_gizmo_{axis.ToString().ToLowerInvariant()}",
            $"Studio {tool} {axis}",
            "mesh",
            null,
            x,
            y,
            z,
            int.MaxValue - (int)axis,
            RotationX: rotationX,
            RotationY: rotationY,
            RotationZ: rotationZ,
            MaterialColor: color,
            EmissiveColor: color,
            EmissiveStrength: 4,
            LineSegments: new RekallAgeRuntimeViewportLineSegments(segments, Thickness),
            Layer: "studio-editor")
        {
            CastShadows = false,
            ReceiveShadows = false
        };
    }

    private static IReadOnlyList<RekallAgeRuntimeViewportLineSegment> Straight(
        RekallAgeStudioTransformAxis axis,
        bool scaleHandle)
    {
        var end = axis switch
        {
            RekallAgeStudioTransformAxis.X => (X: AxisLength, Y: 0d, Z: 0d),
            RekallAgeStudioTransformAxis.Y => (X: 0d, Y: AxisLength, Z: 0d),
            _ => (X: 0d, Y: 0d, Z: AxisLength)
        };
        var segments = new List<RekallAgeRuntimeViewportLineSegment>
        {
            new(0, 0, 0, end.X, end.Y, end.Z)
        };
        var size = scaleHandle ? 0.13 : 0.18;
        segments.Add(new(end.X - size, end.Y, end.Z, end.X, end.Y, end.Z));
        segments.Add(new(end.X, end.Y - size, end.Z, end.X, end.Y, end.Z));
        segments.Add(new(end.X, end.Y, end.Z - size, end.X, end.Y, end.Z));
        return segments;
    }

    private static IReadOnlyList<RekallAgeRuntimeViewportLineSegment> Ring(RekallAgeStudioTransformAxis axis)
    {
        const int steps = 48;
        var segments = new RekallAgeRuntimeViewportLineSegment[steps];
        for (var index = 0; index < steps; index++)
        {
            var a = index * Math.Tau / steps;
            var b = (index + 1) * Math.Tau / steps;
            var from = RingPoint(axis, a);
            var to = RingPoint(axis, b);
            segments[index] = new(from.X, from.Y, from.Z, to.X, to.Y, to.Z);
        }
        return segments;
    }

    private static (double X, double Y, double Z) RingPoint(RekallAgeStudioTransformAxis axis, double angle) => axis switch
    {
        RekallAgeStudioTransformAxis.X => (0, Math.Cos(angle) * AxisLength, Math.Sin(angle) * AxisLength),
        RekallAgeStudioTransformAxis.Y => (Math.Cos(angle) * AxisLength, 0, Math.Sin(angle) * AxisLength),
        _ => (Math.Cos(angle) * AxisLength, Math.Sin(angle) * AxisLength, 0)
    };
}
