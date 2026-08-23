using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rekall.Age.Modeling.Contracts;

public sealed record RekallAgeModifierAttributePolicy(
    bool PreservesUnknownAttributes,
    IReadOnlyList<string> RecomputedSemantics,
    IReadOnlyList<RekallAgeGeometryDomain> DroppedDomains,
    bool Lossy);

public sealed record RekallAgeModifierDescriptor(
    string TypeId,
    int TypeVersion,
    string DisplayName,
    string Description,
    RekallAgeMeshChangeKind PossibleChanges,
    IReadOnlyList<RekallAgeModelingParameterDescriptor> Parameters,
    RekallAgeModifierAttributePolicy AttributePolicy,
    bool Deterministic = true);

public sealed record RekallAgeModifierInstance(
    string ModifierId,
    string TypeId,
    int TypeVersion,
    bool Enabled,
    JsonObject Parameters);

public sealed record RekallAgeModifierStackAsset(
    int SchemaVersion,
    string AssetId,
    string Name,
    long Revision,
    string SourceMeshAssetId,
    string SourceMeshFileRevision,
    IReadOnlyList<RekallAgeModifierInstance> Modifiers)
{
    public const int CurrentSchemaVersion = 1;
    public static RekallAgeModifierStackAsset Create(string assetId, string name, string sourceMeshAssetId,
        string sourceMeshFileRevision, IReadOnlyList<RekallAgeModifierInstance> modifiers) =>
        new(CurrentSchemaVersion, assetId, name, 1, sourceMeshAssetId, sourceMeshFileRevision,
            modifiers?.Select(item => item with { Parameters = (JsonObject)item.Parameters.DeepClone() }).ToArray() ?? throw new ArgumentNullException(nameof(modifiers)));
}

public sealed record RekallAgeModifierEvaluationItem(
    string ModifierId,
    string TypeId,
    string CacheKey,
    bool CacheHit,
    bool Invalidated,
    double DurationMilliseconds,
    int PointCount,
    int FaceCount,
    RekallAgeMeshChangeKind Changes);

public sealed record RekallAgeModifierStackEvaluationReport(
    bool Succeeded,
    string StackAssetId,
    long StackLogicalRevision,
    RekallAgeMeshAsset? Mesh,
    int EvaluatedModifierCount,
    int CacheHitCount,
    int InvalidatedModifierCount,
    IReadOnlyList<RekallAgeModifierEvaluationItem> Modifiers,
    double DurationMilliseconds,
    IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics);

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeModifierStackPatchKind>))]
public enum RekallAgeModifierStackPatchKind { Add, Remove, Move, Configure, SetEnabled, SetSource }

public sealed record RekallAgeModifierStackPatchOperation(
    RekallAgeModifierStackPatchKind Kind,
    RekallAgeModifierInstance? Modifier = null,
    string? TargetId = null,
    int? NewIndex = null,
    JsonObject? Parameters = null,
    bool? Enabled = null,
    string? SourceMeshAssetId = null,
    string? SourceMeshFileRevision = null);

public sealed record RekallAgeModifierStackPatch(IReadOnlyList<RekallAgeModifierStackPatchOperation> Operations);
