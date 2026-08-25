using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rekall.Age.Modeling.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeModelingValueType>))]
public enum RekallAgeModelingValueType
{
    Geometry,
    Selection,
    Scalar,
    Integer,
    Boolean,
    Vector2,
    Vector3,
    Vector4,
    String,
    Material,
    Curve
}

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeModelingPortDirection>))]
public enum RekallAgeModelingPortDirection
{
    Input,
    Output
}

public sealed record RekallAgeModelingPortDescriptor(
    string PortId,
    string DisplayName,
    RekallAgeModelingPortDirection Direction,
    RekallAgeModelingValueType ValueType,
    bool Required = false,
    bool AllowsMultipleLinks = false,
    RekallAgeGeometryDomain? Domain = null,
    string? Description = null);

public sealed record RekallAgeModelingParameterDescriptor(
    string ParameterId,
    string DisplayName,
    RekallAgeModelingValueType ValueType,
    JsonNode? DefaultValue = null,
    double? Minimum = null,
    double? Maximum = null,
    string? Unit = null,
    IReadOnlyList<string>? EnumChoices = null,
    string? Description = null);

public sealed record RekallAgeModelingNodeDescriptor(
    string TypeId,
    int TypeVersion,
    string DisplayName,
    string Description,
    IReadOnlyList<RekallAgeModelingPortDescriptor> Ports,
    IReadOnlyList<RekallAgeModelingParameterDescriptor> Parameters,
    bool Deterministic = true);

public sealed record RekallAgeModelingGraphNode(
    string NodeId,
    string TypeId,
    int TypeVersion,
    JsonObject Parameters);

public sealed record RekallAgeModelingGraphLink(
    string LinkId,
    string FromNodeId,
    string FromPortId,
    string ToNodeId,
    string ToPortId);

public sealed record RekallAgeModelingGraphOutput(
    string Name,
    string NodeId,
    string PortId);

public sealed record RekallAgeModelingGraphExposedParameter(
    string Name,
    string NodeId,
    string ParameterId,
    JsonNode? DefaultValue = null);

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeModelingDiagnosticSeverity>))]
public enum RekallAgeModelingDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record RekallAgeModelingGraphDiagnostic(
    string Code,
    RekallAgeModelingDiagnosticSeverity Severity,
    string Message,
    string? NodeId = null,
    string? LinkId = null,
    string? PortId = null);

public sealed record RekallAgeModelingGraphExecutionPlan(
    string AssetId,
    long SourceLogicalRevision,
    IReadOnlyList<string> OrderedNodeIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> OutputNodeIds);

public sealed record RekallAgeModelingGraphValidationReport(
    bool IsValid,
    RekallAgeModelingGraphExecutionPlan? ExecutionPlan,
    IReadOnlyList<string> UnreachableNodeIds,
    IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics);

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeModelingGraphPatchKind>))]
public enum RekallAgeModelingGraphPatchKind
{
    AddNode,
    RemoveNode,
    SetParameter,
    AddLink,
    RemoveLink,
    SetOutput,
    RemoveOutput,
    ExposeParameter,
    RemoveExposedParameter
}

public sealed record RekallAgeModelingGraphPatchOperation(
    RekallAgeModelingGraphPatchKind Kind,
    RekallAgeModelingGraphNode? Node = null,
    RekallAgeModelingGraphLink? Link = null,
    RekallAgeModelingGraphOutput? Output = null,
    RekallAgeModelingGraphExposedParameter? ExposedParameter = null,
    string? TargetId = null,
    string? ParameterId = null,
    JsonNode? Value = null);

public sealed record RekallAgeModelingGraphPatch(
    IReadOnlyList<RekallAgeModelingGraphPatchOperation> Operations);

public sealed record RekallAgeModelingEvaluationContext(
    long Seed,
    double DeterministicTime,
    string EngineVersion,
    string TargetProfile,
    int EvaluationSchemaVersion = 1);

public sealed record RekallAgeModelingEvaluationBudget(
    int MaximumEvaluatedNodes,
    int MaximumPoints,
    int MaximumFaces,
    long MaximumApproximateBytes,
    int MaximumMilliseconds,
    int MaximumReportNodes)
{
    public static RekallAgeModelingEvaluationBudget Default { get; } =
        new(4_096, 2_000_000, 2_000_000, 512L * 1024 * 1024, 30_000, 256);
}

public sealed record RekallAgeModelingNodeEvaluationReport(
    string NodeId,
    string TypeId,
    string CacheKey,
    bool CacheHit,
    bool Invalidated,
    double DurationMilliseconds,
    int PointCount,
    int FaceCount,
    long ApproximateBytes);

public sealed record RekallAgeModelingGraphEvaluationReport(
    bool Succeeded,
    string AssetId,
    long SourceLogicalRevision,
    IReadOnlyDictionary<string, RekallAgeMeshAsset> Outputs,
    bool RetainedLastGoodOutputs,
    int EvaluatedNodeCount,
    int CacheHitCount,
    int InvalidatedNodeCount,
    IReadOnlyList<RekallAgeModelingNodeEvaluationReport> Nodes,
    bool NodesTruncated,
    double DurationMilliseconds,
    IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics);

public sealed record RekallAgeModelingGraphAsset(
    int SchemaVersion,
    string AssetId,
    string Name,
    long Revision,
    IReadOnlyList<RekallAgeModelingGraphNode> Nodes,
    IReadOnlyList<RekallAgeModelingGraphLink> Links,
    IReadOnlyList<RekallAgeModelingGraphOutput> Outputs,
    IReadOnlyList<RekallAgeModelingGraphExposedParameter> ExposedParameters)
{
    public const int CurrentSchemaVersion = 1;

    public static RekallAgeModelingGraphAsset Create(
        string assetId,
        string name,
        IReadOnlyList<RekallAgeModelingGraphNode> nodes,
        IReadOnlyList<RekallAgeModelingGraphLink> links,
        IReadOnlyList<RekallAgeModelingGraphOutput> outputs,
        IReadOnlyList<RekallAgeModelingGraphExposedParameter>? exposedParameters = null) =>
        new(
            CurrentSchemaVersion,
            assetId,
            name,
            1,
            nodes?.ToArray() ?? throw new ArgumentNullException(nameof(nodes)),
            links?.ToArray() ?? throw new ArgumentNullException(nameof(links)),
            outputs?.ToArray() ?? throw new ArgumentNullException(nameof(outputs)),
            exposedParameters?.ToArray() ?? []);
}
