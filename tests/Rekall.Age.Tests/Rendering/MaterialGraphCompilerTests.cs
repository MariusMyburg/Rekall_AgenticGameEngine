using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class MaterialGraphCompilerTests
{
    [Fact]
    public void CompileProducesDeterministicMappedGlslAndWgslAndPhysicallyCompilesGlsl()
    {
        var graph = RekallAgeMaterialGraphAsset.Create(
            "compiled-stone", "Compiled Stone",
            [
                new("color", "rekall.material.constant.color", 1, new JsonObject { ["value"] = "#336699" }),
                new("roughness", "rekall.material.constant.float", 1, new JsonObject { ["value"] = 0.42 }),
                new("pbr", "rekall.material.surface.pbr", 1, new JsonObject { ["metallic"] = 0.15 }),
                new("output", "rekall.material.output", 1, new JsonObject())
            ],
            [
                new("color-pbr", "color", "color", "pbr", "baseColor"),
                new("rough-pbr", "roughness", "value", "pbr", "roughness"),
                new("pbr-output", "pbr", "surface", "output", "surface")
            ], new("surface", "output", "surface"));
        var compiler = new RekallAgeMaterialGraphCompiler();

        var first = compiler.Compile(graph);
        var second = compiler.Compile(graph);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(first.Glsl.Source, second.Glsl.Source);
        Assert.Equal(first.Wgsl.Source, second.Wgsl.Source);
        Assert.Contains("vec4(0.2, 0.4, 0.6, 1.0)", first.Glsl.Source);
        Assert.Contains("0.42", first.Glsl.Source);
        Assert.Contains("@fragment", first.Wgsl.Source);
        Assert.Contains(first.Glsl.SourceMap, item => item.NodeId == "pbr" && item.PortId == "surface");
        Assert.Contains(first.Wgsl.SourceMap, item => item.NodeId == "output" && item.PortId == "surface");

        var spirv = new RekallAgeVulkanShaderCompiler().CompileSource(
            first.Glsl.Source, "compiled-stone.generated.frag", RekallAgeVulkanShaderStage.Fragment);
        Assert.NotEmpty(spirv.Spirv);
    }

    [Fact]
    public void TextureNodesReceiveStablePortableResourcePairs()
    {
        var graph = RekallAgeMaterialGraphAsset.Create(
            "textured", "Textured",
            [
                new("uv", "rekall.material.coordinate.uv", 1, new JsonObject()),
                new("texture", "rekall.material.texture.sample", 1, new JsonObject { ["textureAssetId"] = "textures/stone.png" }),
                new("pbr", "rekall.material.surface.pbr", 1, new JsonObject()),
                new("output", "rekall.material.output", 1, new JsonObject())
            ],
            [
                new("uv-texture", "uv", "uv", "texture", "uv"),
                new("texture-pbr", "texture", "color", "pbr", "baseColor"),
                new("pbr-output", "pbr", "surface", "output", "surface")
            ], new("surface", "output", "surface"));

        var compiled = new RekallAgeMaterialGraphCompiler().Compile(graph);

        var resource = Assert.Single(compiled.Resources);
        Assert.Equal("textures/stone.png", resource.TextureAssetId);
        Assert.Equal(0, resource.TextureBinding);
        Assert.Equal(1, resource.SamplerBinding);
        Assert.Contains("layout(set = 2, binding = 0) uniform texture2D materialTexture0", compiled.Glsl.Source);
        Assert.Contains("@group(2) @binding(1) var materialSampler0", compiled.Wgsl.Source);
    }
}
