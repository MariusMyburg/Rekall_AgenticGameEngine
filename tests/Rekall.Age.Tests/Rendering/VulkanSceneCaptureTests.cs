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
