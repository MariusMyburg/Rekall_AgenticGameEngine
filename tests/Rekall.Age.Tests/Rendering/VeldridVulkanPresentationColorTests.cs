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
        string? skyAssetId = null) =>
        new RekallAgeRuntimeViewportFrame(
            "Main",
            1,
            1.0 / 60.0,
            64,
            64,
            new RekallAgeRuntimeViewportCamera("camera", "Camera", "camera", true, ClearColor: cameraColor),
            [],
            [],
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
