using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeMaterialShaderSourceMapEntry(string NodeId, string PortId, int StartLine, int EndLine);
public sealed record RekallAgeMaterialCompiledShader(string Language, string EntryPoint, string Source, IReadOnlyList<RekallAgeMaterialShaderSourceMapEntry> SourceMap);
public sealed record RekallAgeMaterialTextureResource(string NodeId, string TextureAssetId, int Set, int TextureBinding, int SamplerBinding);
public sealed record RekallAgeCompiledMaterialGraph(bool Succeeded, string AssetId, long SourceLogicalRevision, string ContentHash,
    RekallAgeMaterialCompiledShader Glsl, RekallAgeMaterialCompiledShader Wgsl, IReadOnlyList<RekallAgeMaterialTextureResource> Resources,
    IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics);

public sealed class RekallAgeMaterialGraphCompiler
{
    private const int MaximumTextures = 7;
    private readonly RekallAgeMaterialGraphValidator _validator = new(RekallAgeMaterialNodeCatalog.CreateDefault());
    private readonly RekallAgeMaterialNodeCatalog _catalog = RekallAgeMaterialNodeCatalog.CreateDefault();

    public RekallAgeCompiledMaterialGraph Compile(RekallAgeMaterialGraphAsset graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var validation = _validator.Validate(graph);
        if (!validation.IsValid || validation.ExecutionPlan is null) return Failed(graph, validation.Diagnostics);
        var diagnostics = new List<RekallAgeModelingGraphDiagnostic>();
        var nodes = graph.Nodes.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
        var order = validation.ExecutionPlan.OrderedNodeIds;
        var textureNodes = order.Select(id => nodes[id]).Where(item => item.TypeId == "rekall.material.texture.sample").ToArray();
        if (textureNodes.Length > MaximumTextures)
        {
            diagnostics.Add(new("REKALL_MATERIAL_GRAPH_TEXTURE_LIMIT", RekallAgeModelingDiagnosticSeverity.Error,
                $"Material graphs support at most {MaximumTextures} sampled textures in scene material ABI {RekallAgeSceneMaterialShaderAbi.Version}."));
            return Failed(graph, diagnostics);
        }
        var resources = textureNodes.Select((node, index) => new RekallAgeMaterialTextureResource(
            node.NodeId, ReadString(node, "textureAssetId", string.Empty), 2, index * 2, index * 2 + 1)).ToArray();
        foreach (var resource in resources.Where(item => string.IsNullOrWhiteSpace(item.TextureAssetId)))
            diagnostics.Add(new("REKALL_MATERIAL_GRAPH_TEXTURE_ASSET_MISSING", RekallAgeModelingDiagnosticSeverity.Error,
                "Texture sample requires a non-empty textureAssetId.", resource.NodeId));
        if (diagnostics.Count > 0) return Failed(graph, diagnostics);

        var glsl = new SourceBuilder(); var wgsl = new SourceBuilder();
        WriteGlslHeader(glsl, resources); WriteWgslHeader(wgsl, resources);
        var values = new Dictionary<(string Node, string Port), ShaderValue>();
        for (var index = 0; index < order.Count; index++)
        {
            var node = nodes[order[index]];
            try { EmitNode(graph, node, index, resources, values, glsl, wgsl); }
            catch (MaterialCompileException error) { diagnostics.Add(new(error.Code, RekallAgeModelingDiagnosticSeverity.Error, error.Message, node.NodeId, PortId: error.PortId)); }
        }
        if (diagnostics.Count > 0) return Failed(graph, diagnostics);
        if (!values.TryGetValue((graph.Output.NodeId, graph.Output.PortId), out var surface))
            return Failed(graph, [new("REKALL_MATERIAL_GRAPH_OUTPUT_UNCOMPILED", RekallAgeModelingDiagnosticSeverity.Error, "The material surface output did not compile.", graph.Output.NodeId, PortId: graph.Output.PortId)]);
        WriteGlslFooter(glsl, surface.Glsl); WriteWgslFooter(wgsl, surface.Wgsl);
        var glslSource = glsl.Build(); var wgslSource = wgsl.Build();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(glslSource.ReplaceLineEndings("\n") + "\0" + wgslSource.ReplaceLineEndings("\n"))));
        return new(true, graph.AssetId, graph.Revision, hash,
            new("glsl", "main", glslSource, glsl.Map), new("wgsl", "main", wgslSource, wgsl.Map), resources, []);
    }

    private void EmitNode(RekallAgeMaterialGraphAsset graph, RekallAgeMaterialGraphNode node, int index,
        IReadOnlyList<RekallAgeMaterialTextureResource> resources,
        Dictionary<(string Node, string Port), ShaderValue> values, SourceBuilder glsl, SourceBuilder wgsl)
    {
        var name = $"n{index}";
        ShaderValue Input(string port, RekallAgeMaterialValueType type, ShaderValue fallback)
        {
            var link = graph.Links.SingleOrDefault(item => item.ToNodeId == node.NodeId && item.ToPortId == port);
            if (link is null) return fallback;
            return values.TryGetValue((link.FromNodeId, link.FromPortId), out var value) && value.Type == type
                ? value : throw new MaterialCompileException("REKALL_MATERIAL_GRAPH_INPUT_UNAVAILABLE", $"Input '{port}' was not compiled.", port);
        }
        void Emit(string port, RekallAgeMaterialValueType type, string glslType, string wgslType, string glslExpression, string wgslExpression)
        {
            var variable = $"{name}_{Safe(port)}";
            glsl.AddMapped(node.NodeId, port, $"    {glslType} {variable} = {glslExpression};");
            wgsl.AddMapped(node.NodeId, port, $"    let {variable}: {wgslType} = {wgslExpression};");
            values[(node.NodeId, port)] = new(type, variable, variable);
        }

        switch (node.TypeId)
        {
            case "rekall.material.constant.float":
                Emit("value", RekallAgeMaterialValueType.Float, "float", "f32", Number(ReadNumber(node, "value", 0)), Number(ReadNumber(node, "value", 0)));
                break;
            case "rekall.material.constant.color":
                var color = Color(ReadString(node, "value", "#ffffff"));
                Emit("color", RekallAgeMaterialValueType.Color, "vec4", "vec4f", color.Glsl, color.Wgsl);
                break;
            case "rekall.material.coordinate.uv":
                Emit("uv", RekallAgeMaterialValueType.Vector2, "vec2", "vec2f", "fragUv", "input.uv");
                break;
            case "rekall.material.mapping":
                var vector = Input("vector", RekallAgeMaterialValueType.Vector2, new(RekallAgeMaterialValueType.Vector2, "fragUv", "input.uv"));
                var scale = ReadVector2(node, "scale", 1); var offset = ReadVector2(node, "offset", 0); var radians = ReadNumber(node, "rotation", 0) * Math.PI / 180;
                var cs = Number(Math.Cos(radians)); var sn = Number(Math.Sin(radians)); var negativeSn = Number(-Math.Sin(radians));
                Emit("vector", RekallAgeMaterialValueType.Vector2, "vec2", "vec2f",
                    $"mat2({cs}, {sn}, {negativeSn}, {cs}) * ({vector.Glsl} * vec2({Number(scale.X)}, {Number(scale.Y)})) + vec2({Number(offset.X)}, {Number(offset.Y)})",
                    $"mat2x2f({cs}, {sn}, {negativeSn}, {cs}) * ({vector.Wgsl} * vec2f({Number(scale.X)}, {Number(scale.Y)})) + vec2f({Number(offset.X)}, {Number(offset.Y)})");
                break;
            case "rekall.material.texture.sample":
                var uv = Input("uv", RekallAgeMaterialValueType.Vector2, new(RekallAgeMaterialValueType.Vector2, "fragUv", "input.uv"));
                var resource = resources.Single(item => item.NodeId == node.NodeId); var resourceIndex = resource.TextureBinding / 2;
                Emit("color", RekallAgeMaterialValueType.Color, "vec4", "vec4f",
                    $"texture(sampler2D(materialTexture{resourceIndex}, materialSampler{resourceIndex}), {uv.Glsl})",
                    $"textureSample(materialTexture{resourceIndex}, materialSampler{resourceIndex}, {uv.Wgsl})");
                var sampled = values[(node.NodeId, "color")];
                Emit("alpha", RekallAgeMaterialValueType.Float, "float", "f32", $"{sampled.Glsl}.a", $"{sampled.Wgsl}.a");
                break;
            case "rekall.material.math.float":
                var a = Input("a", RekallAgeMaterialValueType.Float, Scalar(ReadNumber(node, "a", 0)));
                var b = Input("b", RekallAgeMaterialValueType.Float, Scalar(ReadNumber(node, "b", 0)));
                var operation = ReadString(node, "operation", "add");
                Emit("value", RekallAgeMaterialValueType.Float, "float", "f32", MathExpression(operation, a.Glsl, b.Glsl, false), MathExpression(operation, a.Wgsl, b.Wgsl, true));
                break;
            case "rekall.material.mix.color":
                var ca = Input("a", RekallAgeMaterialValueType.Color, Color(ReadString(node, "a", "#000000")));
                var cb = Input("b", RekallAgeMaterialValueType.Color, Color(ReadString(node, "b", "#ffffff")));
                var factor = Input("factor", RekallAgeMaterialValueType.Float, Scalar(ReadNumber(node, "factor", 0.5)));
                Emit("color", RekallAgeMaterialValueType.Color, "vec4", "vec4f", $"mix({ca.Glsl}, {cb.Glsl}, clamp({factor.Glsl}, 0.0, 1.0))", $"mix({ca.Wgsl}, {cb.Wgsl}, clamp({factor.Wgsl}, 0.0, 1.0))");
                break;
            case "rekall.material.normal.map":
                var normalColor = Input("color", RekallAgeMaterialValueType.Color, default);
                var strength = Number(ReadNumber(node, "strength", 1));
                Emit("normal", RekallAgeMaterialValueType.Normal, "vec3", "vec3f", $"normalize(vec3(({normalColor.Glsl}.xy * 2.0 - 1.0) * {strength}, {normalColor.Glsl}.z * 2.0 - 1.0))", $"normalize(vec3f(({normalColor.Wgsl}.xy * 2.0 - vec2f(1.0)) * {strength}, {normalColor.Wgsl}.z * 2.0 - 1.0))");
                break;
            case "rekall.material.surface.pbr":
                var baseColor = Input("baseColor", RekallAgeMaterialValueType.Color, Color(ReadString(node, "baseColor", "#ffffff")));
                var metallic = Input("metallic", RekallAgeMaterialValueType.Float, Scalar(ReadNumber(node, "metallic", 0)));
                var roughness = Input("roughness", RekallAgeMaterialValueType.Float, Scalar(ReadNumber(node, "roughness", 1)));
                var normal = Input("normal", RekallAgeMaterialValueType.Normal, new(RekallAgeMaterialValueType.Normal, "normalize(fragNormal)", "normalize(input.normal)"));
                var emissive = Input("emissive", RekallAgeMaterialValueType.Color, Color(ReadString(node, "emissive", "#000000")));
                var emissiveStrength = Number(ReadNumber(node, "emissiveStrength", 0));
                Emit("surface", RekallAgeMaterialValueType.Surface, "RekallMaterialSurface", "RekallMaterialSurface",
                    $"RekallMaterialSurface({baseColor.Glsl}, clamp({metallic.Glsl}, 0.0, 1.0), clamp({roughness.Glsl}, 0.04, 1.0), {normal.Glsl}, {emissive.Glsl}.rgb * {emissiveStrength})",
                    $"RekallMaterialSurface({baseColor.Wgsl}, clamp({metallic.Wgsl}, 0.0, 1.0), clamp({roughness.Wgsl}, 0.04, 1.0), {normal.Wgsl}, {emissive.Wgsl}.rgb * {emissiveStrength})");
                break;
            case "rekall.material.surface.emissive":
                var emission = Input("color", RekallAgeMaterialValueType.Color, Color(ReadString(node, "color", "#ffffff")));
                var emissionStrength = Input("strength", RekallAgeMaterialValueType.Float, Scalar(ReadNumber(node, "strength", 1)));
                Emit("surface", RekallAgeMaterialValueType.Surface, "RekallMaterialSurface", "RekallMaterialSurface",
                    $"RekallMaterialSurface({emission.Glsl}, 0.0, 1.0, normalize(fragNormal), {emission.Glsl}.rgb * max({emissionStrength.Glsl}, 0.0))",
                    $"RekallMaterialSurface({emission.Wgsl}, 0.0, 1.0, normalize(input.normal), {emission.Wgsl}.rgb * max({emissionStrength.Wgsl}, 0.0))");
                break;
            case "rekall.material.output":
                var output = Input("surface", RekallAgeMaterialValueType.Surface, default);
                Emit("surface", RekallAgeMaterialValueType.Surface, "RekallMaterialSurface", "RekallMaterialSurface", output.Glsl, output.Wgsl);
                break;
            default: throw new MaterialCompileException("REKALL_MATERIAL_GRAPH_NODE_COMPILER_MISSING", $"Node type '{node.TypeId}' has no backend compiler.");
        }
    }

    private static void WriteGlslHeader(SourceBuilder code, IReadOnlyList<RekallAgeMaterialTextureResource> resources)
    {
        code.Add("#version 450", "", "layout(location = 0) in vec3 fragNormal;", "layout(location = 1) in vec4 fragColor;", "layout(location = 2) in vec2 fragUv;", "layout(location = 3) in vec3 fragWorldPosition;", "",
            "layout(set = 0, binding = 0) uniform FrameUniform { mat4 viewProjection; vec4 lightDirection; vec4 lightColor; vec4 lightPosition; vec4 cameraPosition; } frame;");
        foreach (var resource in resources) { var index = resource.TextureBinding / 2; code.Add($"layout(set = 2, binding = {resource.TextureBinding}) uniform texture2D materialTexture{index};", $"layout(set = 2, binding = {resource.SamplerBinding}) uniform sampler materialSampler{index};"); }
        code.Add("layout(location = 0) out vec4 outColor;", "struct RekallMaterialSurface { vec4 baseColor; float metallic; float roughness; vec3 normal; vec3 emissive; };", "void main()", "{");
    }
    private static void WriteGlslFooter(SourceBuilder code, string surface) => code.Add(
        $"    RekallMaterialSurface material = {surface};",
        "    vec3 normal = normalize(material.normal);",
        "    vec3 lightDirection = normalize(-frame.lightDirection.xyz);",
        "    float diffuse = max(dot(normal, lightDirection), 0.0);",
        "    vec3 dielectric = material.baseColor.rgb * (0.08 + diffuse * max(frame.lightColor.rgb, vec3(0.0)));",
        "    vec3 color = mix(dielectric, material.baseColor.rgb * diffuse, material.metallic) + material.emissive;",
        "    outColor = vec4(color, material.baseColor.a);", "}");
    private static void WriteWgslHeader(SourceBuilder code, IReadOnlyList<RekallAgeMaterialTextureResource> resources)
    {
        code.Add("struct FrameUniform { viewProjection: mat4x4f, lightDirection: vec4f, lightColor: vec4f, lightPosition: vec4f, cameraPosition: vec4f };", "@group(0) @binding(0) var<uniform> frame: FrameUniform;");
        foreach (var resource in resources) { var index = resource.TextureBinding / 2; code.Add($"@group(2) @binding({resource.TextureBinding}) var materialTexture{index}: texture_2d<f32>;", $"@group(2) @binding({resource.SamplerBinding}) var materialSampler{index}: sampler;"); }
        code.Add("struct FragmentInput { @location(0) normal: vec3f, @location(1) color: vec4f, @location(2) uv: vec2f, @location(3) worldPosition: vec3f };",
            "struct RekallMaterialSurface { baseColor: vec4f, metallic: f32, roughness: f32, normal: vec3f, emissive: vec3f };", "@fragment", "fn main(input: FragmentInput) -> @location(0) vec4f", "{");
    }
    private static void WriteWgslFooter(SourceBuilder code, string surface) => code.Add(
        $"    let material: RekallMaterialSurface = {surface};",
        "    let normal = normalize(material.normal);", "    let lightDirection = normalize(-frame.lightDirection.xyz);",
        "    let diffuse = max(dot(normal, lightDirection), 0.0);",
        "    let dielectric = material.baseColor.rgb * (vec3f(0.08) + diffuse * max(frame.lightColor.rgb, vec3f(0.0)));",
        "    let color = mix(dielectric, material.baseColor.rgb * diffuse, material.metallic) + material.emissive;",
        "    return vec4f(color, material.baseColor.a);", "}");

    private static string MathExpression(string operation, string a, string b, bool wgsl) => operation switch
    { "add" => $"({a} + {b})", "subtract" => $"({a} - {b})", "multiply" => $"({a} * {b})", "divide" => $"({a} / max(abs({b}), 0.000001))", "minimum" => $"min({a}, {b})", "maximum" => $"max({a}, {b})", "power" => $"pow(max({a}, 0.0), {b})", _ => throw new MaterialCompileException("REKALL_MATERIAL_GRAPH_MATH_OPERATION", $"Unsupported math operation '{operation}'.") };
    private static ShaderValue Scalar(double value) { var literal = Number(value); return new(RekallAgeMaterialValueType.Float, literal, literal); }
    private static ShaderValue Color(string value) { if (!TryColor(value, out var color)) throw new MaterialCompileException("REKALL_MATERIAL_GRAPH_COLOR_INVALID", $"Color '{value}' must use #RRGGBB or #RRGGBBAA."); return color; }
    private static bool TryColor(string text, out ShaderValue value)
    {
        value = default; if (text.Length is not (7 or 9) || text[0] != '#') return false;
        if (!uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)) return false;
        if (text.Length == 7) packed = (packed << 8) | 255;
        var r = ((packed >> 24) & 255) / 255d; var g = ((packed >> 16) & 255) / 255d; var b = ((packed >> 8) & 255) / 255d; var a = (packed & 255) / 255d;
        value = new(RekallAgeMaterialValueType.Color, $"vec4({Number(r)}, {Number(g)}, {Number(b)}, {Number(a)})", $"vec4f({Number(r)}, {Number(g)}, {Number(b)}, {Number(a)})"); return true;
    }
    private double ReadDefaultNumber(RekallAgeMaterialGraphNode node, string key, double fallback)
    {
        var descriptor = _catalog.Find(node.TypeId, node.TypeVersion); var defaultNode = descriptor?.Parameters.FirstOrDefault(item => item.ParameterId == key)?.DefaultValue;
        return defaultNode is JsonValue value && value.TryGetValue<double>(out var result) ? result : fallback;
    }
    private double ReadNumber(RekallAgeMaterialGraphNode node, string key, double fallback) => node.Parameters[key] is JsonValue value && value.TryGetValue<double>(out var result) ? result : ReadDefaultNumber(node, key, fallback);
    private static string ReadString(RekallAgeMaterialGraphNode node, string key, string fallback) => node.Parameters[key] is JsonValue value && value.TryGetValue<string>(out var result) ? result : fallback;
    private static (double X, double Y) ReadVector2(RekallAgeMaterialGraphNode node, string key, double fallback) => node.Parameters[key] is JsonArray { Count: 2 } array ? (array[0]!.GetValue<double>(), array[1]!.GetValue<double>()) : (fallback, fallback);
    private static string Number(double value) => value.ToString("0.0################", CultureInfo.InvariantCulture);
    private static string Safe(string value) => string.Concat(value.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'));
    private static RekallAgeCompiledMaterialGraph Failed(RekallAgeMaterialGraphAsset graph, IReadOnlyList<RekallAgeModelingGraphDiagnostic> diagnostics) => new(false, graph.AssetId, graph.Revision, string.Empty, new("glsl", "main", string.Empty, []), new("wgsl", "main", string.Empty, []), [], diagnostics);

    private readonly record struct ShaderValue(RekallAgeMaterialValueType Type, string Glsl, string Wgsl);
    private sealed class MaterialCompileException(string code, string message, string? portId = null) : Exception(message) { public string Code { get; } = code; public string? PortId { get; } = portId; }
    private sealed class SourceBuilder
    {
        private readonly List<string> _lines = []; private readonly List<RekallAgeMaterialShaderSourceMapEntry> _map = [];
        public IReadOnlyList<RekallAgeMaterialShaderSourceMapEntry> Map => _map;
        public void Add(params string[] lines) => _lines.AddRange(lines);
        public void AddMapped(string node, string port, string line) { _lines.Add(line); _map.Add(new(node, port, _lines.Count, _lines.Count)); }
        public string Build() => string.Join("\n", _lines) + "\n";
    }
}
