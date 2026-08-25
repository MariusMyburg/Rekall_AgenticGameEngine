using System.Text.Json.Serialization;

namespace Rekall.Age.Modeling.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeCurveSplineKind>))]
public enum RekallAgeCurveSplineKind
{
    Poly,
    CubicBezier
}

public sealed record RekallAgeCurveControlPoint(
    ulong ControlPointId,
    RekallAgeGeometryVector3 Position,
    RekallAgeGeometryVector3 HandleIn,
    RekallAgeGeometryVector3 HandleOut,
    double Radius = 1,
    double TiltRadians = 0);

public sealed record RekallAgeCurveSpline(
    ulong SplineId,
    RekallAgeCurveSplineKind Kind,
    bool Cyclic,
    IReadOnlyList<RekallAgeCurveControlPoint> ControlPoints);

public sealed record RekallAgeCurveAsset(
    int SchemaVersion,
    string AssetId,
    string Name,
    long Revision,
    IReadOnlyList<RekallAgeCurveSpline> Splines)
{
    public const int CurrentSchemaVersion = 1;

    public static RekallAgeCurveAsset Create(string assetId, string name, IReadOnlyList<RekallAgeCurveSpline> splines) =>
        new(CurrentSchemaVersion, assetId, name, 1, splines);
}

public enum RekallAgeCurveDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record RekallAgeCurveDiagnostic(
    string Code,
    RekallAgeCurveDiagnosticSeverity Severity,
    string Message,
    ulong? SplineId = null,
    ulong? ControlPointId = null);

public sealed record RekallAgeCurveValidationReport(
    bool IsValid,
    IReadOnlyList<RekallAgeCurveDiagnostic> Diagnostics);

public sealed record RekallAgeEvaluatedCurvePoint(
    RekallAgeGeometryVector3 Position,
    RekallAgeGeometryVector3 Tangent,
    double Radius,
    double TiltRadians,
    ulong SourceSplineId,
    ulong SourceStartControlPointId,
    ulong SourceEndControlPointId,
    double SegmentT);

public sealed record RekallAgeEvaluatedCurveSpline(
    ulong SourceSplineId,
    bool Cyclic,
    IReadOnlyList<RekallAgeEvaluatedCurvePoint> Points);

public sealed record RekallAgeEvaluatedCurve(IReadOnlyList<RekallAgeEvaluatedCurveSpline> Splines);
