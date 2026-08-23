using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Rekall.Age.Modeling.Contracts;

public readonly record struct RekallAgeMeshElementId(ulong Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct RekallAgeGeometryVector2(double X, double Y);

public readonly record struct RekallAgeGeometryVector3(double X, double Y, double Z);

public readonly record struct RekallAgeGeometryVector4(double X, double Y, double Z, double W);

public sealed record RekallAgeLegacyGeometryVertex(
    RekallAgeGeometryVector3 Position,
    RekallAgeGeometryVector3? Normal = null,
    RekallAgeGeometryVector2? Uv = null,
    RekallAgeGeometryVector4? Color = null);

public readonly record struct RekallAgeMeshEdgePointIndices(int A, int B);

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeGeometryDomain>))]
public enum RekallAgeGeometryDomain
{
    Point,
    Edge,
    Face,
    Corner,
    Instance
}

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeGeometryValueType>))]
public enum RekallAgeGeometryValueType
{
    Bool,
    Int32,
    Float,
    Float2,
    Float3,
    Float4,
    ColorLinear,
    Quaternion,
    Matrix4x4,
    String
}

[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeGeometryInterpolation>))]
public enum RekallAgeGeometryInterpolation
{
    Constant,
    Nearest,
    Linear,
    NormalizedLinear
}

public sealed record RekallAgeGeometryAttribute(
    string Name,
    RekallAgeGeometryDomain Domain,
    RekallAgeGeometryValueType ValueType,
    IReadOnlyList<JsonElement> Values,
    string? Semantic = null,
    RekallAgeGeometryInterpolation Interpolation = RekallAgeGeometryInterpolation.Linear,
    JsonElement? DefaultValue = null);

public sealed record RekallAgeMaterialSlot(string Name, string? MaterialAssetId);

public sealed record RekallAgeMeshSelection(
    string Name,
    RekallAgeGeometryDomain Domain,
    IReadOnlyList<ulong> ElementIds,
    ulong? ActiveElementId = null,
    IReadOnlyList<ulong>? OrderedHistory = null);

public enum RekallAgeMeshDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record RekallAgeMeshDiagnostic(
    string Code,
    RekallAgeMeshDiagnosticSeverity Severity,
    string Message,
    IReadOnlyList<ulong> ElementIds);

public sealed record RekallAgeMeshBounds(
    RekallAgeGeometryVector3 Min,
    RekallAgeGeometryVector3 Max);

public sealed record RekallAgeMeshValidationSummary(
    int PointCount,
    int EdgeCount,
    int FaceCount,
    int CornerCount,
    int LooseEdgeCount,
    int BoundaryEdgeCount,
    int NonManifoldEdgeCount,
    RekallAgeMeshBounds Bounds);

public sealed record RekallAgeMeshValidationReport(
    bool IsValid,
    RekallAgeMeshValidationSummary Summary,
    IReadOnlyList<RekallAgeMeshDiagnostic> Diagnostics);

[Flags]
public enum RekallAgeMeshChangeKind
{
    None = 0,
    Positions = 1,
    Topology = 2,
    Attributes = 4,
    Selection = 8,
    Materials = 16
}

public sealed record RekallAgeMeshChangeSet(
    RekallAgeMeshChangeKind Kind,
    IReadOnlyList<ulong> CreatedPointIds,
    IReadOnlyList<ulong> CreatedEdgeIds,
    IReadOnlyList<ulong> CreatedFaceIds,
    IReadOnlyList<ulong> CreatedCornerIds,
    IReadOnlyList<ulong> DeletedPointIds,
    IReadOnlyList<ulong> DeletedEdgeIds,
    IReadOnlyList<ulong> DeletedFaceIds,
    IReadOnlyList<ulong> DeletedCornerIds,
    IReadOnlyList<ulong> ModifiedPointIds,
    IReadOnlyList<ulong> ModifiedEdgeIds,
    IReadOnlyList<ulong> ModifiedFaceIds,
    IReadOnlyList<ulong> ModifiedCornerIds,
    IReadOnlyList<string> ChangedAttributes,
    RekallAgeMeshBounds AffectedBounds);

public sealed record RekallAgeMeshElementProvenance(
    RekallAgeGeometryDomain Domain,
    ulong InputElementId,
    IReadOnlyList<ulong> OutputElementIds);

public sealed record RekallAgeMeshOperationRequest(
    string OperationId,
    RekallAgeGeometryDomain Domain,
    IReadOnlyList<ulong> ElementIds,
    JsonObject Parameters);

public sealed record RekallAgeMeshOperationResult(
    RekallAgeMeshAsset Mesh,
    long BeforeRevision,
    long AfterRevision,
    RekallAgeMeshChangeSet Changes,
    IReadOnlyList<RekallAgeMeshElementProvenance> Provenance,
    RekallAgeMeshValidationReport Validation);

public sealed record RekallAgeMeshAttributePredicate(string AttributeName, JsonElement EqualsValue);

public sealed record RekallAgeMeshElementSelector(
    RekallAgeGeometryDomain Domain,
    IReadOnlyList<ulong>? ExplicitElementIds = null,
    string? SelectionSetName = null,
    IReadOnlyList<ulong>? ConnectivitySeedIds = null,
    bool IncludeConnectivitySeeds = false,
    RekallAgeMeshBounds? WithinBounds = null,
    RekallAgeMeshAttributePredicate? AttributePredicate = null);

public sealed record RekallAgeMeshElementQueryResult(
    RekallAgeGeometryDomain Domain,
    IReadOnlyList<ulong> ElementIds,
    int MatchedCount,
    int TotalDomainCount,
    bool Truncated);

public sealed record RekallAgeMeshOperationParameterDescriptor(
    string Name,
    RekallAgeGeometryValueType ValueType,
    bool Required,
    JsonElement? DefaultValue,
    string Description);

public sealed record RekallAgeMeshOperationDescriptor(
    string OperationId,
    string Description,
    RekallAgeGeometryDomain Domain,
    RekallAgeMeshChangeKind PossibleChanges,
    IReadOnlyList<RekallAgeMeshOperationParameterDescriptor> Parameters);

public sealed record RekallAgeCompiledMeshVertex(
    ulong SourcePointId,
    ulong SourceCornerId,
    RekallAgeGeometryVector3 Position,
    RekallAgeGeometryVector3 Normal,
    RekallAgeGeometryVector4 Tangent,
    RekallAgeGeometryVector2 Uv,
    RekallAgeGeometryVector4 Color);

public sealed record RekallAgeCompiledMeshTriangle(
    int TriangleIndex,
    ulong SourceFaceId,
    IReadOnlyList<ulong> SourceCornerIds,
    IReadOnlyList<ulong> SourcePointIds,
    int SurfaceIndex);

public sealed record RekallAgeCompiledMeshSurface(
    int SurfaceIndex,
    int MaterialSlotIndex,
    string? MaterialAssetId,
    int FirstIndex,
    int IndexCount,
    IReadOnlyList<ulong> SourceFaceIds);

public sealed record RekallAgeCompiledMeshSnapshot(
    string SourceAssetId,
    long SourceLogicalRevision,
    IReadOnlyList<RekallAgeCompiledMeshVertex> Vertices,
    IReadOnlyList<uint> Indices,
    IReadOnlyList<RekallAgeCompiledMeshTriangle> Triangles,
    IReadOnlyList<RekallAgeCompiledMeshSurface> Surfaces,
    RekallAgeMeshBounds Bounds,
    bool HasVertexColors = false);

public sealed record RekallAgeMeshTopology(
    IReadOnlyList<ulong> PointIds,
    IReadOnlyList<RekallAgeGeometryVector3> Positions,
    IReadOnlyList<ulong> EdgeIds,
    IReadOnlyList<RekallAgeMeshEdgePointIndices> EdgePointIndices,
    IReadOnlyList<ulong> FaceIds,
    IReadOnlyList<int> FaceOffsets,
    IReadOnlyList<ulong> CornerIds,
    IReadOnlyList<int> CornerPointIndices,
    IReadOnlyList<int> CornerEdgeIndices);

public sealed record RekallAgeMeshAsset(
    int SchemaVersion,
    string AssetId,
    string Name,
    long Revision,
    RekallAgeMeshTopology Topology,
    IReadOnlyList<RekallAgeGeometryAttribute> Attributes,
    IReadOnlyList<RekallAgeMaterialSlot> MaterialSlots,
    IReadOnlyList<RekallAgeMeshSelection> SelectionSets)
{
    public const int CurrentSchemaVersion = 1;

    public static RekallAgeMeshAsset Create(
        string assetId,
        string name,
        RekallAgeMeshTopology topology,
        IReadOnlyList<RekallAgeGeometryAttribute>? attributes = null,
        IReadOnlyList<RekallAgeMaterialSlot>? materialSlots = null,
        IReadOnlyList<RekallAgeMeshSelection>? selectionSets = null) =>
        new(
            CurrentSchemaVersion,
            assetId,
            name,
            1,
            topology,
            attributes ?? [],
            materialSlots ?? [],
            selectionSets ?? []);
}

public static class RekallAgeModelingJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }
}
