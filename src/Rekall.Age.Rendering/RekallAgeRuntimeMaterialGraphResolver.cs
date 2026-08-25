using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Rendering;

/// <summary>
/// Resolves the portable, static portion of an AGE material graph into the
/// standard runtime PBR binding used by built-in render paths. Dynamic/custom
/// graph shaders remain available through the material compiler.
/// </summary>
public sealed class RekallAgeRuntimeMaterialGraphResolver
{
    public RekallAgeRuntimeMaterialAsset Resolve(RekallAgeMaterialGraphAsset graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var nodes = graph.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var links = graph.Links.ToArray();
        var outputLink = links.SingleOrDefault(link =>
            link.ToNodeId == graph.Output.NodeId && link.ToPortId == "surface")
            ?? throw new InvalidDataException("REKALL_RENDER_MATERIAL_OUTPUT_MISSING: Material graph output has no surface input.");
        if (!nodes.TryGetValue(outputLink.FromNodeId, out var surface))
        {
            throw new InvalidDataException("REKALL_RENDER_MATERIAL_SURFACE_MISSING: Material graph surface node was not found.");
        }

        return surface.TypeId switch
        {
            "rekall.material.surface.pbr" => ResolvePbr(graph.AssetId, surface, nodes, links),
            "rekall.material.surface.emissive" => ResolveEmissive(graph.AssetId, surface, nodes, links),
            _ => throw new InvalidDataException($"REKALL_RENDER_MATERIAL_SURFACE_UNSUPPORTED: Surface node type '{surface.TypeId}' is not supported by the standard PBR runtime path.")
        };
    }

    private static RekallAgeRuntimeMaterialAsset ResolvePbr(
        string assetId,
        RekallAgeMaterialGraphNode surface,
        IReadOnlyDictionary<string, RekallAgeMaterialGraphNode> nodes,
        IReadOnlyList<RekallAgeMaterialGraphLink> links)
    {
        var normalNode = SourceNode(surface.NodeId, "normal", nodes, links);
        var normalTexture = normalNode?.TypeId == "rekall.material.normal.map"
            ? TextureFromInput(normalNode.NodeId, "color", nodes, links)
            : null;
        var normalScale = normalNode?.TypeId == "rekall.material.normal.map"
            ? ReadFloat(normalNode, "strength", 1, 0, 4)
            : 1;
        var baseColorSource = SourceNode(surface.NodeId, "baseColor", nodes, links);
        var emissiveSource = SourceNode(surface.NodeId, "emissive", nodes, links);
        var emissiveStrength = ReadFloat(surface, "emissiveStrength", 0, 0, 1_000_000);
        var emissiveColor = emissiveSource?.TypeId == "rekall.material.texture.sample"
            ? Vector4.One
            : ResolveColor(surface.NodeId, "emissive", surface, nodes, links, "#000000");

        return new(assetId)
        {
            BaseColorTextureAssetId = TextureFromNode(baseColorSource),
            NormalTextureAssetId = normalTexture,
            EmissiveTextureAssetId = TextureFromNode(emissiveSource),
            BaseColorFactor = baseColorSource?.TypeId == "rekall.material.texture.sample"
                ? Vector4.One
                : ResolveColor(surface.NodeId, "baseColor", surface, nodes, links, "#ffffff"),
            MetallicFactor = ResolveFloat(surface.NodeId, "metallic", surface, nodes, links, 0, 0, 1),
            RoughnessFactor = ResolveFloat(surface.NodeId, "roughness", surface, nodes, links, 1, 0.04f, 1),
            NormalScale = normalScale,
            EmissiveFactor = new(emissiveColor.X, emissiveColor.Y, emissiveColor.Z, emissiveStrength)
        };
    }

    private static RekallAgeRuntimeMaterialAsset ResolveEmissive(
        string assetId,
        RekallAgeMaterialGraphNode surface,
        IReadOnlyDictionary<string, RekallAgeMaterialGraphNode> nodes,
        IReadOnlyList<RekallAgeMaterialGraphLink> links)
    {
        var colorSource = SourceNode(surface.NodeId, "color", nodes, links);
        var color = colorSource?.TypeId == "rekall.material.texture.sample"
            ? Vector4.One
            : ResolveColor(surface.NodeId, "color", surface, nodes, links, "#ffffff");
        var strength = ResolveFloat(surface.NodeId, "strength", surface, nodes, links, 1, 0, 1_000_000);
        return new(assetId)
        {
            EmissiveTextureAssetId = TextureFromNode(colorSource),
            EmissiveFactor = new(color.X, color.Y, color.Z, strength)
        };
    }

    private static RekallAgeMaterialGraphNode? SourceNode(
        string targetNodeId,
        string targetPortId,
        IReadOnlyDictionary<string, RekallAgeMaterialGraphNode> nodes,
        IReadOnlyList<RekallAgeMaterialGraphLink> links)
    {
        var link = links.SingleOrDefault(item => item.ToNodeId == targetNodeId && item.ToPortId == targetPortId);
        return link is not null && nodes.TryGetValue(link.FromNodeId, out var source) ? source : null;
    }

    private static string? TextureFromInput(
        string targetNodeId,
        string targetPortId,
        IReadOnlyDictionary<string, RekallAgeMaterialGraphNode> nodes,
        IReadOnlyList<RekallAgeMaterialGraphLink> links) =>
        TextureFromNode(SourceNode(targetNodeId, targetPortId, nodes, links));

    private static string? TextureFromNode(RekallAgeMaterialGraphNode? node) =>
        node?.TypeId == "rekall.material.texture.sample" ? ReadString(node, "textureAssetId") : null;

    private static float ResolveFloat(
        string targetNodeId,
        string targetPortId,
        RekallAgeMaterialGraphNode target,
        IReadOnlyDictionary<string, RekallAgeMaterialGraphNode> nodes,
        IReadOnlyList<RekallAgeMaterialGraphLink> links,
        float fallback,
        float minimum,
        float maximum)
    {
        var source = SourceNode(targetNodeId, targetPortId, nodes, links);
        return source?.TypeId == "rekall.material.constant.float"
            ? ReadFloat(source, "value", fallback, minimum, maximum)
            : ReadFloat(target, targetPortId, fallback, minimum, maximum);
    }

    private static Vector4 ResolveColor(
        string targetNodeId,
        string targetPortId,
        RekallAgeMaterialGraphNode target,
        IReadOnlyDictionary<string, RekallAgeMaterialGraphNode> nodes,
        IReadOnlyList<RekallAgeMaterialGraphLink> links,
        string fallback)
    {
        var source = SourceNode(targetNodeId, targetPortId, nodes, links);
        return source?.TypeId == "rekall.material.constant.color"
            ? ParseColor(ReadString(source, "value") ?? fallback)
            : ParseColor(ReadString(target, targetPortId) ?? fallback);
    }

    private static string? ReadString(RekallAgeMaterialGraphNode node, string name) =>
        node.Parameters[name] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result?.Trim()
            : null;

    private static float ReadFloat(
        RekallAgeMaterialGraphNode node,
        string name,
        float fallback,
        float minimum,
        float maximum)
    {
        var result = node.Parameters[name] is JsonValue value && value.TryGetValue<double>(out var number)
            ? number
            : fallback;
        return (float)Math.Clamp(double.IsFinite(result) ? result : fallback, minimum, maximum);
    }

    private static Vector4 ParseColor(string value)
    {
        var text = value.Trim().TrimStart('#');
        if (text.Length is not 6 and not 8
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return Vector4.One;
        }

        if (text.Length == 6) packed = (packed << 8) | 0xff;
        return new(
            ((packed >> 24) & 0xff) / 255f,
            ((packed >> 16) & 0xff) / 255f,
            ((packed >> 8) & 0xff) / 255f,
            (packed & 0xff) / 255f);
    }
}
