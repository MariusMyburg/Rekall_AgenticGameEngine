using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeCurveEvaluator
{
    public RekallAgeEvaluatedCurve Evaluate(RekallAgeCurveAsset curve, int resolutionPerSegment = 8)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var validation = new RekallAgeCurveValidator().Validate(curve);
        if (!validation.IsValid)
            throw new InvalidDataException("Curve is invalid: " + string.Join(", ", validation.Diagnostics.Select(item => item.Code).Distinct(StringComparer.Ordinal)));
        if (resolutionPerSegment is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(resolutionPerSegment));
        var total = curve.Splines.Sum(spline => (spline.Cyclic ? spline.ControlPoints.Count : spline.ControlPoints.Count - 1) * resolutionPerSegment + (spline.Cyclic ? 0 : 1));
        if (total > 1_000_000) throw new InvalidDataException("Evaluated curve exceeds the one-million-point safety bound.");

        return new(curve.Splines.Select(spline => EvaluateSpline(spline, resolutionPerSegment)).ToArray());
    }

    private static RekallAgeEvaluatedCurveSpline EvaluateSpline(RekallAgeCurveSpline spline, int resolution)
    {
        var points = new List<RekallAgeEvaluatedCurvePoint>();
        var segmentCount = spline.Cyclic ? spline.ControlPoints.Count : spline.ControlPoints.Count - 1;
        for (var segment = 0; segment < segmentCount; segment++)
        {
            var start = spline.ControlPoints[segment];
            var end = spline.ControlPoints[(segment + 1) % spline.ControlPoints.Count];
            for (var step = 0; step < resolution; step++) points.Add(Sample(spline, start, end, step / (double)resolution));
        }
        if (!spline.Cyclic)
        {
            var start = spline.ControlPoints[^2];
            var end = spline.ControlPoints[^1];
            points.Add(Sample(spline, start, end, 1));
        }
        return new(spline.SplineId, spline.Cyclic, points);
    }

    private static RekallAgeEvaluatedCurvePoint Sample(
        RekallAgeCurveSpline spline,
        RekallAgeCurveControlPoint start,
        RekallAgeCurveControlPoint end,
        double t)
    {
        RekallAgeGeometryVector3 position;
        RekallAgeGeometryVector3 derivative;
        if (spline.Kind == RekallAgeCurveSplineKind.CubicBezier)
        {
            var inverse = 1 - t;
            position = Add(Scale(start.Position, inverse * inverse * inverse), Scale(start.HandleOut, 3 * inverse * inverse * t), Scale(end.HandleIn, 3 * inverse * t * t), Scale(end.Position, t * t * t));
            derivative = Add(Scale(Subtract(start.HandleOut, start.Position), 3 * inverse * inverse), Scale(Subtract(end.HandleIn, start.HandleOut), 6 * inverse * t), Scale(Subtract(end.Position, end.HandleIn), 3 * t * t));
        }
        else
        {
            position = Lerp(start.Position, end.Position, t);
            derivative = Subtract(end.Position, start.Position);
        }
        var tangent = Normalize(derivative, Subtract(end.Position, start.Position));
        return new(position, tangent, start.Radius + (end.Radius - start.Radius) * t, start.TiltRadians + (end.TiltRadians - start.TiltRadians) * t, spline.SplineId, start.ControlPointId, end.ControlPointId, t);
    }

    private static RekallAgeGeometryVector3 Lerp(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    private static RekallAgeGeometryVector3 Subtract(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static RekallAgeGeometryVector3 Scale(RekallAgeGeometryVector3 value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
    private static RekallAgeGeometryVector3 Add(params RekallAgeGeometryVector3[] values) => new(values.Sum(value => value.X), values.Sum(value => value.Y), values.Sum(value => value.Z));
    private static RekallAgeGeometryVector3 Normalize(RekallAgeGeometryVector3 value, RekallAgeGeometryVector3 fallback)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        if (length <= 1e-12)
        {
            value = fallback;
            length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        }
        if (length <= 1e-12) throw new InvalidDataException("Curve contains a zero-length evaluated span.");
        return new(value.X / length, value.Y / length, value.Z / length);
    }
}
