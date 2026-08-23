using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModifierCatalog
{
    private readonly IReadOnlyDictionary<string, RekallAgeModifierDescriptor> _byIdentity;
    private RekallAgeModifierCatalog(IReadOnlyList<RekallAgeModifierDescriptor> descriptors)
    { Descriptors = descriptors; _byIdentity = descriptors.ToDictionary(item => $"{item.TypeId}@{item.TypeVersion}", StringComparer.Ordinal); }
    public IReadOnlyList<RekallAgeModifierDescriptor> Descriptors { get; }
    public RekallAgeModifierDescriptor? Find(string id, int version) => _byIdentity.GetValueOrDefault($"{id}@{version}");
    public static RekallAgeModifierCatalog CreateDefault()
    {
        var preserve = new RekallAgeModifierAttributePolicy(true, [], [], false);
        return new(
        [
            new("rekall.modifier.transform", 1, "Transform", "Translates selected or all points without changing topology.",
                RekallAgeMeshChangeKind.Positions,
                [Number("x", 0), Number("y", 0), Number("z", 0), Text("selection", "")], preserve),
            new("rekall.modifier.triangulate", 1, "Triangulate", "Triangulates selected or all polygon faces with provenance.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes,
                [Text("selection", "")], preserve),
            new("rekall.modifier.extrude", 1, "Extrude", "Extrudes a selected face region with stable provenance.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
                [Number("x", 0), Number("y", 0), Number("z", 1), Text("selection", "")], preserve),
            new("rekall.modifier.subdivide", 1, "Subdivide", "Subdivides selected or all polygon faces into centroid triangle fans.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
                [Text("selection", "")], preserve),
            new("rekall.modifier.merge_by_distance", 1, "Merge by Distance", "Welds selected or all points and deduplicates resulting edges.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
                [PositiveNumber("distance", 0.0001), Text("selection", "")], preserve)
        ]);
    }
    private static RekallAgeModelingParameterDescriptor Number(string id, double value) => new(id, id, RekallAgeModelingValueType.Scalar, JsonValue.Create(value), -1_000_000, 1_000_000);
    private static RekallAgeModelingParameterDescriptor PositiveNumber(string id, double value) => new(id, id, RekallAgeModelingValueType.Scalar, JsonValue.Create(value), 0.000000001, 1_000_000);
    private static RekallAgeModelingParameterDescriptor Text(string id, string value) => new(id, id, RekallAgeModelingValueType.String, JsonValue.Create(value));
}
