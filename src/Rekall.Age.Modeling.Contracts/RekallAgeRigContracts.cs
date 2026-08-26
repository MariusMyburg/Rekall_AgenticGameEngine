using System.Text.Json.Serialization;

namespace Rekall.Age.Modeling.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeRigDiagnosticSeverity>))]
public enum RekallAgeRigDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record RekallAgeRigJoint(
    string JointId,
    string Name,
    int? ParentIndex,
    IReadOnlyList<double> BindLocalMatrix);

public sealed record RekallAgeRigAsset(
    int SchemaVersion,
    string AssetId,
    string Name,
    long Revision,
    IReadOnlyList<RekallAgeRigJoint> Joints)
{
    public const int CurrentSchemaVersion = 1;

    public static RekallAgeRigAsset Create(
        string assetId,
        string name,
        IReadOnlyList<RekallAgeRigJoint> joints) =>
        new(CurrentSchemaVersion, assetId, name, 1, joints);
}

public sealed record RekallAgeRigDiagnostic(
    string Code,
    RekallAgeRigDiagnosticSeverity Severity,
    string Message,
    string? JointId = null);

public sealed record RekallAgeRigValidationReport(
    bool IsValid,
    IReadOnlyList<RekallAgeRigDiagnostic> Diagnostics);

public sealed record RekallAgeEvaluatedRig(
    IReadOnlyList<string> JointIds,
    IReadOnlyList<IReadOnlyList<double>> JointMatrices);
