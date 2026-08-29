using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class EnvironmentBackgroundResolverTests
{
    [Fact]
    public void HdrBackgroundParsesEncodedRgbaAndDecodesRgbToLinearLight()
    {
        var resolved = RekallAgeEnvironmentBackgroundResolver.ResolveForHdr(
            Frame("camera", null, cameraColor: "#B4203AFF"));

        Assert.Equal(0xB4 / 255f, resolved.EncodedSrgb.X, 5);
        Assert.Equal(0x20 / 255f, resolved.EncodedSrgb.Y, 5);
        Assert.Equal(0x3A / 255f, resolved.EncodedSrgb.Z, 5);
        Assert.Equal(1f, resolved.EncodedSrgb.W);
        Assert.Equal(0.45641103f, resolved.LinearRgba.X, 5);
        Assert.Equal(0.01444384f, resolved.LinearRgba.Y, 5);
        Assert.Equal(0.04231141f, resolved.LinearRgba.Z, 5);
        Assert.Equal(1f, resolved.LinearRgba.W);
        Assert.True(resolved.IsSolidColor);
    }

    [Fact]
    public void HdrBackgroundKeepsAuthoredAlphaAndMarksAvailableSkyAsToneMapped()
    {
        var rgba = RekallAgeEnvironmentBackgroundResolver.ResolveForHdr(
            Frame("camera", null, cameraColor: "#80402080"));
        var sky = RekallAgeEnvironmentBackgroundResolver.ResolveForHdr(
            Frame("skybox", "#18303f", skyAssetId: "sky.hdr"));

        Assert.Equal(0x80 / 255f, rgba.EncodedSrgb.W, 5);
        Assert.Equal(0x80 / 255f, rgba.LinearRgba.W, 5);
        Assert.True(rgba.IsSolidColor);
        Assert.False(sky.IsSolidColor);
    }

    [Fact]
    public void AuthoredEnvironmentBackgroundOverridesCameraClearForColorAndSkyFallbackPolicies()
    {
        foreach (var policy in new[] { "color", "skybox" })
        {
            var frame = Frame(policy, "#18303f");

            var color = RekallAgeEnvironmentBackgroundResolver.Resolve(frame);

            Assert.Equal(0x18 / 255f, color.X, 5);
            Assert.Equal(0x30 / 255f, color.Y, 5);
            Assert.Equal(0x3f / 255f, color.Z, 5);
            Assert.Equal(1f, color.W);
        }
    }

    [Fact]
    public void CameraPolicyAndMalformedEnvironmentColorUseCameraClearColor()
    {
        foreach (var (policy, backgroundColor) in new[]
                 {
                     ("camera", "#ffffff"),
                     ("color", "not-a-color"),
                     ("color", "#ffffffzz")
                 })
        {
            var color = RekallAgeEnvironmentBackgroundResolver.Resolve(Frame(policy, backgroundColor));

            Assert.Equal(0x08 / 255f, color.X, 5);
            Assert.Equal(0x0b / 255f, color.Y, 5);
            Assert.Equal(0x0b / 255f, color.Z, 5);
            Assert.Equal(1f, color.W);
        }
    }

    private static RekallAgeRuntimeViewportFrame Frame(
        string policy,
        string? backgroundColor,
        string cameraColor = "#080b0b",
        string? skyAssetId = null)
    {
        return new RekallAgeRuntimeViewportFrame(
            "Main",
            1,
            1.0 / 60.0,
            640,
            360,
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
                0,
                "agx",
                11.2,
                null,
                policy)
            {
                BackgroundColor = backgroundColor
            }
        };
    }
}
