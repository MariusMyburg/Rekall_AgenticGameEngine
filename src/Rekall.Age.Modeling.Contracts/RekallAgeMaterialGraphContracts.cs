using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rekall.Age.Modeling.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeMaterialValueType>))]
public enum RekallAgeMaterialValueType
{
    Float,
    Vector2,
    Vector3,
    Color,
    Normal,
    Texture2D,
    Surface,
    String
}

public sealed record RekallAgeMaterialPortDescriptor(
    string PortId,
    string DisplayName,
    RekallAgeModelingPortDirection Direction,
    RekallAgeMaterialValueType ValueType,
    bool Required = false,
    bool AllowsMultipleLinks = false,
    string? Description = null);

public sealed record RekallAgeMaterialParameterDescriptor(
    string ParameterId,
    string DisplayName,
    RekallAgeMaterialValueType ValueType,
    JsonNode? DefaultValue = null,
    double? Minimum = null,
    double? Maximum = null,
    IReadOnlyList<string>? EnumChoices = null,
    string? AssetKind = null,
    string? Description = null);

public sealed record RekallAgeMaterialNodeDescriptor(
    string TypeId,
    int TypeVersion,
    string DisplayName,
    string Description,
    IReadOnlyList<RekallAgeMaterialPortDescriptor> Ports,
    IReadOnlyList<RekallAgeMaterialParameterDescriptor> Parameters,
    bool Deterministic = true);

public sealed record RekallAgeMaterialGraphNode(
    string NodeId,
    string TypeId,
    int TypeVersion,
    JsonObject Parameters);

public sealed record RekallAgeMaterialGraphLink(
    string LinkId,
    string FromNodeId,
    string FromPortId,
    string ToNodeId,
    string ToPortId);

public sealed record RekallAgeMaterialGraphOutput(string Name, string NodeId, string PortId);

public sealed record RekallAgeMaterialGraphExposedParameter(
    string Name,
    string NodeId,
    string ParameterId,
    RekallAgeMaterialValueType ValueType,
    JsonNode? DefaultValue = null);

public sealed record RekallAgeMaterialGraphAsset(
    int SchemaVersion,
    string AssetId,
    string Name,
    long Revision,
    IReadOnlyList<RekallAgeMaterialGraphNode> Nodes,
    IReadOnlyList<RekallAgeMaterialGraphLink> Links,
    RekallAgeMaterialGraphOutput Output,
    IReadOnlyList<RekallAgeMaterialGraphExposedParameter> ExposedParameters)
{
    public const int CurrentSchemaVersion = 1;

    public static RekallAgeMaterialGraphAsset Create(
        string assetId,
        string name,
        IReadOnlyList<RekallAgeMaterialGraphNode> nodes,
        IReadOnlyList<RekallAgeMaterialGraphLink> links,
        RekallAgeMaterialGraphOutput output,
        IReadOnlyList<RekallAgeMaterialGraphExposedParameter>? exposedParameters = null) =>
        new(CurrentSchemaVersion, assetId, name, 1,
            nodes?.ToArray() ?? throw new ArgumentNullException(nameof(nodes)),
            links?.ToArray() ?? throw new ArgumentNullException(nameof(links)),
            output ?? throw new ArgumentNullException(nameof(output)),
            exposedParameters?.ToArray() ?? []);
}

public sealed record RekallAgeMaterialGraphExecutionPlan(
    string AssetId,
    long SourceLogicalRevision,
    IReadOnlyList<string> OrderedNodeIds);

public sealed record RekallAgeMaterialGraphValidationReport(
    bool IsValid,
    RekallAgeMaterialGraphExecutionPlan? ExecutionPlan,
    IReadOnlyList<string> UnreachableNodeIds,
    IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics);

public sealed record RekallAgeMaterialInstanceAsset(
    int SchemaVersion,
    string AssetId,
    string Name,
    long Revision,
    string GraphAssetId,
    string GraphFileRevision,
    IReadOnlyDictionary<string, JsonNode?> Overrides)
{
    public const int CurrentSchemaVersion = 1;

    public static RekallAgeMaterialInstanceAsset Create(
        string assetId,
        string name,
        string graphAssetId,
        string graphFileRevision,
        IReadOnlyDictionary<string, JsonNode?>? overrides = null) =>
        new(CurrentSchemaVersion, assetId, name, 1, graphAssetId, graphFileRevision,
            (overrides ?? new Dictionary<string, JsonNode?>()).ToDictionary(
                item => item.Key,
                item => item.Value?.DeepClone(),
                StringComparer.Ordinal));
}
