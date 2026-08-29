using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;

namespace Rekall.Age.Tests.Rendering;

[SupportedOSPlatform("windows")]
public sealed class VeldridVulkanPresentationColorTests
{
    [Fact]
    public async Task TransparentGeometryUsesCoverageInsteadOfDepthForBackgroundCorrection()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const int width = 64;
        const int height = 64;
        var assets = RekallAgeRuntimeViewportAssetSet.Empty;
        var hwnd = NativeWindow.Create(width, height);
        try
        {
            var solid = Frame("#12203A", exposure: 2, renderables: [TransparentCube()]);
            await using var session = new RekallAgeVeldridVulkanPresentationSession(
                new RekallAgeWin32RenderSurfaceDescriptor(hwnd, width, height),
                new RekallAgeVulkanPresentationOptions(
                    Path.GetFullPath("."),
                    SyncToVerticalBlank: false,
                    SceneSupersampleFactor: 1,
                    DebugHudEnabled: false),
                solid,
                assets);

            var solidPixel = await CaptureCenterAsync(session, solid, assets, sceneRevision: 1);

            Assert.True(
                Math.Abs(solidPixel[0] - 0x12) > 20,
                $"Expected transparent geometry to survive presentation, got RGB ({solidPixel[0]}, {solidPixel[1]}, {solidPixel[2]}).");
            Assert.Contains(
                "(SolidBackgroundEncoded.rgb - baseline) * (1.0 - sceneCoverage)",
                RekallAgeVeldridSceneShaders.PresentFragmentShader,
                StringComparison.Ordinal);
        }
        finally
        {
            NativeWindow.Destroy(hwnd);
        }
    }

    [Fact]
    public async Task FxaaEdgeFiltersTransparentCoverageWithTheSameWeightsAsColor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const int width = 64;
        const int height = 64;
        var assets = AssetsWithImage("one-pixel-sky", 0x20, 0xD0, 0x50);
        var redFallback = Frame(
            "#000000",
            environmentBackground: "#D02020",
            backgroundPolicy: "skybox",
            skyAssetId: "one-pixel-sky",
            renderables: [TransparentCube()]);
        var blueFallback = Frame(
            "#000000",
            environmentBackground: "#2020D0",
            backgroundPolicy: "skybox",
            skyAssetId: "one-pixel-sky",
            renderables: [TransparentCube()]);
        var hwnd = NativeWindow.Create(width, height);
        try
        {
            await using var session = new RekallAgeVeldridVulkanPresentationSession(
                new RekallAgeWin32RenderSurfaceDescriptor(hwnd, width, height),
                new RekallAgeVulkanPresentationOptions(
                    Path.GetFullPath("."),
                    SyncToVerticalBlank: false,
                    SceneSupersampleFactor: 1,
                    DebugHudEnabled: false),
                redFallback,
                assets);

            var redPixels = await CaptureAsync(session, redFallback, assets, sceneRevision: 1);
            var bluePixels = await CaptureAsync(session, blueFallback, assets, sceneRevision: 2);

            var maximumDifference = Enumerable.Range(0, redPixels.Length / 4)
                .SelectMany(pixel => Enumerable.Range(0, 3)
                    .Select(channel => Math.Abs(redPixels[pixel * 4 + channel] - bluePixels[pixel * 4 + channel])))
                .Max();
            // The two fallback colours are intentionally extreme, so luma-driven FXAA may
            // select a slightly different edge direction. The incoherent center-alpha path
            // produced a maximum channel error of 88; filtering RGBA together keeps the
            // partial-coverage edge below half that visible discontinuity.
            Assert.InRange(maximumDifference, 0, 50);
        }
        finally
        {
            NativeWindow.Destroy(hwnd);
        }
    }

    [Fact]
    public async Task OnePixelSkyTextureIsRenderedAsSkyInsteadOfTreatedAsSolidSentinel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const int width = 64;
        const int height = 64;
        var assets = AssetsWithImage("one-pixel-sky", 0x10, 0xE0, 0x20);
        var frame = Frame(
            "#A01010",
            environmentBackground: "#A01010",
            backgroundPolicy: "skybox",
            skyAssetId: "one-pixel-sky");
        var hwnd = NativeWindow.Create(width, height);
        try
        {
            await using var session = new RekallAgeVeldridVulkanPresentationSession(
                new RekallAgeWin32RenderSurfaceDescriptor(hwnd, width, height),
                new RekallAgeVulkanPresentationOptions(
                    Path.GetFullPath("."),
                    SyncToVerticalBlank: false,
                    SceneSupersampleFactor: 1,
                    DebugHudEnabled: false),
                frame,
                assets);

            var pixel = await CaptureCenterAsync(session, frame, assets, sceneRevision: 1);

            Assert.True(pixel[1] > pixel[0] + 40, $"Expected green 1x1 sky texel, got RGB ({pixel[0]}, {pixel[1]}, {pixel[2]}).");
        }
        finally
        {
            NativeWindow.Destroy(hwnd);
        }
    }

    [Fact]
    public void SolidBackgroundReconstructsAuthoredAlphaAfterUsingAlphaAsCoverage()
    {
        var frame = Frame("#12203A80");
        var background = RekallAgeEnvironmentBackgroundResolver.ResolveForHdr(frame);

        Assert.InRange(background.EncodedSrgb.W, 0.501f, 0.503f);
        Assert.Contains(
            "sceneCoverage + backgroundAlpha * (1.0 - sceneCoverage)",
            RekallAgeVeldridSceneShaders.PresentFragmentShader,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolidBackgroundReadbackMatchesAuthoredSrgbAcrossDisplayTransformsAndMissingSkyFallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const int width = 64;
        const int height = 64;
        var hwnd = NativeWindow.Create(width, height);
        try
        {
            var initialFrame = Frame("#12203A");
            await using var session = new RekallAgeVeldridVulkanPresentationSession(
                new RekallAgeWin32RenderSurfaceDescriptor(hwnd, width, height),
                new RekallAgeVulkanPresentationOptions(
                    Path.GetFullPath("."),
                    SyncToVerticalBlank: false,
                    SceneSupersampleFactor: 1,
                    DebugHudEnabled: false),
                initialFrame,
                RekallAgeRuntimeViewportAssetSet.Empty);

            await AssertBackgroundAsync(session, initialFrame, 0x12, 0x20, 0x3A, sceneRevision: 1);
            await AssertBackgroundAsync(
                session,
                Frame("#B4203AFF", exposure: 2, toneMapper: "agx"),
                0xB4,
                0x20,
                0x3A,
                sceneRevision: 2);
            await AssertBackgroundAsync(
                session,
                Frame(
                    "#12203A",
                    environmentBackground: "#18303F",
                    backgroundPolicy: "skybox",
                    skyAssetId: "missing-sky.hdr"),
                0x18,
                0x30,
                0x3F,
                sceneRevision: 3);
        }
        finally
        {
            NativeWindow.Destroy(hwnd);
        }
    }

    private static async Task AssertBackgroundAsync(
        RekallAgeVeldridVulkanPresentationSession session,
        RekallAgeRuntimeViewportFrame frame,
        byte red,
        byte green,
        byte blue,
        int sceneRevision)
    {
        await session.PresentAsync(
            new RekallAgeVulkanSceneSubmission(
                frame,
                RekallAgeRuntimeViewportAssetSet.Empty,
                [],
                0,
                sceneRevision,
                0),
            CancellationToken.None);
        var capture = await session.CapturePresentedRgbaAsync(CancellationToken.None);
        var pixelOffset = ((capture.Height / 2 * capture.Width) + capture.Width / 2) * 4;
        var pixels = capture.Rgba.Span;

        Assert.InRange(pixels[pixelOffset], red - 1, red + 1);
        Assert.InRange(pixels[pixelOffset + 1], green - 1, green + 1);
        Assert.InRange(pixels[pixelOffset + 2], blue - 1, blue + 1);
    }

    private static RekallAgeRuntimeViewportFrame Frame(
        string cameraColor,
        double exposure = 0,
        string toneMapper = "exponential",
        string? environmentBackground = null,
        string backgroundPolicy = "camera",
        string? skyAssetId = null,
        IReadOnlyList<RekallAgeRuntimeViewportRenderable>? renderables = null) =>
        new RekallAgeRuntimeViewportFrame(
            "Main",
            1,
            1.0 / 60.0,
            64,
            64,
            new RekallAgeRuntimeViewportCamera("camera", "Camera", "Camera3D", true, Z: -4, ClearColor: cameraColor),
            [],
            renderables ?? [],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            [])
        {
            Environment = new RekallAgeRuntimeViewportEnvironment(
                "environment",
                "Environment",
                skyAssetId,
                1,
                exposure,
                toneMapper,
                11.2,
                null,
                backgroundPolicy)
            {
                BackgroundColor = environmentBackground
            }
        };

    private static RekallAgeRuntimeViewportRenderable TransparentCube() =>
        new(
            "transparent-cube",
            "Transparent Cube",
            "mesh",
            "rekall.primitive.cube",
            0,
            0,
            0,
            0,
            Variant: "rekall.geometry.cube",
            ScaleX: 2,
            ScaleY: 2,
            ScaleZ: 2,
            MaterialColor: "#FF603080")
        {
            AlphaMode = "blend"
        };

    private static RekallAgeRuntimeViewportAssetSet AssetsWithImage(string id, byte red, byte green, byte blue) =>
        new(
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal)
            {
                [id] = new RekallAgeRgbaImage(1, 1, [red, green, blue, 255])
            },
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal),
            []);

    private static async Task<byte[]> CaptureCenterAsync(
        RekallAgeVeldridVulkanPresentationSession session,
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        int sceneRevision)
    {
        var pixels = await CaptureAsync(session, frame, assets, sceneRevision);
        var offset = ((frame.Height / 2 * frame.Width) + frame.Width / 2) * 4;
        return pixels.AsSpan(offset, 4).ToArray();
    }

    private static async Task<byte[]> CaptureAsync(
        RekallAgeVeldridVulkanPresentationSession session,
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        int sceneRevision)
    {
        await session.PresentAsync(
            new RekallAgeVulkanSceneSubmission(frame, assets, [], 0, sceneRevision, 0),
            CancellationToken.None);
        var capture = await session.CapturePresentedRgbaAsync(CancellationToken.None);
        return capture.Rgba.ToArray();
    }

    private static class NativeWindow
    {
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsVisible = 0x10000000;

        internal static IntPtr Create(int width, int height)
        {
            var hwnd = CreateWindowExW(
                0,
                "STATIC",
                string.Empty,
                WsPopup | WsVisible,
                -32000,
                -32000,
                width,
                height,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
            return hwnd != IntPtr.Zero
                ? hwnd
                : throw new InvalidOperationException("Could not create the offscreen Vulkan test window.");
        }

        internal static void Destroy(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                _ = DestroyWindow(hwnd);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            int extendedStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);
    }
}
