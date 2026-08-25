using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeCurveOperations
{
    public RekallAgeEvaluatedCurve Line(
        RekallAgeGeometryVector3 start,
        RekallAgeGeometryVector3 end,
        double startRadius = 1,
        double endRadius = 1,
        double startTiltRadians = 0,
        double endTiltRadians = 0)
    {
        ValidateRadius(startRadius, nameof(startRadius));
        ValidateRadius(endRadius, nameof(endRadius));
        var tangent = Normalize(Subtract(end, start));
        return new([new(1, false,
        [
            new(start, tangent, startRadius, startTiltRadians, 1, 1, 2, 0),
            new(end, tangent, endRadius, endTiltRadians, 1, 1, 2, 1)
        ])]);
    }

    public RekallAgeEvaluatedCurve Circle(
        RekallAgeGeometryVector3 center,
        double radius,
        int segments = 32,
        string plane = "xy")
    {
        ValidateRadius(radius, nameof(radius));
        if (segments is < 3 or > 100_000) throw new ArgumentOutOfRangeException(nameof(segments));
        var points = new RekallAgeEvaluatedCurvePoint[segments];
        for (var index = 0; index < segments; index++)
        {
            var angle = Math.PI * 2 * index / segments;
            var nextAngle = Math.PI * 2 * (index + 1) / segments;
            var position = OnPlane(center, radius, angle, plane);
            var next = OnPlane(center, radius, nextAngle, plane);
            points[index] = new(position, Normalize(Subtract(next, position)), 1, 0, 1,
                (ulong)(index + 1), (ulong)((index + 1) % segments + 1), 0);
        }
        return new([new(1, true, points)]);
    }

    public RekallAgeEvaluatedCurve Reverse(RekallAgeEvaluatedCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return new(curve.Splines.Select(spline => new RekallAgeEvaluatedCurveSpline(
            spline.SourceSplineId,
            spline.Cyclic,
            spline.Points.Reverse().Select(point => point with
            {
                Tangent = Scale(point.Tangent, -1),
                SourceStartControlPointId = point.SourceEndControlPointId,
                SourceEndControlPointId = point.SourceStartControlPointId,
                SegmentT = 1 - point.SegmentT
            }).ToArray())).ToArray());
    }

    public RekallAgeEvaluatedCurve Resample(RekallAgeEvaluatedCurve curve, int pointCount)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (pointCount is < 2 or > 100_000) throw new ArgumentOutOfRangeException(nameof(pointCount));
        return new(curve.Splines.Select(spline => ResampleSpline(spline, pointCount)).ToArray());
    }

    private static RekallAgeEvaluatedCurveSpline ResampleSpline(RekallAgeEvaluatedCurveSpline spline, int pointCount)
    {
        if (spline.Points.Count < 2) throw new InvalidDataException("Curve resampling requires at least two evaluated points.");
        var spanCount = spline.Cyclic ? spline.Points.Count : spline.Points.Count - 1;
        var cumulative = new double[spanCount + 1];
        for (var span = 0; span < spanCount; span++)
        {
            var a = spline.Points[span];
            var b = spline.Points[(span + 1) % spline.Points.Count];
            cumulative[span + 1] = cumulative[span] + Length(Subtract(b.Position, a.Position));
        }
        var totalLength = cumulative[^1];
        if (totalLength <= 1e-12) throw new InvalidDataException("Curve resampling requires nonzero length.");
        var divisor = spline.Cyclic ? pointCount : pointCount - 1;
        var result = new RekallAgeEvaluatedCurvePoint[pointCount];
        for (var index = 0; index < pointCount; index++)
        {
            var distance = totalLength * index / divisor;
            var span = Math.Min(spanCount - 1, FindSpan(cumulative, distance));
            var start = spline.Points[span];
            var end = spline.Points[(span + 1) % spline.Points.Count];
            var spanLength = cumulative[span + 1] - cumulative[span];
            var t = spanLength <= 1e-12 ? 0 : (distance - cumulative[span]) / spanLength;
            result[index] = new(
                Lerp(start.Position, end.Position, t),
                Normalize(Subtract(end.Position, start.Position)),
                Lerp(start.Radius, end.Radius, t),
                Lerp(start.TiltRadians, end.TiltRadians, t),
                spline.SourceSplineId,
                start.SourceStartControlPointId,
                start.SourceEndControlPointId,
                Lerp(start.SegmentT, end.SegmentT, t));
        }
        return new(spline.SourceSplineId, spline.Cyclic, result);
    }

    private static int FindSpan(IReadOnlyList<double> cumulative, double distance)
    {
        var index = Array.BinarySearch(cumulative.ToArray(), distance);
        if (index >= 0) return Math.Min(index, cumulative.Count - 2);
        return Math.Max(0, ~index - 1);
    }

    private static RekallAgeGeometryVector3 OnPlane(RekallAgeGeometryVector3 center, double radius, double angle, string plane) =>
        plane.ToLowerInvariant() switch
        {
            "xy" => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle), center.Z),
            "xz" => new(center.X + radius * Math.Cos(angle), center.Y, center.Z + radius * Math.Sin(angle)),
            "yz" => new(center.X, center.Y + radius * Math.Cos(angle), center.Z + radius * Math.Sin(angle)),
            _ => throw new ArgumentException($"Curve plane '{plane}' is unsupported.", nameof(plane))
        };

    private static void ValidateRadius(double radius, string name)
    {
        if (!double.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(name);
    }

    private static double Length(RekallAgeGeometryVector3 value) => Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static RekallAgeGeometryVector3 Lerp(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b, double t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Z, b.Z, t));
    private static RekallAgeGeometryVector3 Subtract(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static RekallAgeGeometryVector3 Scale(RekallAgeGeometryVector3 value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
    private static RekallAgeGeometryVector3 Normalize(RekallAgeGeometryVector3 value)
    {
        var length = Length(value);
        if (length <= 1e-12 || !double.IsFinite(length)) throw new InvalidDataException("Curve contains a zero-length or non-finite span.");
        return Scale(value, 1 / length);
    }
}
