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

    public RekallAgeEvaluatedCurve Trim(RekallAgeEvaluatedCurve curve, double start, double end)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end > 1 || start >= end)
            throw new ArgumentOutOfRangeException(nameof(start), "Normalized trim range must satisfy 0 <= start < end <= 1.");
        return new(curve.Splines.Select(spline => TrimSpline(spline, start, end)).ToArray());
    }

    public RekallAgeEvaluatedCurve Join(IReadOnlyList<RekallAgeEvaluatedCurve> curves, double tolerance = 0.0001)
    {
        ArgumentNullException.ThrowIfNull(curves);
        if (curves.Count < 2) throw new ArgumentException("Curve join requires at least two inputs.", nameof(curves));
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var splines = curves.SelectMany(curve => curve.Splines).ToArray();
        if (splines.Length != curves.Count || splines.Any(spline => spline.Cyclic))
            throw new InvalidDataException("Curve join currently requires exactly one open spline per input.");
        var joined = splines[0].Points.ToList();
        for (var index = 1; index < splines.Length; index++)
        {
            var next = splines[index];
            var forwardDistance = Length(Subtract(next.Points[0].Position, joined[^1].Position));
            var reverseDistance = Length(Subtract(next.Points[^1].Position, joined[^1].Position));
            var points = reverseDistance < forwardDistance
                ? Reverse(new([next])).Splines[0].Points
                : next.Points;
            var distance = Math.Min(forwardDistance, reverseDistance);
            if (distance > tolerance)
                throw new InvalidDataException($"Curve join endpoints are {distance:F6} units apart, exceeding tolerance {tolerance:F6}.");
            joined.AddRange(points.Skip(1));
        }
        return new([WithTangents(new(splines[0].SourceSplineId, false, joined))]);
    }

    public RekallAgeEvaluatedCurve Fillet(RekallAgeEvaluatedCurve curve, double radius, int segments = 4)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ValidateRadius(radius, nameof(radius));
        if (segments is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(segments));
        return new(curve.Splines.Select(spline => FilletSpline(spline, radius, segments)).ToArray());
    }

    private static RekallAgeEvaluatedCurveSpline TrimSpline(RekallAgeEvaluatedCurveSpline spline, double start, double end)
    {
        var (cumulative, spanCount, totalLength) = Measure(spline);
        var startDistance = totalLength * start;
        var endDistance = totalLength * end;
        var points = new List<RekallAgeEvaluatedCurvePoint> { SampleAtDistance(spline, cumulative, spanCount, startDistance) };
        for (var index = 1; index < spline.Points.Count; index++)
        {
            if (cumulative[index] > startDistance + 1e-9 && cumulative[index] < endDistance - 1e-9)
                points.Add(spline.Points[index]);
        }
        points.Add(SampleAtDistance(spline, cumulative, spanCount, endDistance));
        return WithTangents(new(spline.SourceSplineId, false, RemoveCoincident(points)));
    }

    private static RekallAgeEvaluatedCurveSpline FilletSpline(RekallAgeEvaluatedCurveSpline spline, double radius, int segments)
    {
        if (spline.Points.Count < (spline.Cyclic ? 3 : 2)) throw new InvalidDataException("Curve fillet requires a valid evaluated spline.");
        var result = new List<RekallAgeEvaluatedCurvePoint>();
        if (!spline.Cyclic) result.Add(spline.Points[0]);
        var firstCorner = spline.Cyclic ? 0 : 1;
        var lastCorner = spline.Cyclic ? spline.Points.Count - 1 : spline.Points.Count - 2;
        for (var index = firstCorner; index <= lastCorner; index++)
        {
            var previous = spline.Points[(index - 1 + spline.Points.Count) % spline.Points.Count];
            var corner = spline.Points[index];
            var next = spline.Points[(index + 1) % spline.Points.Count];
            var toPrevious = Subtract(previous.Position, corner.Position);
            var toNext = Subtract(next.Position, corner.Position);
            var previousLength = Length(toPrevious);
            var nextLength = Length(toNext);
            if (previousLength <= 1e-9 || nextLength <= 1e-9) throw new InvalidDataException("Curve fillet encountered coincident points.");
            var previousDirection = Scale(toPrevious, 1 / previousLength);
            var nextDirection = Scale(toNext, 1 / nextLength);
            var angle = Math.Acos(Math.Clamp(Dot(previousDirection, nextDirection), -1, 1));
            if (Math.Abs(Math.PI - angle) <= 1e-5)
            {
                result.Add(corner);
                continue;
            }
            var tangentDistance = Math.Min(radius / Math.Max(Math.Tan(angle / 2), 1e-6), Math.Min(previousLength, nextLength) * 0.49);
            var entry = LerpPoint(corner, previous, tangentDistance / previousLength);
            var exit = LerpPoint(corner, next, tangentDistance / nextLength);
            result.Add(entry);
            for (var step = 1; step <= segments; step++)
            {
                var t = step / (double)segments;
                var inverse = 1 - t;
                var position = Add(Scale(entry.Position, inverse * inverse), Scale(corner.Position, 2 * inverse * t), Scale(exit.Position, t * t));
                result.Add(corner with
                {
                    Position = position,
                    Radius = Lerp(entry.Radius, exit.Radius, t),
                    TiltRadians = Lerp(entry.TiltRadians, exit.TiltRadians, t),
                    SegmentT = Lerp(entry.SegmentT, exit.SegmentT, t)
                });
            }
        }
        if (!spline.Cyclic) result.Add(spline.Points[^1]);
        return WithTangents(new(spline.SourceSplineId, spline.Cyclic, RemoveCoincident(result)));
    }

    private static RekallAgeEvaluatedCurveSpline ResampleSpline(RekallAgeEvaluatedCurveSpline spline, int pointCount)
    {
        if (spline.Points.Count < 2) throw new InvalidDataException("Curve resampling requires at least two evaluated points.");
        var (cumulative, spanCount, totalLength) = Measure(spline);
        var divisor = spline.Cyclic ? pointCount : pointCount - 1;
        var result = new RekallAgeEvaluatedCurvePoint[pointCount];
        for (var index = 0; index < pointCount; index++)
        {
            var distance = totalLength * index / divisor;
            result[index] = SampleAtDistance(spline, cumulative, spanCount, distance);
        }
        return new(spline.SourceSplineId, spline.Cyclic, result);
    }

    private static (double[] Cumulative, int SpanCount, double TotalLength) Measure(RekallAgeEvaluatedCurveSpline spline)
    {
        var spanCount = spline.Cyclic ? spline.Points.Count : spline.Points.Count - 1;
        var cumulative = new double[spanCount + 1];
        for (var span = 0; span < spanCount; span++)
        {
            var a = spline.Points[span];
            var b = spline.Points[(span + 1) % spline.Points.Count];
            cumulative[span + 1] = cumulative[span] + Length(Subtract(b.Position, a.Position));
        }
        if (cumulative[^1] <= 1e-12) throw new InvalidDataException("Curve operation requires nonzero length.");
        return (cumulative, spanCount, cumulative[^1]);
    }

    private static RekallAgeEvaluatedCurvePoint SampleAtDistance(RekallAgeEvaluatedCurveSpline spline, IReadOnlyList<double> cumulative, int spanCount, double distance)
    {
        var span = Math.Min(spanCount - 1, FindSpan(cumulative, distance));
        var start = spline.Points[span];
        var end = spline.Points[(span + 1) % spline.Points.Count];
        var spanLength = cumulative[span + 1] - cumulative[span];
        var t = spanLength <= 1e-12 ? 0 : (distance - cumulative[span]) / spanLength;
        return new(
            Lerp(start.Position, end.Position, t),
            Normalize(Subtract(end.Position, start.Position)),
            Lerp(start.Radius, end.Radius, t),
            Lerp(start.TiltRadians, end.TiltRadians, t),
            spline.SourceSplineId,
            start.SourceStartControlPointId,
            start.SourceEndControlPointId,
            Lerp(start.SegmentT, end.SegmentT, t));
    }

    private static RekallAgeEvaluatedCurveSpline WithTangents(RekallAgeEvaluatedCurveSpline spline)
    {
        var points = spline.Points.Select((point, index) =>
        {
            var previous = spline.Points[index == 0 ? (spline.Cyclic ? spline.Points.Count - 1 : 0) : index - 1];
            var next = spline.Points[index == spline.Points.Count - 1 ? (spline.Cyclic ? 0 : index) : index + 1];
            var delta = Subtract(next.Position, previous.Position);
            if (Length(delta) <= 1e-12)
                delta = index + 1 < spline.Points.Count ? Subtract(next.Position, point.Position) : Subtract(point.Position, previous.Position);
            return point with { Tangent = Normalize(delta) };
        }).ToArray();
        return spline with { Points = points };
    }

    private static IReadOnlyList<RekallAgeEvaluatedCurvePoint> RemoveCoincident(IReadOnlyList<RekallAgeEvaluatedCurvePoint> points)
    {
        var result = new List<RekallAgeEvaluatedCurvePoint>(points.Count);
        foreach (var point in points)
            if (result.Count == 0 || Length(Subtract(point.Position, result[^1].Position)) > 1e-9)
                result.Add(point);
        if (result.Count < 2) throw new InvalidDataException("Curve operation collapsed to fewer than two points.");
        return result;
    }

    private static RekallAgeEvaluatedCurvePoint LerpPoint(RekallAgeEvaluatedCurvePoint a, RekallAgeEvaluatedCurvePoint b, double t) => a with
    {
        Position = Lerp(a.Position, b.Position, t),
        Radius = Lerp(a.Radius, b.Radius, t),
        TiltRadians = Lerp(a.TiltRadians, b.TiltRadians, t),
        SegmentT = Lerp(a.SegmentT, b.SegmentT, t)
    };

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
    private static double Dot(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static RekallAgeGeometryVector3 Lerp(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b, double t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Z, b.Z, t));
    private static RekallAgeGeometryVector3 Subtract(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static RekallAgeGeometryVector3 Scale(RekallAgeGeometryVector3 value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
    private static RekallAgeGeometryVector3 Add(params RekallAgeGeometryVector3[] values) => new(values.Sum(value => value.X), values.Sum(value => value.Y), values.Sum(value => value.Z));
    private static RekallAgeGeometryVector3 Normalize(RekallAgeGeometryVector3 value)
    {
        var length = Length(value);
        if (length <= 1e-12 || !double.IsFinite(length)) throw new InvalidDataException("Curve contains a zero-length or non-finite span.");
        return Scale(value, 1 / length);
    }
}
