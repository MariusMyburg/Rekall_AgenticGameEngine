using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanSceneCaptureTests
{
    [Fact]
    public async Task VulkanCaptureCompositesRuntimeUiIntoTheCapturedImage()
    {
        var root = TestPaths.CreateTempDirectory();
        var outputPath = Path.Combine(root, "vulkan-base.png");
        var rgba = Enumerable.Range(0, 64 * 32)
            .SelectMany(_ => new byte[] { 16, 24, 32, 255 })
            .ToArray();
        await RekallAgePngWriter.WriteRgbaAsync(outputPath, 64, 32, rgba, CancellationToken.None);
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            64,
            32,
            null,
            [],
            [
                new RekallAgeRuntimeViewportRenderable(
                    "hud",
                    "HUD",
                    "ui",
                    null,
                    0,
                    0,
                    0,
                    400,
                    UiVisual: new RekallAgeRuntimeViewportUiVisual(
                        "Label",
                        2,
                        2,
                        60,
                        20,
                        0,
                        0,
                        64,
                        32,
                        "HUD",
                        "#20304080",
                        "#ffffffff",
                        "#00000000",
                        0,
                        10,
                        null))
            ],
            1,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
        var capture = new RekallAgeVulkanSceneCaptureResult(
            true,
            outputPath,
            "test",
            null,
            64,
            32,
            "R8G8B8A8_UNorm",
            (ulong)rgba.Length,
            (ulong)rgba.Length,
            new RekallAgeVulkanReadbackPixel(16, 24, 32, 255),
            1,
            0,
            0,
            0,
            0,
            [],
            true,
            true,
            true,
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            [])
        {
            HighFidelityFrame = new RekallAgeHighFidelityFrameReport(
                true,
                "R16G16B16A16_SFloat",
                "R8G8B8A8_UNorm",
                [],
                [new RekallAgeHighFidelityFramePassReport("tone-map", "graphics", [], ["ldr-color"], true, 0, 1)],
                [])
        };

        var composited = await RekallAgeNativeVulkanSceneCapture.CompositeUiOverlayAsync(
            capture,
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            CancellationToken.None);
        var image = await RekallAgePngReader.ReadRgbaAsync(outputPath, CancellationToken.None);

        Assert.NotEqual((ulong)1, composited.ByteChecksum);
        var overlay = new RekallAgeRuntimeSoftwareRenderer().RenderUiOverlayRgba(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty);
        var uiPixel = Enumerable.Range(0, overlay.Length / 4)
            .First(pixel => overlay[pixel * 4 + 3] > 0);
        var offset = uiPixel * 4;
        Assert.Equal(overlay[offset], image.Rgba[offset]);
        Assert.Equal(overlay[offset + 1], image.Rgba[offset + 1]);
        Assert.Equal(overlay[offset + 2], image.Rgba[offset + 2]);
        Assert.Single(Assert.IsType<RekallAgeHighFidelityFrameReport>(composited.HighFidelityFrame).Passes, pass => pass.Name == "ui");
        Assert.Contains(Enumerable.Range(0, image.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return image.Rgba[index] > 200 && image.Rgba[index + 1] > 200 && image.Rgba[index + 2] > 200;
        });
    }

    [Fact]
    public async Task NativeSceneCaptureExecutesCanonicalFrameDrawAndMaterialResourceAbiWhenVulkanIsAvailable()
    {
        var root = TestPaths.CreateTempDirectory();
        var shaderRoot = Path.Combine(root, "Shaders", "agent");
        Directory.CreateDirectory(shaderRoot);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "resourceful.vert"), """
            #version 450
            layout(location = 0) in vec3 inPosition;
            layout(set = 0, binding = 0) uniform FrameUniformBuffer
            {
                mat4 ViewProjection;
                vec4 LightDirection;
                vec4 LightColor;
                vec4 LightPosition;
                vec4 CameraPosition;
            } Frame;
            layout(set = 1, binding = 0) uniform DrawUniformBuffer
            {
                mat4 Model;
                vec4 MaterialFactors;
                vec4 EmissiveFactors;
                vec4 AtmosphereFactors0;
                vec4 AtmosphereFactors1;
                vec4 AtmosphereColor0;
                vec4 AtmosphereColor1;
                vec4 AtmosphereColor2;
                vec4 CloudFactors;
                vec4 CloudColor;
                vec4 CloudShadowFactors;
                vec4 SurfaceWaterFactors;
            } Draw;
            layout(location = 0) out vec2 fragUv;
            void main()
            {
                gl_Position = Frame.ViewProjection * Draw.Model * vec4(inPosition, 1.0);
                fragUv = inPosition.xy * 0.5 + 0.5;
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "resourceful.frag"), """
            #version 450
            layout(location = 0) in vec2 fragUv;
            layout(set = 2, binding = 0) uniform texture2D BaseColorTexture;
            layout(set = 2, binding = 1) uniform sampler BaseColorSampler;
            layout(location = 0) out vec4 outColor;
            void main()
            {
                float sampledAlpha = texture(sampler2D(BaseColorTexture, BaseColorSampler), fragUv).a;
                outColor = vec4(1.0, 0.0, 1.0, sampledAlpha);
            }
            """);
        var pipeline = new RekallAgeRuntimeViewportShaderPipeline("agent/resourceful", "agent/resourceful");
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "resourceful-cube",
            "Resourceful Cube",
            "mesh",
            "rekall.primitive.cube",
            0,
            0,
            0,
            1,
            Variant: "rekall.geometry.cube",
            ShaderPipeline: pipeline));

        var result = await new RekallAgeNativeVulkanSceneCapture(new FakeClearCapture()).CaptureProjectSceneAsync(
            root,
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        if (!result.Captured)
        {
            Assert.NotEmpty(result.Errors);
            Assert.Null(result.SelectedDevice);
            return;
        }

        Assert.True(Assert.Single(result.ShaderPipelines).Valid);
        var image = await RekallAgePngReader.ReadRgbaAsync(result.OutputPath, CancellationToken.None);
        Assert.True(CountMagentaPixels(image) > 0);
    }

    [Fact]
    public async Task NativeSceneCaptureExecutesValidProjectShaderPipelineWhenVulkanIsAvailable()
    {
        var root = TestPaths.CreateTempDirectory();
        var shaderRoot = Path.Combine(root, "Shaders", "agent");
        Directory.CreateDirectory(shaderRoot);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "magenta.vert"), """
            #version 450
            layout(location = 0) in vec3 inPosition;
            void main() { gl_Position = vec4(inPosition, 1.0); }
            """);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "magenta.frag"), """
            #version 450
            layout(location = 0) out vec4 outColor;
            void main() { outColor = vec4(1.0, 0.0, 1.0, 1.0); }
            """);
        var pipeline = new RekallAgeRuntimeViewportShaderPipeline("agent/magenta", "agent/magenta");
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "cube-1",
            "Magenta Cube",
            "mesh",
            "rekall.primitive.cube",
            0,
            0,
            0,
            1,
            Variant: "rekall.geometry.cube",
            ShaderPipeline: pipeline));

        var result = await new RekallAgeNativeVulkanSceneCapture(new FakeClearCapture()).CaptureProjectSceneAsync(
            root,
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        var use = Assert.Single(result.ShaderPipelines);
        Assert.Equal("cube-1", use.EntityId);
        Assert.Equal("agent/magenta", use.VertexShader);
        Assert.True(use.Valid, string.Join(Environment.NewLine, use.Diagnostics));
        Assert.Equal(64, use.ContentHash.Length);
        if (!result.Captured)
        {
            Assert.NotEmpty(result.Errors);
            return;
        }

        var image = await RekallAgePngReader.ReadRgbaAsync(result.OutputPath, CancellationToken.None);
        Assert.True(CountMagentaPixels(image) > 0);
    }

    [Fact]
    public async Task NativeSceneCaptureRejectsInvalidProjectPipelineBeforeGpuWork()
    {
        var root = TestPaths.CreateTempDirectory();
        var shaderRoot = Path.Combine(root, "Shaders", "agent");
        Directory.CreateDirectory(shaderRoot);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "invalid.vert"), """
            #version 450
            layout(location = 0) in vec2 inPosition;
            void main() { gl_Position = vec4(inPosition, 0.0, 1.0); }
            """);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "invalid.frag"), """
            #version 450
            layout(location = 0) out vec4 outColor;
            void main() { outColor = vec4(1.0); }
            """);
        var pipeline = new RekallAgeRuntimeViewportShaderPipeline("agent/invalid", "agent/invalid");
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "bad-cube",
            "Invalid Shader Cube",
            "mesh",
            "rekall.primitive.cube",
            0,
            0,
            0,
            1,
            Variant: "rekall.geometry.cube",
            ShaderPipeline: pipeline));

        var result = await new RekallAgeNativeVulkanSceneCapture(new FakeClearCapture()).CaptureProjectSceneAsync(
            root,
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        Assert.False(result.Captured);
        var use = Assert.Single(result.ShaderPipelines);
        Assert.False(use.Valid);
        Assert.False(use.Fallback);
        Assert.Contains(use.Diagnostics, diagnostic => diagnostic.Contains("REKALL_SHADER_VERTEX_ABI_MISMATCH", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Invalid Shader Cube", StringComparison.Ordinal));
        Assert.False(result.GraphicsPipelineCreated);
    }

    [Fact]
    public async Task NativeSceneCaptureSelectsDefaultAndProjectPipelinesPerDrawWhenVulkanIsAvailable()
    {
        var root = TestPaths.CreateTempDirectory();
        var shaderRoot = Path.Combine(root, "Shaders", "agent");
        Directory.CreateDirectory(shaderRoot);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "magenta.vert"), """
            #version 450
            layout(location = 0) in vec3 inPosition;
            void main() { gl_Position = vec4(inPosition * 0.45 + vec3(-0.45, 0.0, 0.0), 1.0); }
            """);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "magenta.frag"), """
            #version 450
            layout(location = 0) out vec4 outColor;
            void main() { outColor = vec4(1.0, 0.0, 1.0, 1.0); }
            """);
        var pipeline = new RekallAgeRuntimeViewportShaderPipeline("agent/magenta", "agent/magenta");
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "custom-cube", "Custom Cube", "mesh", "rekall.primitive.cube",
                0, 0, 0, 1,
                Variant: "rekall.geometry.cube",
                ShaderPipeline: pipeline),
            new RekallAgeRuntimeViewportRenderable(
                "default-cube", "Default Cube", "mesh", "rekall.primitive.cube",
                1.1, 0, 0, 2,
                Variant: "rekall.geometry.cube",
                MaterialColor: "#00ff00"));

        var result = await new RekallAgeNativeVulkanSceneCapture(new FakeClearCapture()).CaptureProjectSceneAsync(
            root,
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        if (!result.Captured)
        {
            Assert.NotEmpty(result.Errors);
            return;
        }

        Assert.Equal(2, result.DrawCallCount);
        var image = await RekallAgePngReader.ReadRgbaAsync(result.OutputPath, CancellationToken.None);
        Assert.True(CountMagentaPixels(image) > 0);
        Assert.True(CountGreenDominantPixels(image) > 0);
    }

    [Fact]
    public async Task NativeSceneCaptureReportsUnsupportedRenderableKindsWithoutThrowing()
    {
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "sprite-1",
                "Sprite",
                "sprite",
                "asset_sprite",
                0,
                0,
                0,
                1),
            new RekallAgeRuntimeViewportRenderable(
                "sprite-2",
                "Other Sprite",
                "sprite",
                "asset_other",
                1,
                0,
                0,
                2),
            new RekallAgeRuntimeViewportRenderable(
                "mesh-1",
                "Imported Mesh",
                "mesh",
                "robot.glb",
                0,
                0,
                0,
                3));

        var result = await new RekallAgeNativeVulkanSceneCapture(new FakeClearCapture()).CaptureSceneAsync(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        Assert.False(result.Captured);
        Assert.Equal(3, result.UnsupportedRenderableCount);
        Assert.Equal(["mesh", "sprite"], result.UnsupportedRenderableKinds);
        Assert.Contains("does not yet support", string.Join(" ", result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeSceneCaptureUsesClearPassForEmptyFrames()
    {
        var clear = new FakeClearCapture();
        var frame = CreateFrame();

        var result = await new RekallAgeNativeVulkanSceneCapture(clear).CaptureSceneAsync(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "integrated-gpu",
            CancellationToken.None);

        Assert.True(result.Captured);
        Assert.Equal("Fake GPU", result.SelectedDevice?.Name);
        Assert.Equal("integrated-gpu", clear.PreferredDeviceType);
        Assert.True(result.ColorTargetCreated);
        Assert.True(result.RenderPassCreated);
        Assert.False(result.GraphicsPipelineCreated);
    }

    [Fact]
    public async Task NativeSceneCaptureDrawsPrimitiveMeshesWhenVulkanIsAvailable()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "cube-1",
            "Cube",
            "mesh",
            "rekall.primitive.cube",
            0,
            0,
            0,
            1,
            Variant: "rekall.geometry.cube"));

        var result = await new RekallAgeNativeVulkanSceneCapture(new FakeClearCapture()).CaptureSceneAsync(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        Assert.Equal(1, result.MeshCount);
        Assert.True(result.VertexBufferCreated);
        Assert.True(result.IndexBufferCreated);
        if (result.Captured)
        {
            Assert.True(File.Exists(result.OutputPath));
            Assert.True(result.ColorTargetCreated);
            Assert.True(result.DepthTargetCreated);
            Assert.True(result.RenderPassCreated);
            Assert.True(result.FramebufferCreated);
            Assert.True(result.UniformBufferCreated);
            Assert.True(result.DescriptorSetLayoutCreated);
            Assert.True(result.PipelineLayoutCreated);
            Assert.True(result.GraphicsPipelineCreated);
            Assert.Equal(1, result.DrawCallCount);
            Assert.True(result.NonZeroBytes > 0);
            var image = await RekallAgePngReader.ReadRgbaAsync(result.OutputPath, CancellationToken.None);
            Assert.True(CountPixelsDifferentFromClear(image) > 0);
        }
        else
        {
            Assert.NotEmpty(result.Errors);
        }
    }

    [Fact]
    public async Task NativeSceneCaptureDrawsPrimitiveMeshesFromAuthoredCameraWhenVulkanIsAvailable()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera-1",
            "Player Camera",
            "Camera3D",
            true,
            0,
            1.2,
            -5,
            RotationX: 0,
            RotationY: 0,
            ProjectionMode: "perspective",
            FieldOfViewDegrees: 70);
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "cube-1",
            "Cube",
            "mesh",
            "rekall.primitive.cube",
            0,
            1.2,
            0,
            1,
            Variant: "rekall.geometry.cube")) with
        {
            ActiveCamera = camera,
            Cameras = [camera]
        };

        var result = await new RekallAgeNativeVulkanSceneCapture(new FakeClearCapture()).CaptureSceneAsync(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        if (!result.Captured)
        {
            Assert.NotEmpty(result.Errors);
            return;
        }

        var image = await RekallAgePngReader.ReadRgbaAsync(result.OutputPath, CancellationToken.None);
        Assert.True(CountPixelsDifferentFromClear(image) > 0);
    }

    [Fact]
    public async Task NativeSceneCaptureSamplesGpuCompressedRuntimeTexturesWhenVulkanIsAvailable()
    {
        var textureId = "asset_red_bc1";
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "sphere-1",
            "Textured Sphere",
            "mesh",
            "rekall.primitive.sphere",
            0,
            0,
            0,
            1,
            Variant: "rekall.geometry.sphere",
            TextureAssetId: textureId));
        var assets = RekallAgeRuntimeViewportAssetSet.Empty with
        {
            Textures = new Dictionary<string, RekallAgeRuntimeTextureAsset>(StringComparer.Ordinal)
            {
                [textureId] = new RekallAgeRuntimeTextureAsset(
                    textureId,
                    "ktx2",
                    4,
                    4,
                    1,
                    "VK_FORMAT_BC1_RGB_UNORM_BLOCK",
                    null,
                    true,
                    [new RekallAgeRuntimeTextureMipLevel(0, 4, 4, [0x00, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00])])
            }
        };

        var result = await new RekallAgeNativeVulkanSceneCapture(new FakeClearCapture()).CaptureSceneAsync(
            frame,
            assets,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        if (!result.Captured)
        {
            Assert.NotEmpty(result.Errors);
            return;
        }

        var image = await RekallAgePngReader.ReadRgbaAsync(result.OutputPath, CancellationToken.None);
        Assert.True(CountRedDominantPixels(image) > 0);
    }

    [Theory]
    [InlineData("VK_FORMAT_BC1_RGB_SRGB_BLOCK", "BC1RgbSrgbBlock")]
    [InlineData("VK_FORMAT_BC3_UNORM_BLOCK", "BC3UnormBlock")]
    [InlineData("VK_FORMAT_BC7_SRGB_BLOCK", "BC7SrgbBlock")]
    public void VulkanCompressedTextureFormatMapperResolvesKtxAndDdsBlockFormats(
        string format,
        string expected)
    {
        Assert.True(RekallAgeVulkanTextureFormatMapper.TryMapBlockCompressedFormat(format, out var actual));
        Assert.Equal(expected, actual.ToString());
    }

    private static int CountPixelsDifferentFromClear(RekallAgeRgbaImage image)
    {
        var changed = 0;
        for (var offset = 0; offset + 3 < image.Rgba.Length; offset += 4)
        {
            if (Math.Abs(image.Rgba[offset + 0] - 20) > 2
                || Math.Abs(image.Rgba[offset + 1] - 26) > 2
                || Math.Abs(image.Rgba[offset + 2] - 36) > 2)
            {
                changed++;
            }
        }

        return changed;
    }

    private static int CountRedDominantPixels(RekallAgeRgbaImage image)
    {
        var changed = 0;
        for (var offset = 0; offset + 3 < image.Rgba.Length; offset += 4)
        {
            if (image.Rgba[offset] > image.Rgba[offset + 1] + 20
                && image.Rgba[offset] > image.Rgba[offset + 2] + 20)
            {
                changed++;
            }
        }

        return changed;
    }

    private static int CountMagentaPixels(RekallAgeRgbaImage image)
    {
        var changed = 0;
        for (var offset = 0; offset + 3 < image.Rgba.Length; offset += 4)
        {
            if (image.Rgba[offset] > 220
                && image.Rgba[offset + 1] < 30
                && image.Rgba[offset + 2] > 220)
            {
                changed++;
            }
        }

        return changed;
    }

    private static int CountGreenDominantPixels(RekallAgeRgbaImage image)
    {
        var changed = 0;
        for (var offset = 0; offset + 3 < image.Rgba.Length; offset += 4)
        {
            if (image.Rgba[offset + 1] > image.Rgba[offset] + 20
                && image.Rgba[offset + 1] > image.Rgba[offset + 2] + 20)
            {
                changed++;
            }
        }

        return changed;
    }

    private static RekallAgeRuntimeViewportFrame CreateFrame(params RekallAgeRuntimeViewportRenderable[] renderables)
    {
        return new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            64,
            64,
            null,
            [],
            renderables,
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
    }

    private sealed class FakeClearCapture : IRekallAgeVulkanRenderPassCapture
    {
        public string? PreferredDeviceType { get; private set; }

        public ValueTask<RekallAgeVulkanRenderPassCaptureResult> CaptureClearRenderPassAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            string outputDirectory,
            RekallAgeVulkanClearColor clearColor,
            CancellationToken cancellationToken)
        {
            PreferredDeviceType = preferredDeviceType;
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, "clear.png");
            File.WriteAllBytes(outputPath, [1, 2, 3, 4]);
            return ValueTask.FromResult(new RekallAgeVulkanRenderPassCaptureResult(
                true,
                outputPath,
                "fake-vulkan",
                new RekallAgeVulkanSelectedDevice(
                    "Fake GPU",
                    "integrated-gpu",
                    "1.3.0",
                    new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 1)),
                width,
                height,
                format,
                clearColor,
                width * height * 4,
                4,
                new RekallAgeVulkanReadbackPixel(1, 2, 3, 4),
                10,
                []));
        }
    }
}
