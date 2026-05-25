using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanSceneCaptureTests
{
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
