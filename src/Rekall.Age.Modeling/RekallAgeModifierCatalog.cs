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
            new("rekall.modifier.subdivide_smooth", 1, "Smooth Subdivision", "Applies bounded crease-aware Catmull-Clark-style levels to the complete surface.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
                [Integer("levels", 1, 1, 6), Text("creaseAttribute", "crease.edge")], preserve),
            new("rekall.modifier.merge_by_distance", 1, "Merge by Distance", "Welds selected or all points and deduplicates resulting edges.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
                [PositiveNumber("distance", 0.0001), Text("selection", "")], preserve),
            new("rekall.modifier.bevel", 1, "Bevel", "Applies deterministic selection- and weight-aware profile bevel topology to two-face manifold edges.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes,
                [PositiveNumber("width", 0.05), Integer("segments", 1, 1, 64), Number("profile", 0.5), Boolean("clampOverlap", true), Boolean("hardenNormals", false), Text("selection", ""), Text("weightAttribute", ""), Integer("materialIndex", -1, -1, 65_535)], preserve),
            new("rekall.modifier.solidify", 1, "Solidify", "Adds shell thickness and optional boundary rims to a surface.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes,
                [Number("thickness", 0.05), Number("offset", 0), Boolean("rim", true), Boolean("evenThickness", true), Text("selection", "")], preserve),
            new("rekall.modifier.mirror", 1, "Mirror", "Creates a winding-correct mirrored copy around an authored local axis.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes,
                [Text("axis", "x"), Number("origin", 0), PositiveOrZero("mergeDistance", 0), Boolean("bisect", false)], preserve),
            new("rekall.modifier.array", 1, "Array", "Creates deterministic realized offset copies.",
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes,
                [Integer("count", 2, 1, 4_096), Number("x", 1), Number("y", 0), Number("z", 0), Boolean("relativeOffset", false), Boolean("instanceMode", false)], preserve),
            new("rekall.modifier.auto_smooth", 1, "Auto Smooth", "Classifies sharp normal-fan boundaries from adjacent face angles.",
                RekallAgeMeshChangeKind.Attributes,
                [BoundedNumber("angleDegrees", 60, 0, 180), Text("sharpAttribute", "normal.sharp")], preserve),
            new("rekall.modifier.weighted_normals", 1, "Weighted Normals", "Authors finite policy-aware area- and corner-angle-weighted normals.",
                RekallAgeMeshChangeKind.Attributes,
                [Text("attribute", "normal.authored"), BoundedNumber("faceAreaWeight", 1, 0, 4), BoundedNumber("cornerAngleWeight", 1, 0, 4), Text("smoothAttribute", "normal.smooth"), Text("sharpAttribute", "normal.sharp"), Text("selection", "")], preserve)
        ]);
    }
    private static RekallAgeModelingParameterDescriptor Number(string id, double value) => new(id, id, RekallAgeModelingValueType.Scalar, JsonValue.Create(value), -1_000_000, 1_000_000);
    private static RekallAgeModelingParameterDescriptor Integer(string id, int value, int minimum, int maximum) => new(id, id, RekallAgeModelingValueType.Integer, JsonValue.Create(value), minimum, maximum);
    private static RekallAgeModelingParameterDescriptor PositiveNumber(string id, double value) => new(id, id, RekallAgeModelingValueType.Scalar, JsonValue.Create(value), 0.000000001, 1_000_000);
    private static RekallAgeModelingParameterDescriptor PositiveOrZero(string id, double value) => new(id, id, RekallAgeModelingValueType.Scalar, JsonValue.Create(value), 0, 1_000_000);
    private static RekallAgeModelingParameterDescriptor BoundedNumber(string id, double value, double minimum, double maximum) => new(id, id, RekallAgeModelingValueType.Scalar, JsonValue.Create(value), minimum, maximum);
    private static RekallAgeModelingParameterDescriptor Boolean(string id, bool value) => new(id, id, RekallAgeModelingValueType.Boolean, JsonValue.Create(value));
    private static RekallAgeModelingParameterDescriptor Text(string id, string value) => new(id, id, RekallAgeModelingValueType.String, JsonValue.Create(value));
}
