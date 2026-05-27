using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanScenePipelineDescriptionTests
{
    [Fact]
    public void DefaultPipelineDeclaresShaderVertexDescriptorPushConstantAndDepthContracts()
    {
        var pipeline = RekallAgeVulkanScenePipelineDescription.Default;

        Assert.EndsWith("rekall_scene.vert", pipeline.VertexShaderPath, StringComparison.Ordinal);
        Assert.EndsWith("rekall_scene.frag", pipeline.FragmentShaderPath, StringComparison.Ordinal);
        Assert.True(pipeline.DepthTestEnabled);
        Assert.True(pipeline.TextureSamplingEnabled);
        Assert.True(pipeline.AlphaBlendingEnabled);
        Assert.Equal(["position", "normal", "color", "uv"], pipeline.VertexAttributes.Select(attribute => attribute.Name));
        Assert.Contains(pipeline.DescriptorBindings, binding =>
            binding.Name == "FrameUniform"
            && binding.Binding == 0
            && binding.DescriptorType == "uniform-buffer"
            && binding.ShaderStage == "vertex+fragment");
        Assert.Contains(pipeline.DescriptorBindings, binding =>
            binding.Name == "BaseColorTexture"
            && binding.Binding == 1
            && binding.DescriptorType == "combined-image-sampler"
            && binding.ShaderStage == "fragment");
        Assert.Contains(pipeline.DescriptorBindings, binding =>
            binding.Name == "EmissiveTexture"
            && binding.Binding == 5
            && binding.DescriptorType == "combined-image-sampler"
            && binding.ShaderStage == "fragment");
        Assert.Contains(pipeline.DescriptorBindings, binding =>
            binding.Name == "CloudShadowTexture"
            && binding.Binding == 6
            && binding.DescriptorType == "combined-image-sampler"
            && binding.ShaderStage == "fragment");
        Assert.Contains(pipeline.DescriptorBindings, binding =>
            binding.Name == "SurfaceWaterTexture"
            && binding.Binding == 7
            && binding.DescriptorType == "combined-image-sampler"
            && binding.ShaderStage == "fragment");
        Assert.True(pipeline.PushConstantBytes >= 240);
    }

    [Fact]
    public void ShaderSourcesDeclareExpectedInputsAndDescriptors()
    {
        var root = FindRepositoryRoot();
        var shaderDirectory = Path.Combine(root, "src", "Rekall.Age.Rendering", "Shaders");
        var vertex = File.ReadAllText(Path.Combine(shaderDirectory, "rekall_scene.vert"));
        var fragment = File.ReadAllText(Path.Combine(shaderDirectory, "rekall_scene.frag"));

        Assert.Contains("layout(location = 0) in vec3 inPosition;", vertex);
        Assert.Contains("layout(location = 1) in vec3 inNormal;", vertex);
        Assert.Contains("layout(location = 2) in vec4 inColor;", vertex);
        Assert.Contains("layout(location = 3) in vec2 inUv;", vertex);
        Assert.Contains("layout(set = 0, binding = 0) uniform FrameUniform", vertex);
        Assert.Contains("mat4 viewProjection;", vertex);
        Assert.Contains("layout(push_constant) uniform DrawPushConstants", vertex);
        Assert.Contains("mat4 model;", vertex);
        Assert.Contains("layout(set = 0, binding = 0) uniform FrameUniform", fragment);
        Assert.Contains("layout(set = 0, binding = 1) uniform sampler2D baseColorTexture;", fragment);
        Assert.Contains("layout(set = 0, binding = 5) uniform sampler2D emissiveTexture;", fragment);
        Assert.Contains("layout(set = 0, binding = 6) uniform sampler2D cloudShadowTexture;", fragment);
        Assert.Contains("layout(set = 0, binding = 7) uniform sampler2D surfaceWaterTexture;", fragment);
        Assert.Contains("vec4 lightPosition;", fragment);
        Assert.Contains("frame.lightPosition.w > 0.5", fragment);
        Assert.Contains("vec4 emissiveFactors;", fragment);
        Assert.Contains("vec4 atmosphereFactors0;", fragment);
        Assert.Contains("vec4 atmosphereColor0;", fragment);
        Assert.Contains("vec4 atmosphereColor1;", fragment);
        Assert.Contains("vec4 atmosphereColor2;", fragment);
        Assert.Contains("vec4 cloudFactors;", fragment);
        Assert.Contains("vec4 cloudColor;", fragment);
        Assert.Contains("renderCloudLayer", fragment);
        Assert.Contains("cloudAlphaFromTextureOnly", fragment);
        Assert.Contains("cloudSkyVisibility", fragment);
        Assert.Contains("vec4 cloudShadowFactors;", fragment);
        Assert.Contains("vec4 surfaceWaterFactors;", fragment);
        Assert.Contains("sampleCloudShadow", fragment);
        Assert.Contains("sampleSurfaceWaterCoverage", fragment);
        Assert.Contains("surfaceWaterSpecularStrength", fragment);
        Assert.Contains("phaseRayleigh", fragment);
        Assert.Contains("integrateOpticalDepth", fragment);
        Assert.Contains("surfaceAtmosphereTransmittance", fragment);
        Assert.Contains("applySurfaceAerialPerspective", fragment);
        Assert.Contains("surfaceAerialPerspectiveScattering", fragment);
        Assert.Contains("aerialPerspectiveStrength", fragment);
        Assert.Contains("spaceAmbientFloor", fragment);
        Assert.Contains("atmosphereLightColor", fragment);
        Assert.Contains("planetShadowFactor", fragment);
        Assert.Contains("ozoneAbsorption", fragment);
        Assert.Contains("shouldDiscardAtmosphereBackHemisphere", fragment);
        Assert.Contains("discard;", fragment);
        Assert.Contains("vec3 emissive =", fragment);
        Assert.Contains("mat4 viewProjection;", fragment);
        Assert.Contains("layout(location = 0) out vec4 outColor;", fragment);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Rekall.AGE.sln from the test output directory.");
    }
}
