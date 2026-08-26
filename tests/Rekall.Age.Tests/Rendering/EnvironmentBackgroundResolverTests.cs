using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class EnvironmentBackgroundResolverTests
{
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
                     ("color", "not-a-color")
                 })
        {
            var color = RekallAgeEnvironmentBackgroundResolver.Resolve(Frame(policy, backgroundColor));

            Assert.Equal(0x08 / 255f, color.X, 5);
            Assert.Equal(0x0b / 255f, color.Y, 5);
            Assert.Equal(0x0b / 255f, color.Z, 5);
            Assert.Equal(1f, color.W);
        }
    }

    private static RekallAgeRuntimeViewportFrame Frame(string policy, string? backgroundColor)
    {
        return new RekallAgeRuntimeViewportFrame(
            "Main",
            1,
            1.0 / 60.0,
            640,
            360,
            new RekallAgeRuntimeViewportCamera("camera", "Camera", "camera", true, ClearColor: "#080b0b"),
            [],
            [],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            [])
        {
            Environment = new RekallAgeRuntimeViewportEnvironment(
                "environment",
                "Environment",
                null,
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
