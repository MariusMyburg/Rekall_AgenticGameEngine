using Rekall.Age.Rendering;
using Silk.NET.Vulkan;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanShaderCompilerTests
{
    [Fact]
    public void ShadowDepthAtlasRequiresSamplingAndFitsDeviceArrayLimits()
    {
        var missingSampling = RekallAgeVulkanHighFidelityFormatValidator.ValidateShadowDepthFormat(
            FormatFeatureFlags.DepthStencilAttachmentBit | FormatFeatureFlags.SampledImageBit);
        var supported = RekallAgeVulkanHighFidelityFormatValidator.ValidateShadowDepthFormat(
            FormatFeatureFlags.DepthStencilAttachmentBit
                | FormatFeatureFlags.SampledImageBit
                | FormatFeatureFlags.SampledImageFilterLinearBit
                | FormatFeatureFlags.TransferSrcBit);
        var exceedsLimits = RekallAgeVulkanHighFidelityFormatValidator.ValidateShadowAtlasLimits(
            requestedResolution: 4096,
            requestedLayers: 4,
            maximumResolution: 2048,
            maximumLayers: 2);

        Assert.NotNull(missingSampling);
        Assert.StartsWith("REKALL_RENDER_FORMAT_UNSUPPORTED:", missingSampling, StringComparison.Ordinal);
        Assert.Contains(nameof(FormatFeatureFlags.SampledImageFilterLinearBit), missingSampling, StringComparison.Ordinal);
        Assert.Null(supported);
        Assert.NotNull(exceedsLimits);
        Assert.StartsWith("REKALL_SHADOW_ATLAS_LIMIT_EXCEEDED:", exceedsLimits, StringComparison.Ordinal);
        Assert.Contains("4096", exceedsLimits, StringComparison.Ordinal);
        Assert.Contains("4", exceedsLimits, StringComparison.Ordinal);
        Assert.Contains("2048", exceedsLimits, StringComparison.Ordinal);
        Assert.Contains("2", exceedsLimits, StringComparison.Ordinal);
    }

    [Fact]
    public void HighFidelityHalfFloatSamplingRequiresLinearFilterCapability()
    {
        var missingLinear = RekallAgeVulkanHighFidelityFormatValidator.ValidateOptimalTilingFeatures(
            Format.R16G16B16A16Sfloat,
            FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit,
            FormatFeatureFlags.ColorAttachmentBit
                | FormatFeatureFlags.SampledImageBit
                | FormatFeatureFlags.SampledImageFilterLinearBit,
            "scene-hdr");
        var supported = RekallAgeVulkanHighFidelityFormatValidator.ValidateOptimalTilingFeatures(
            Format.R16G16B16A16Sfloat,
            FormatFeatureFlags.ColorAttachmentBit
                | FormatFeatureFlags.SampledImageBit
                | FormatFeatureFlags.SampledImageFilterLinearBit,
            FormatFeatureFlags.ColorAttachmentBit
                | FormatFeatureFlags.SampledImageBit
                | FormatFeatureFlags.SampledImageFilterLinearBit,
            "scene-hdr");

        Assert.NotNull(missingLinear);
        Assert.StartsWith("REKALL_RENDER_FORMAT_UNSUPPORTED:", missingLinear, StringComparison.Ordinal);
        Assert.Contains(nameof(FormatFeatureFlags.SampledImageFilterLinearBit), missingLinear, StringComparison.Ordinal);
        Assert.Null(supported);
    }

    [Fact]
    public void FroxelFormatPreflightRequiresStorageSamplingAndDebugTransferSource()
    {
        var missingTransferSource = RekallAgeVulkanHighFidelityFormatValidator.ValidateFogFroxelFormat(
            FormatFeatureFlags.StorageImageBit | FormatFeatureFlags.SampledImageBit);
        var supported = RekallAgeVulkanHighFidelityFormatValidator.ValidateFogFroxelFormat(
            FormatFeatureFlags.StorageImageBit
                | FormatFeatureFlags.SampledImageBit
                | FormatFeatureFlags.TransferSrcBit);

        Assert.NotNull(missingTransferSource);
        Assert.Contains(nameof(FormatFeatureFlags.TransferSrcBit), missingTransferSource, StringComparison.Ordinal);
        Assert.Null(supported);
    }

    [Fact]
    public void SceneShadersAreCopiedBesideTestHostForBundledRuntimeUse()
    {
        var shaderDirectory = Path.Combine(AppContext.BaseDirectory, "Shaders");

        Assert.True(File.Exists(Path.Combine(shaderDirectory, "rekall_scene.vert")));
        Assert.True(File.Exists(Path.Combine(shaderDirectory, "rekall_scene.frag")));
        Assert.True(File.Exists(Path.Combine(shaderDirectory, "rekall_bloom.comp")));
        Assert.True(File.Exists(Path.Combine(shaderDirectory, "rekall_tonemap.frag")));
        Assert.True(File.Exists(Path.Combine(shaderDirectory, "rekall_particles.comp")));
        Assert.True(File.Exists(Path.Combine(shaderDirectory, "rekall_particles.vert")));
        Assert.True(File.Exists(Path.Combine(shaderDirectory, "rekall_particles.frag")));
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

    [Fact]
    public void SceneNormalMappingGuardsDegenerateUvDerivativesBeforeNormalization()
    {
        var compiler = new RekallAgeVulkanShaderCompiler();
        var source = File.ReadAllText(compiler.ResolveShaderPath(Path.Combine("Shaders", "rekall_scene.frag")));

        Assert.Contains("float determinant = st1.s * st2.t - st1.t * st2.s;", source, StringComparison.Ordinal);
        Assert.Contains("if (abs(determinant) <= 0.0000001)", source, StringComparison.Ordinal);
        Assert.Contains("tangentRaw - normal * dot(normal, tangentRaw)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileHighFidelityPostShadersProducesComputeAndGraphicsSpirvModules()
    {
        var result = new RekallAgeVulkanShaderCompiler().CompileHighFidelityPostPipeline();

        Assert.True(result.Compiled, string.Join(" ", result.Errors));
        Assert.Empty(result.Errors);
        Assert.Equal(RekallAgeVulkanShaderStage.Compute, result.Bloom.Stage);
        Assert.EndsWith("rekall_bloom.comp", result.Bloom.SourcePath, StringComparison.Ordinal);
        Assert.Equal(RekallAgeVulkanShaderStage.Fragment, result.Ssao.Stage);
        Assert.EndsWith("rekall_ssao.frag", result.Ssao.SourcePath, StringComparison.Ordinal);
        Assert.Equal(RekallAgeVulkanShaderStage.Vertex, result.FullscreenVertex.Stage);
        Assert.Equal(RekallAgeVulkanShaderStage.Fragment, result.ToneMap.Stage);
        Assert.EndsWith("rekall_tonemap.frag", result.ToneMap.SourcePath, StringComparison.Ordinal);
        Assert.All(
            [result.Ssao.Spirv, result.Bloom.Spirv, result.FullscreenVertex.Spirv, result.ToneMap.Spirv],
            spirv =>
            {
                Assert.NotEmpty(spirv);
                Assert.Equal(0, spirv.Length % 4);
            });
    }

    [Fact]
    public void CompilerBuildsGpuParticleSimulationAndHdrDrawShaders()
    {
        var result = new RekallAgeVulkanShaderCompiler().CompileParticlePipeline();

        Assert.True(result.Compiled, string.Join(Environment.NewLine, result.Errors));
        Assert.NotEmpty(result.Compute.Spirv);
        Assert.NotEmpty(result.Vertex.Spirv);
        Assert.NotEmpty(result.Fragment.Spirv);
        Assert.EndsWith("rekall_particles.comp", result.Compute.SourcePath, StringComparison.Ordinal);
        Assert.EndsWith("rekall_particles.vert", result.Vertex.SourcePath, StringComparison.Ordinal);
        Assert.EndsWith("rekall_particles.frag", result.Fragment.SourcePath, StringComparison.Ordinal);
    }
}
