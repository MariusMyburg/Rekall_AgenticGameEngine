using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanHighFidelityCaptureTests
{
    [Fact]
    public async Task BloomDisabledGraphDoesNotAllocateDispatchOrReportBloom()
    {
        var frame = EmissiveFrame();
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("Performance", Bloom: false),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("native-test"),
            frame.Width,
            frame.Height);
        frame = frame with
        {
            ResolvedQualityPlan = quality,
            PostProcessStack = new RekallAgeRuntimeViewportPostProcessStack(
                "post",
                "Tone Map Only",
                true,
                [new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")])
        };

        var result = await new RekallAgeNativeVulkanSceneCapture().CaptureSceneAsync(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(result.Captured, string.Join(Environment.NewLine, result.Errors));
        var report = Assert.IsType<RekallAgeHighFidelityFrameReport>(result.HighFidelityFrame);
        Assert.True(report.Executed, string.Join(Environment.NewLine, report.Diagnostics));
        Assert.DoesNotContain(report.Resources, resource => resource.Name == "bloom-pyramid");
        Assert.DoesNotContain(report.Passes, pass => pass.Name == "bloom");
        Assert.Contains(report.Passes, pass => pass.Name == "tone-map" && pass.Executed);
    }

    [Fact]
    public async Task ResolvedRenderScaleControlsHdrSceneExtentWhileOutputStaysViewportSized()
    {
        var frame = EmissiveFrame();
        var capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline("native-test");
        var fullQuality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High", ResolutionScale: 1, Bloom: false),
            capabilities,
            frame.Width,
            frame.Height);
        var scaledQuality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High", ResolutionScale: 0.5, Bloom: false),
            capabilities,
            frame.Width,
            frame.Height);
        var post = new RekallAgeRuntimeViewportPostProcessStack(
            "post",
            "HDR Post",
            true,
            [new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")]);
        var output = TestPaths.CreateTempDirectory();
        var capture = new RekallAgeNativeVulkanSceneCapture();

        var full = await capture.CaptureSceneAsync(
            frame with { ResolvedQualityPlan = fullQuality, PostProcessStack = post },
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);
        var scaled = await capture.CaptureSceneAsync(
            frame with { ResolvedQualityPlan = scaledQuality, PostProcessStack = post },
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(full.Captured, string.Join(Environment.NewLine, full.Errors));
        Assert.True(scaled.Captured, string.Join(Environment.NewLine, scaled.Errors));
        Assert.NotEqual(full.ByteChecksum, scaled.ByteChecksum);
        var scaledReport = Assert.IsType<RekallAgeHighFidelityFrameReport>(scaled.HighFidelityFrame);
        Assert.Contains(scaledReport.Resources, resource => resource is
        { Name: "scene-hdr", Width: 48, Height: 32, Allocated: true });
        Assert.Contains(scaledReport.Resources, resource => resource is
        { Name: "ldr-color", Width: 96, Height: 64, Allocated: true });
        var outputImage = await RekallAgePngReader.ReadRgbaAsync(scaled.OutputPath, CancellationToken.None);
        Assert.Equal(96, outputImage.Width);
        Assert.Equal(64, outputImage.Height);
    }

    [Fact]
    public async Task AuthoredBloomRadiusChangesNativeBloomReconstruction()
    {
        var frame = EmissiveFrame();
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High", Bloom: true),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("native-test"),
            frame.Width,
            frame.Height);
        var output = TestPaths.CreateTempDirectory();
        var capture = new RekallAgeNativeVulkanSceneCapture();

        var narrow = await capture.CaptureSceneAsync(
            frame with
            {
                ResolvedQualityPlan = quality,
                PostProcessStack = BloomPostStack(radius: 0.5)
            },
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);
        var wide = await capture.CaptureSceneAsync(
            frame with
            {
                ResolvedQualityPlan = quality,
                PostProcessStack = BloomPostStack(radius: 3)
            },
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(narrow.Captured, string.Join(Environment.NewLine, narrow.Errors));
        Assert.True(wide.Captured, string.Join(Environment.NewLine, wide.Errors));
        Assert.NotEqual(narrow.ByteChecksum, wide.ByteChecksum);
    }

    [Fact]
    public async Task NativeFrameReportSurfacesAuthoredPostDegradationFacts()
    {
        var frame = EmissiveFrame();
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("Performance", Bloom: false),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("native-test"),
            frame.Width,
            frame.Height);
        frame = frame with
        {
            ResolvedQualityPlan = quality,
            PostProcessStack = new RekallAgeRuntimeViewportPostProcessStack(
                "post",
                "Inspectable Post",
                true,
                [
                    new RekallAgeRuntimeViewportPostProcessPass("Lens", "vignette"),
                    new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")
                ])
        };

        var result = await new RekallAgeNativeVulkanSceneCapture().CaptureSceneAsync(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            TestPaths.CreateTempDirectory(),
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(result.Captured, string.Join(Environment.NewLine, result.Errors));
        var report = Assert.IsType<RekallAgeHighFidelityFrameReport>(result.HighFidelityFrame);
        Assert.Contains(
            report.Diagnostics,
            diagnostic => diagnostic.Contains("REKALL_RENDER_POST_PASS_UNSUPPORTED", StringComparison.Ordinal)
                && diagnostic.Contains("post.pass[0].type", StringComparison.Ordinal)
                && diagnostic.Contains("requested='vignette'", StringComparison.Ordinal)
                && diagnostic.Contains("resolved='ignored'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HighProfileExecutesAuthoredBloomAndToneMappingInsteadOfReturningLegacyPixels()
    {
        var legacyFrame = EmissiveFrame();
        var resolvedPlan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High", Bloom: true),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("native-test"),
            legacyFrame.Width,
            legacyFrame.Height);
        var highFrame = legacyFrame with
        {
            ResolvedQualityPlan = resolvedPlan,
            PostProcessStack = new RekallAgeRuntimeViewportPostProcessStack(
                "post",
                "HDR Post",
                true,
                [
                    new RekallAgeRuntimeViewportPostProcessPass(
                        "Bloom",
                        "bloom",
                        Threshold: 0.7,
                        Intensity: 0.85,
                        Radius: 1.5),
                    new RekallAgeRuntimeViewportPostProcessPass(
                        "Tone Map",
                        "tone-map")
                ])
        };
        var output = TestPaths.CreateTempDirectory();
        var capture = new RekallAgeNativeVulkanSceneCapture();

        var legacy = await capture.CaptureSceneAsync(
            legacyFrame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);
        var high = await capture.CaptureSceneAsync(
            highFrame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(legacy.Captured, string.Join(Environment.NewLine, legacy.Errors));
        Assert.True(high.Captured, string.Join(Environment.NewLine, high.Errors));
        Assert.NotEqual(legacy.ByteChecksum, high.ByteChecksum);
        Assert.Null(legacy.HighFidelityFrame);
        var report = Assert.IsType<RekallAgeHighFidelityFrameReport>(high.HighFidelityFrame);
        Assert.True(report.Executed, string.Join(Environment.NewLine, report.Diagnostics));
        Assert.Equal("R16G16B16A16_SFloat", report.SceneColorFormat);
        Assert.Equal("R8G8B8A8_UNorm", report.OutputColorFormat);
        Assert.Contains(report.Resources, resource => resource.Name == "scene-hdr"
            && resource.Format == "R16G16B16A16_SFloat"
            && resource.Allocated);
        Assert.Contains(report.Resources, resource => resource.Name == "bloom-pyramid"
            && resource.Allocated);
        Assert.Contains(report.Passes, pass => pass.Name == "bloom"
            && pass.Executed
            && pass.DispatchCount > 0);
        Assert.Contains(report.Passes, pass => pass.Name == "tone-map"
            && pass.Executed
            && pass.DrawCount == 1);
        Assert.Equal("R8G8B8A8_UNorm", high.Format);
        var legacyImage = await RekallAgePngReader.ReadRgbaAsync(legacy.OutputPath, CancellationToken.None);
        var highImage = await RekallAgePngReader.ReadRgbaAsync(high.OutputPath, CancellationToken.None);
        Assert.Contains(highImage.Rgba.Where((_, index) => index % 4 != 3), channel => channel > 32);
        Assert.Contains(
            Enumerable.Range(0, highImage.Width * highImage.Height),
            pixel => PixelBrightness(highImage.Rgba, pixel) > PixelBrightness(legacyImage.Rgba, pixel) + 3);
        Assert.DoesNotContain(highImage.Rgba.Where((_, index) => index % 4 != 3), channel => channel == byte.MaxValue);
    }

    private static int PixelBrightness(byte[] rgba, int pixel)
    {
        var offset = pixel * 4;
        return Math.Max(rgba[offset], Math.Max(rgba[offset + 1], rgba[offset + 2]));
    }

    private static RekallAgeRuntimeViewportPostProcessStack BloomPostStack(double radius) =>
        new(
            "post",
            "HDR Post",
            true,
            [
                new RekallAgeRuntimeViewportPostProcessPass(
                    "Bloom",
                    "bloom",
                    Threshold: 0.7,
                    Intensity: 0.85,
                    Radius: radius),
                new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")
            ]);

    private static RekallAgeRuntimeViewportFrame EmissiveFrame()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Camera3D",
            true,
            0,
            0,
            -4,
            FieldOfViewDegrees: 70,
            ClearColor: "#020306");
        return new RekallAgeRuntimeViewportFrame(
            "HDR Test",
            0,
            0,
            96,
            64,
            camera,
            [camera],
            [
                new RekallAgeRuntimeViewportRenderable(
                    "emissive-cube",
                    "Emissive Cube",
                    "mesh",
                    "rekall.primitive.cube",
                    0,
                    0,
                    0,
                    0,
                    Variant: "rekall.geometry.cube",
                    MaterialColor: "#241204",
                    MetallicFactor: 0.15,
                    RoughnessFactor: 0.35,
                    EmissiveColor: "#ff6a18",
                    EmissiveStrength: 12)
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
    }
}
