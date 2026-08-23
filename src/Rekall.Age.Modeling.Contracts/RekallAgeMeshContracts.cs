using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rekall.Age.Modeling.Contracts;

public readonly record struct RekallAgeMeshElementId(ulong Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct RekallAgeGeometryVector2(double X, double Y);

public readonly record struct RekallAgeGeometryVector3(double X, double Y, double Z);

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
