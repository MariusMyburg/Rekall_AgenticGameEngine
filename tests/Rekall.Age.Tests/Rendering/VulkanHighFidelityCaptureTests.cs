using System.Security.Cryptography;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanHighFidelityCaptureTests
{
    [Fact]
    public async Task DirectionalShadowsAllocateDrawSampleAndDarkenNativePixels()
    {
        var shadowedFrame = ShadowFrame(castShadows: true);
        var unshadowedFrame = ShadowFrame(castShadows: false);
        var output = TestPaths.CreateTempDirectory();
        var capture = new RekallAgeNativeVulkanSceneCapture();

        var shadowed = await capture.CaptureSceneAsync(
            shadowedFrame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);
        var unshadowed = await capture.CaptureSceneAsync(
            unshadowedFrame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(shadowed.Captured, string.Join(Environment.NewLine, shadowed.Errors));
        Assert.True(unshadowed.Captured, string.Join(Environment.NewLine, unshadowed.Errors));
        var report = Assert.IsType<RekallAgeHighFidelityFrameReport>(shadowed.HighFidelityFrame);
        Assert.Contains(report.Resources, resource => resource is
        { Name: "shadow-directional", Format: "D32_SFloat", Width: 2048, Height: 2048, Allocated: true });
        Assert.Contains(report.Passes, pass => pass.Name == "shadow-directional"
            && pass.Executed
            && pass.DrawCount >= 3);
        Assert.Contains(report.Passes, pass => pass.Name == "opaque-hdr"
            && pass.Inputs.Contains("shadow-directional", StringComparer.Ordinal));
        Assert.Equal(3, report.ShadowCascades.Count);
        Assert.All(report.ShadowCascades, cascade =>
        {
            Assert.Equal(2048, cascade.Resolution);
            Assert.Equal(12, cascade.FilterTapCount);
            Assert.True(cascade.AtlasBytes > 0);
            Assert.True(cascade.DrawCount > 0);
            Assert.True(cascade.SplitFar > cascade.SplitNear);
        });
        Assert.Equal(report.ShadowCascades.Count, report.ShadowDebugCaptures.Count);
        var debugImages = new List<RekallAgeRgbaImage>();
        foreach (var debugCapture in report.ShadowDebugCaptures.OrderBy(item => item.CascadeIndex))
        {
            var cascade = report.ShadowCascades[debugCapture.CascadeIndex];
            Assert.Equal(cascade.SplitNear, debugCapture.SplitNear);
            Assert.Equal(cascade.SplitFar, debugCapture.SplitFar);
            Assert.True(File.Exists(debugCapture.OutputPath), debugCapture.OutputPath);
            Assert.True(debugCapture.NonBlank);
            var debugImage = await RekallAgePngReader.ReadRgbaAsync(debugCapture.OutputPath, CancellationToken.None);
            Assert.Equal(cascade.Resolution, debugImage.Width);
            Assert.Equal(cascade.Resolution, debugImage.Height);
            Assert.Contains(Enumerable.Range(0, debugImage.Width * debugImage.Height), pixel =>
                PixelBrightness(debugImage.Rgba, pixel) > 0);
            debugImages.Add(debugImage);
        }
        Assert.True(
            debugImages.Select(image => Convert.ToHexString(SHA256.HashData(image.Rgba))).Distinct(StringComparer.Ordinal).Count() > 1,
            "Expected cascade depth visualizations to be distinguishable across planned layers.");

        var shadowedImage = await RekallAgePngReader.ReadRgbaAsync(shadowed.OutputPath, CancellationToken.None);
        var unshadowedImage = await RekallAgePngReader.ReadRgbaAsync(unshadowed.OutputPath, CancellationToken.None);
        Assert.NotEqual(shadowed.ByteChecksum, unshadowed.ByteChecksum);
        Assert.True(
            Enumerable.Range(0, shadowedImage.Width * shadowedImage.Height).Count(pixel =>
                PixelBrightness(unshadowedImage.Rgba, pixel) >= PixelBrightness(shadowedImage.Rgba, pixel) + 3) > 48,
            "Expected the executable shadow sampling path to darken a visible population of receiver pixels.");
    }

    [Fact]
    public async Task BlendedRenderableDoesNotCastOrInflateRecordedShadowDrawReports()
    {
        var baselineFrame = ShadowFrame(castShadows: true);
        var blendedFrame = baselineFrame with
        {
            Renderables = baselineFrame.Renderables.Append(new RekallAgeRuntimeViewportRenderable(
                "blend",
                "Blended Surface",
                "mesh",
                "rekall.primitive.cube",
                -2.5,
                0,
                5,
                4,
                Variant: "rekall.geometry.cube",
                MaterialColor: "#80d0ffff")
            {
                AlphaMode = "blend",
                CastShadows = true
            }).ToArray()
        };
        var output = TestPaths.CreateTempDirectory();
        var capture = new RekallAgeNativeVulkanSceneCapture();

        var baseline = await capture.CaptureSceneAsync(
            baselineFrame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);
        var blended = await capture.CaptureSceneAsync(
            blendedFrame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(baseline.Captured, string.Join(Environment.NewLine, baseline.Errors));
        Assert.True(blended.Captured, string.Join(Environment.NewLine, blended.Errors));
        var baselineReport = Assert.IsType<RekallAgeHighFidelityFrameReport>(baseline.HighFidelityFrame);
        var blendedReport = Assert.IsType<RekallAgeHighFidelityFrameReport>(blended.HighFidelityFrame);
        Assert.Equal(
            Assert.Single(baselineReport.Passes, pass => pass.Name == "shadow-directional").DrawCount,
            Assert.Single(blendedReport.Passes, pass => pass.Name == "shadow-directional").DrawCount);
        Assert.Equal(
            baselineReport.ShadowCascades.Select(cascade => cascade.DrawCount),
            blendedReport.ShadowCascades.Select(cascade => cascade.DrawCount));
    }

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

    private static RekallAgeRuntimeViewportFrame ShadowFrame(bool castShadows)
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Camera3D",
            true,
            0,
            3.5,
            -7,
            RotationX: 18,
            FieldOfViewDegrees: 55,
            NearClip: 0.1,
            FarClip: 80,
            ClearColor: "#020306");
        var light = new RekallAgeRuntimeViewportRenderable(
            "sun",
            "Sun",
            "light",
            null,
            0,
            8,
            0,
            -100,
            Variant: "directional",
            RotationX: 50,
            RotationY: -25,
            Intensity: 2.4,
            MaterialColor: "#fff1d2")
        {
            CastShadows = castShadows,
            ShadowMaximumDistance = 80,
            ShadowBias = 0.0015,
            ShadowNormalBias = 0.02,
            ShadowPriority = 10
        };
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High", Bloom: false),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("native-test"),
            256,
            192);
        return new RekallAgeRuntimeViewportFrame(
            "Directional Shadow Test",
            0,
            0,
            256,
            192,
            camera,
            [camera],
            [
                light,
                new RekallAgeRuntimeViewportRenderable(
                    "ground",
                    "Ground",
                    "mesh",
                    "rekall.primitive.cube",
                    0,
                    -1,
                    6,
                    0,
                    Variant: "rekall.geometry.cube",
                    ScaleX: 7,
                    ScaleY: 0.2,
                    ScaleZ: 7,
                    MaterialColor: "#b8aa8a",
                    RoughnessFactor: 0.9),
                new RekallAgeRuntimeViewportRenderable(
                    "near-caster",
                    "Near Caster",
                    "mesh",
                    "rekall.primitive.cube",
                    2.2,
                    0,
                    0,
                    1,
                    Variant: "rekall.geometry.cube",
                    ScaleX: 0.7,
                    ScaleY: 1.1,
                    ScaleZ: 0.7,
                    MaterialColor: "#79a85b",
                    RoughnessFactor: 0.7),
                new RekallAgeRuntimeViewportRenderable(
                    "caster",
                    "Caster",
                    "mesh",
                    "rekall.primitive.cube",
                    0,
                    0.2,
                    5,
                    2,
                    Variant: "rekall.geometry.cube",
                    ScaleX: 0.85,
                    ScaleY: 1.4,
                    ScaleZ: 0.85,
                    MaterialColor: "#d96b38",
                    RoughnessFactor: 0.55),
                new RekallAgeRuntimeViewportRenderable(
                    "far-caster",
                    "Far Caster",
                    "mesh",
                    "rekall.primitive.cube",
                    -4,
                    1,
                    38,
                    3,
                    Variant: "rekall.geometry.cube",
                    ScaleX: 3,
                    ScaleY: 4,
                    ScaleZ: 3,
                    MaterialColor: "#6b83b8",
                    RoughnessFactor: 0.65)
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            [],
            PostProcessStack: new RekallAgeRuntimeViewportPostProcessStack(
                "post",
                "Tone Map",
                true,
                [new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")]))
        {
            ResolvedQualityPlan = quality
        };
    }
}
