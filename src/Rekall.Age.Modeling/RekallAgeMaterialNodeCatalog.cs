using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMaterialNodeCatalog
{
    private readonly IReadOnlyDictionary<string, RekallAgeMaterialNodeDescriptor> _byIdentity;
    private RekallAgeMaterialNodeCatalog(IReadOnlyList<RekallAgeMaterialNodeDescriptor> descriptors)
    {
        Descriptors = descriptors;
        _byIdentity = descriptors.ToDictionary(item => Identity(item.TypeId, item.TypeVersion), StringComparer.Ordinal);
    }

    public IReadOnlyList<RekallAgeMaterialNodeDescriptor> Descriptors { get; }
    public RekallAgeMaterialNodeDescriptor? Find(string typeId, int typeVersion) => _byIdentity.GetValueOrDefault(Identity(typeId, typeVersion));

    public static RekallAgeMaterialNodeCatalog CreateDefault() => new(
    [
        Node("rekall.material.constant.float", "Float", "Provides a finite scalar constant.",
            [Output("value", RekallAgeMaterialValueType.Float)], [Float("value", "Value", 0)]),
        Node("rekall.material.constant.color", "Color", "Provides a linear RGBA color constant.",
            [Output("color", RekallAgeMaterialValueType.Color)], [Color("value", "Color", "#ffffff")]),
        Node("rekall.material.coordinate.uv", "UV Coordinates", "Reads a selected mesh UV attribute.",
            [Output("uv", RekallAgeMaterialValueType.Vector2)], [Text("attribute", "Attribute", "uv0")]),
        Node("rekall.material.mapping", "Mapping", "Transforms two-dimensional texture coordinates.",
            [Input("vector", RekallAgeMaterialValueType.Vector2), Output("vector", RekallAgeMaterialValueType.Vector2)],
            [Vector2("scale", "Scale", 1), Vector2("offset", "Offset", 0), Float("rotation", "Rotation", 0, -360, 360)]),
        Node("rekall.material.texture.sample", "Texture Sample", "Samples an engine texture asset with explicit coordinates.",
            [Input("uv", RekallAgeMaterialValueType.Vector2), Output("color", RekallAgeMaterialValueType.Color), Output("alpha", RekallAgeMaterialValueType.Float)],
            [Asset("textureAssetId", "Texture", "texture"), Text("colorSpace", "Color Space", "srgb", ["srgb", "linear", "normal"])]),
        Node("rekall.material.math.float", "Float Math", "Applies finite scalar arithmetic.",
            [Input("a", RekallAgeMaterialValueType.Float), Input("b", RekallAgeMaterialValueType.Float), Output("value", RekallAgeMaterialValueType.Float)],
            [Text("operation", "Operation", "add", ["add", "subtract", "multiply", "divide", "minimum", "maximum", "power"]), Float("a", "A", 0), Float("b", "B", 0)]),
        Node("rekall.material.mix.color", "Mix Color", "Mixes two colors by a scalar factor.",
            [Input("a", RekallAgeMaterialValueType.Color), Input("b", RekallAgeMaterialValueType.Color), Input("factor", RekallAgeMaterialValueType.Float), Output("color", RekallAgeMaterialValueType.Color)],
            [Color("a", "A", "#000000"), Color("b", "B", "#ffffff"), Float("factor", "Factor", 0.5, 0, 1)]),
        Node("rekall.material.normal.map", "Normal Map", "Decodes tangent-space normal texture data.",
            [Input("color", RekallAgeMaterialValueType.Color, true), Output("normal", RekallAgeMaterialValueType.Normal)], [Float("strength", "Strength", 1, 0, 4)]),
        Node("rekall.material.surface.pbr", "PBR Surface", "Builds a portable physically based surface closure.",
            [Input("baseColor", RekallAgeMaterialValueType.Color), Input("metallic", RekallAgeMaterialValueType.Float), Input("roughness", RekallAgeMaterialValueType.Float), Input("normal", RekallAgeMaterialValueType.Normal), Input("emissive", RekallAgeMaterialValueType.Color), Output("surface", RekallAgeMaterialValueType.Surface)],
            [Color("baseColor", "Base Color", "#ffffff"), Float("metallic", "Metallic", 0, 0, 1), Float("roughness", "Roughness", 1, 0.04, 1), Color("emissive", "Emissive", "#000000"), Float("emissiveStrength", "Emissive Strength", 0, 0, 1_000_000)]),
        Node("rekall.material.surface.emissive", "Emissive Surface", "Builds an unlit emissive surface closure.",
            [Input("color", RekallAgeMaterialValueType.Color), Input("strength", RekallAgeMaterialValueType.Float), Output("surface", RekallAgeMaterialValueType.Surface)],
            [Color("color", "Color", "#ffffff"), Float("strength", "Strength", 1, 0, 1_000_000)]),
        Node("rekall.material.output", "Material Output", "Publishes the graph's surface to the renderer.",
            [Input("surface", RekallAgeMaterialValueType.Surface, true), Output("surface", RekallAgeMaterialValueType.Surface)])
    ]);

    private static RekallAgeMaterialNodeDescriptor Node(string id, string name, string description,
        IReadOnlyList<RekallAgeMaterialPortDescriptor> ports, IReadOnlyList<RekallAgeMaterialParameterDescriptor>? parameters = null) =>
        new(id, 1, name, description, ports, parameters ?? []);
    private static RekallAgeMaterialPortDescriptor Input(string id, RekallAgeMaterialValueType type, bool required = false) => new(id, Display(id), RekallAgeModelingPortDirection.Input, type, required);
    private static RekallAgeMaterialPortDescriptor Output(string id, RekallAgeMaterialValueType type) => new(id, Display(id), RekallAgeModelingPortDirection.Output, type);
    private static RekallAgeMaterialParameterDescriptor Float(string id, string name, double value, double? minimum = null, double? maximum = null) => new(id, name, RekallAgeMaterialValueType.Float, JsonValue.Create(value), minimum, maximum);
    private static RekallAgeMaterialParameterDescriptor Color(string id, string name, string value) => new(id, name, RekallAgeMaterialValueType.Color, JsonValue.Create(value));
    private static RekallAgeMaterialParameterDescriptor Vector2(string id, string name, double value) => new(id, name, RekallAgeMaterialValueType.Vector2, new JsonArray(value, value));
    private static RekallAgeMaterialParameterDescriptor Text(string id, string name, string value, IReadOnlyList<string>? choices = null) => new(id, name, RekallAgeMaterialValueType.String, JsonValue.Create(value), EnumChoices: choices);
    private static RekallAgeMaterialParameterDescriptor Asset(string id, string name, string kind) => new(id, name, RekallAgeMaterialValueType.Texture2D, JsonValue.Create(string.Empty), AssetKind: kind);
    private static string Display(string id) => string.Concat(id.Select((character, index) => index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));
    private static string Identity(string id, int version) => $"{id}@{version}";
}
