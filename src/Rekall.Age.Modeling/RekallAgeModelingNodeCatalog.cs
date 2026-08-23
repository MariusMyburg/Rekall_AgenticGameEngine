using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModelingNodeCatalog
{
    private readonly IReadOnlyDictionary<string, RekallAgeModelingNodeDescriptor> _byIdentity;

    private RekallAgeModelingNodeCatalog(IReadOnlyList<RekallAgeModelingNodeDescriptor> descriptors)
    {
        Descriptors = descriptors;
        _byIdentity = descriptors.ToDictionary(Identity, StringComparer.Ordinal);
    }

    public IReadOnlyList<RekallAgeModelingNodeDescriptor> Descriptors { get; }

    public RekallAgeModelingNodeDescriptor? Find(string typeId, int typeVersion) =>
        _byIdentity.GetValueOrDefault(Identity(typeId, typeVersion));

    public static RekallAgeModelingNodeCatalog CreateDefault() => new(
    [
        Primitive("rekall.modeling.primitive.box", "Box", [
            Number("sizeX", "Size X", 1, 0.0001, 1_000_000, "world-unit"),
            Number("sizeY", "Size Y", 1, 0.0001, 1_000_000, "world-unit"),
            Number("sizeZ", "Size Z", 1, 0.0001, 1_000_000, "world-unit")]),
        Primitive("rekall.modeling.primitive.grid", "Grid", [
            Number("sizeX", "Size X", 1, 0.0001, 1_000_000, "world-unit"),
            Number("sizeY", "Size Y", 1, 0.0001, 1_000_000, "world-unit"),
            Integer("segmentsX", "Segments X", 1, 1, 4_096),
            Integer("segmentsY", "Segments Y", 1, 1, 4_096)]),
        Primitive("rekall.modeling.primitive.sphere", "Sphere", [
            Number("radius", "Radius", 0.5, 0.0001, 1_000_000, "world-unit"),
            Integer("segments", "Segments", 16, 3, 4_096),
            Integer("rings", "Rings", 8, 2, 4_096)]),
        Node("rekall.modeling.transform", "Transform", "Transforms geometry without mutating its upstream snapshot.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Vector3("translation", "Translation"), Vector3("rotation", "Rotation", "degree"), Vector3("scale", "Scale", defaultValue: 1)]),
        Node("rekall.modeling.join", "Join Geometry", "Combines one or more immutable geometry inputs.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true, multiple: true), Output("geometry", RekallAgeModelingValueType.Geometry)]),
        Node("rekall.modeling.extrude", "Extrude", "Extrudes a selected geometry region through the semantic mesh operation contract.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Vector3("offset", "Offset")]),
        Node("rekall.modeling.triangulate", "Triangulate", "Triangulates selected polygon faces with source provenance.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)]),
        Node("rekall.modeling.subdivide", "Subdivide", "Subdivides selected polygon faces into centroid triangle fans with source provenance.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)]),
        Node("rekall.modeling.subdivide_smooth", "Smooth Subdivision", "Applies one Catmull-Clark-style level to a complete manifold or boundary surface.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)]),
        Node("rekall.modeling.merge_by_distance", "Merge by Distance", "Welds selected points using deterministic spatial hashing and stable provenance.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Number("distance", "Distance", 0.0001, 0.000000001, 1_000_000, "world-unit")]),
        Node("rekall.modeling.attribute.capture", "Capture Attribute", "Captures a field into a named typed geometry attribute.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("value", RekallAgeModelingValueType.Scalar, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("name", "Name", "attribute"), Text("domain", "Domain", "point")]),
        Node("rekall.modeling.attribute.named", "Named Attribute", "Reads a named scalar attribute as a graph field.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("value", RekallAgeModelingValueType.Scalar)],
            [Text("name", "Name", "attribute")]),
        Node("rekall.modeling.field.math", "Field Math", "Applies deterministic scalar field arithmetic.",
            [Input("a", RekallAgeModelingValueType.Scalar), Input("b", RekallAgeModelingValueType.Scalar), Output("value", RekallAgeModelingValueType.Scalar)],
            [
                Text("operation", "Operation", "add", ["add", "subtract", "multiply", "divide", "minimum", "maximum"]),
                Number("a", "A", 0, -1_000_000_000, 1_000_000_000),
                Number("b", "B", 0, -1_000_000_000, 1_000_000_000)
            ]),
        Node("rekall.modeling.material.assign", "Assign Material", "Assigns a material slot to a selected geometry region.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Input("material", RekallAgeModelingValueType.Material), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("materialAssetId", "Material Asset ID", "material.default"), Text("slotName", "Slot Name", "material")]),
        Node("rekall.modeling.output.mesh", "Mesh Output", "Publishes evaluated geometry as a named graph output.",
            [Input("input", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)])
    ]);

    private static RekallAgeModelingNodeDescriptor Primitive(
        string typeId,
        string displayName,
        IReadOnlyList<RekallAgeModelingParameterDescriptor> parameters) =>
        Node(typeId, displayName, $"Creates deterministic {displayName.ToLowerInvariant()} geometry.",
            [Output("geometry", RekallAgeModelingValueType.Geometry)], parameters);

    private static RekallAgeModelingNodeDescriptor Node(
        string typeId,
        string displayName,
        string description,
        IReadOnlyList<RekallAgeModelingPortDescriptor> ports,
        IReadOnlyList<RekallAgeModelingParameterDescriptor>? parameters = null) =>
        new(typeId, 1, displayName, description, ports, parameters ?? []);

    private static RekallAgeModelingPortDescriptor Input(
        string id,
        RekallAgeModelingValueType type,
        bool required = false,
        bool multiple = false) =>
        new(id, Display(id), RekallAgeModelingPortDirection.Input, type, required, multiple);

    private static RekallAgeModelingPortDescriptor Output(string id, RekallAgeModelingValueType type) =>
        new(id, Display(id), RekallAgeModelingPortDirection.Output, type);

    private static RekallAgeModelingParameterDescriptor Number(
        string id, string name, double value, double minimum, double maximum, string? unit = null) =>
        new(id, name, RekallAgeModelingValueType.Scalar, JsonValue.Create(value), minimum, maximum, unit);

    private static RekallAgeModelingParameterDescriptor Integer(
        string id, string name, int value, int minimum, int maximum) =>
        new(id, name, RekallAgeModelingValueType.Integer, JsonValue.Create(value), minimum, maximum);

    private static RekallAgeModelingParameterDescriptor Vector3(
        string id, string name, string? unit = null, double defaultValue = 0) =>
        new(id, name, RekallAgeModelingValueType.Vector3,
            new JsonArray(defaultValue, defaultValue, defaultValue), Unit: unit);

    private static RekallAgeModelingParameterDescriptor Text(
        string id, string name, string value, IReadOnlyList<string>? choices = null) =>
        new(id, name, RekallAgeModelingValueType.String, JsonValue.Create(value), EnumChoices: choices);

    private static string Identity(RekallAgeModelingNodeDescriptor descriptor) =>
        Identity(descriptor.TypeId, descriptor.TypeVersion);

    private static string Identity(string typeId, int typeVersion) => $"{typeId}@{typeVersion}";

    private static string Display(string id) => char.ToUpperInvariant(id[0]) + id[1..];
}
