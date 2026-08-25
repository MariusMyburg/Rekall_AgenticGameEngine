using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanParticleCaptureTests
{
    [Fact]
    public async Task NativeGpuSimulationIndirectDrawAndDebugEvidenceExecuteAfterFogInHdr()
    {
        var particleFrame = Frame(withEmitter: true);
        var emptyFrame = Frame(withEmitter: false);
        var output = TestPaths.CreateTempDirectory();
        using var capture = new RekallAgeNativeVulkanSceneCapture();

        var particles = await capture.CaptureSceneAsync(
            particleFrame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);
        var empty = await capture.CaptureSceneAsync(
            emptyFrame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(particles.Captured, string.Join(Environment.NewLine, particles.Errors));
        Assert.True(empty.Captured, string.Join(Environment.NewLine, empty.Errors));
        Assert.NotEqual(empty.ByteChecksum, particles.ByteChecksum);
        var report = Assert.IsType<RekallAgeHighFidelityFrameReport>(particles.HighFidelityFrame);
        var particle = Assert.IsType<RekallAgeHighFidelityParticleReport>(report.Particles);
        Assert.True(particle.Enabled);
        Assert.Equal(64, particle.AllocatedCapacity);
        Assert.Equal(1, particle.SimulationDispatchCount);
        Assert.True(particle.IndirectDraw);
        Assert.True(particle.DepthTested);
        Assert.False(particle.DepthWrite);
        Assert.True(particle.SceneDepthSampled);
        Assert.True(particle.HdrOutput);
        Assert.Equal(1.0 / 60.0, particle.DeltaSeconds, precision: 8);
        Assert.Contains(report.Resources, resource => resource.Name == "particle-state-a" && resource.Allocated);
        Assert.Contains(report.Resources, resource => resource.Name == "particle-state-b" && resource.Allocated);
        Assert.Contains(report.Resources, resource => resource.Name == "particle-indirect" && resource.Allocated);
        var fogIndex = report.Passes.ToList().FindIndex(pass => pass.Name == "fog-integrate");
        var simulateIndex = report.Passes.ToList().FindIndex(pass => pass.Name == "particle-simulate");
        var drawIndex = report.Passes.ToList().FindIndex(pass => pass.Name == "transparent-particles");
        Assert.True(fogIndex < simulateIndex && simulateIndex < drawIndex);
        Assert.Contains(report.Passes, pass => pass.Name == "particle-simulate" && pass.Executed && pass.DispatchCount == 1);
        Assert.Contains(report.Passes, pass => pass.Name == "transparent-particles" && pass.Executed && pass.DrawCount == 1);
        Assert.Equal(["bounds", "overdraw"], report.ParticleDebugCaptures.Select(item => item.Kind));
        Assert.All(report.ParticleDebugCaptures, debug =>
        {
            Assert.True(File.Exists(debug.OutputPath), debug.OutputPath);
            Assert.True(debug.NonBlank, debug.Kind);
            Assert.Equal("native-particle-execution", debug.Source);
        });
    }

    [Fact]
    public async Task ConsecutiveCapturesReuseResidentStateAndAlternatePingPongBuffers()
    {
        var firstFrame = Frame(withEmitter: true);
        var secondFrame = firstFrame with { FrameIndex = 2, ElapsedSeconds = 2.0 / 60.0 };
        var output = TestPaths.CreateTempDirectory();
        using var capture = new RekallAgeNativeVulkanSceneCapture();

        var first = await capture.CaptureSceneAsync(firstFrame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        var second = await capture.CaptureSceneAsync(secondFrame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        Assert.True(first.Captured, string.Join(Environment.NewLine, first.Errors));
        Assert.True(second.Captured, string.Join(Environment.NewLine, second.Errors));
        var firstParticles = Assert.IsType<RekallAgeHighFidelityParticleReport>(Assert.IsType<RekallAgeHighFidelityFrameReport>(first.HighFidelityFrame).Particles);
        var secondParticles = Assert.IsType<RekallAgeHighFidelityParticleReport>(Assert.IsType<RekallAgeHighFidelityFrameReport>(second.HighFidelityFrame).Particles);
        Assert.False(firstParticles.PreviousStateReused);
        Assert.True(secondParticles.PreviousStateReused);
        Assert.Equal(firstParticles.StateResourceGeneration, secondParticles.StateResourceGeneration);
        Assert.Equal("particle-state-a", firstParticles.SimulationSource);
        Assert.Equal("particle-state-b", firstParticles.SimulationDestination);
        Assert.Equal("particle-state-b", secondParticles.SimulationSource);
        Assert.Equal("particle-state-a", secondParticles.SimulationDestination);
    }

    private static RekallAgeRuntimeViewportFrame Frame(bool withEmitter)
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera", "Camera", "Camera3D", true,
            0, 0, -4,
            FieldOfViewDegrees: 65,
            NearClip: 0.1,
            FarClip: 50,
            ClearColor: "#020306");
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High", Bloom: false, MaximumActiveParticles: 64),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("particle-native-test"),
            96,
            64);
        return new RekallAgeRuntimeViewportFrame(
            "Particle Native Test", 1, 1.0 / 60.0, 96, 64,
            camera, [camera],
            [new RekallAgeRuntimeViewportRenderable(
                "cube", "Cube", "mesh", "rekall.primitive.cube",
                0, -1.25, 3, 0,
                Variant: "rekall.geometry.cube",
                ScaleX: 3,
                ScaleY: 0.2,
                ScaleZ: 3,
                MaterialColor: "#183050")],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            [],
            PostProcessStack: new RekallAgeRuntimeViewportPostProcessStack(
                "post", "Tone Map", true,
                [new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")]))
        {
            DeltaSeconds = 1.0 / 60.0,
            ResolvedQualityPlan = quality,
            ParticleEmitters = withEmitter ? [HighFidelityRenderGraphTests.ParticleEmitter(64) with
            {
                SpawnRate = 3_840,
                SoftParticleFade = 0.5,
                EmissiveIntensity = 12
            }] : []
        };
    }
}
