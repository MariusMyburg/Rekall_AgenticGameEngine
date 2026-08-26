using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeCurveValidator
{
    public RekallAgeCurveValidationReport Validate(RekallAgeCurveAsset curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var diagnostics = new List<RekallAgeCurveDiagnostic>();
        if (curve.SchemaVersion != RekallAgeCurveAsset.CurrentSchemaVersion)
            Error("REKALL_CURVE_SCHEMA_UNSUPPORTED", "Curve schema version is unsupported.");
        if (string.IsNullOrWhiteSpace(curve.AssetId) || curve.AssetId.Length > 128)
            Error("REKALL_CURVE_ASSET_ID_INVALID", "Curve asset ID must be bounded and non-empty.");
        if (curve.Revision < 1) Error("REKALL_CURVE_REVISION_INVALID", "Curve revision must be positive.");
        if (curve.Splines.Count == 0) Error("REKALL_CURVE_SPLINE_MISSING", "Curve requires at least one spline.");
        var splineIds = new HashSet<ulong>();
        var pointIds = new HashSet<ulong>();
        foreach (var spline in curve.Splines)
        {
            if (spline.SplineId == 0 || !splineIds.Add(spline.SplineId))
                Error("REKALL_CURVE_SPLINE_ID_DUPLICATE", $"Spline ID '{spline.SplineId}' is zero or duplicated.", spline.SplineId);
            var minimum = spline.Cyclic ? 3 : 2;
            if (spline.ControlPoints.Count < minimum)
                Error("REKALL_CURVE_CONTROL_POINT_COUNT_INVALID", $"Spline '{spline.SplineId}' requires at least {minimum} control points.", spline.SplineId);
            foreach (var point in spline.ControlPoints)
            {
                if (point.ControlPointId == 0 || !pointIds.Add(point.ControlPointId))
                    Error("REKALL_CURVE_CONTROL_POINT_ID_DUPLICATE", $"Control-point ID '{point.ControlPointId}' is zero or duplicated.", spline.SplineId, point.ControlPointId);
                if (!Finite(point.Position) || !Finite(point.HandleIn) || !Finite(point.HandleOut) || !double.IsFinite(point.Radius) || point.Radius <= 0 || !double.IsFinite(point.TiltRadians))
                    Error("REKALL_CURVE_CONTROL_POINT_INVALID", $"Control point '{point.ControlPointId}' contains non-finite data or a non-positive radius.", spline.SplineId, point.ControlPointId);
            }
        }
        return new(diagnostics.All(item => item.Severity != RekallAgeCurveDiagnosticSeverity.Error), diagnostics);

        void Error(string code, string message, ulong? splineId = null, ulong? pointId = null) =>
            diagnostics.Add(new(code, RekallAgeCurveDiagnosticSeverity.Error, message, splineId, pointId));
    }

    private static bool Finite(RekallAgeGeometryVector3 value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
}
