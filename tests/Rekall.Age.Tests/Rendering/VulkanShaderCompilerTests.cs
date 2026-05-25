using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanShaderCompilerTests
{
    [Fact]
    public void SceneShadersAreCopiedBesideTestHostForBundledRuntimeUse()
    {
        var shaderDirectory = Path.Combine(AppContext.BaseDirectory, "Shaders");

        Assert.True(File.Exists(Path.Combine(shaderDirectory, "rekall_scene.vert")));
        Assert.True(File.Exists(Path.Combine(shaderDirectory, "rekall_scene.frag")));
    }

    [Fact]
    public void CompileDefaultSceneShadersProducesSpirvModules()
    {
        var compiler = new RekallAgeVulkanShaderCompiler();

        var result = compiler.CompileScenePipeline(RekallAgeVulkanScenePipelineDescription.Default);

        Assert.True(result.Compiled, string.Join(" ", result.Errors));
        Assert.Empty(result.Errors);
        Assert.EndsWith("rekall_scene.vert", result.Vertex.SourcePath, StringComparison.Ordinal);
        Assert.EndsWith("rekall_scene.frag", result.Fragment.SourcePath, StringComparison.Ordinal);
        Assert.True(result.Vertex.Spirv.Length > 0);
        Assert.True(result.Fragment.Spirv.Length > 0);
        Assert.Equal(0, result.Vertex.Spirv.Length % 4);
        Assert.Equal(0, result.Fragment.Spirv.Length % 4);
    }
}
