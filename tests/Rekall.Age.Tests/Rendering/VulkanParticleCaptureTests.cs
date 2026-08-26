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
        Assert.Equal(64, particle.PlannedSpawnCount);
        Assert.Equal(64, particle.GpuActiveCount);
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
            Assert.Equal(debug.Kind == "bounds" ? "gpu-particle-state-readback" : "gpu-particle-fragment-counter-readback", debug.Source);
            if (debug.Kind == "bounds") Assert.StartsWith("particle-state-", debug.EvidenceResource, StringComparison.Ordinal);
            else Assert.Equal("particle-fragment-counts", debug.EvidenceResource);
            Assert.True(debug.GpuSampleCount > 0);
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

    [Fact]
    public async Task SurvivingParticlesReportGpuActiveCountWhenCurrentFramePlansNoSpawn()
    {
        var emitter = HighFidelityRenderGraphTests.ParticleEmitter(64) with
        {
            SpawnRate = 0,
            Bursts = [new(1.0 / 60.0, 8)],
            LifetimeSeconds = 2
        };
        var firstFrame = Frame(withEmitter: false) with { ParticleEmitters = [emitter] };
        var secondFrame = firstFrame with { FrameIndex = 2, ElapsedSeconds = 2.0 / 60.0 };
        var output = TestPaths.CreateTempDirectory();
        using var capture = new RekallAgeNativeVulkanSceneCapture();

        var first = await capture.CaptureSceneAsync(firstFrame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        var second = await capture.CaptureSceneAsync(secondFrame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        var firstReport = Assert.IsType<RekallAgeHighFidelityParticleReport>(Assert.IsType<RekallAgeHighFidelityFrameReport>(first.HighFidelityFrame).Particles);
        var secondReport = Assert.IsType<RekallAgeHighFidelityParticleReport>(Assert.IsType<RekallAgeHighFidelityFrameReport>(second.HighFidelityFrame).Particles);
        Assert.Equal(8, firstReport.PlannedSpawnCount);
        Assert.Equal(8, firstReport.GpuActiveCount);
        Assert.Equal(0, secondReport.PlannedSpawnCount);
        Assert.Equal(8, secondReport.GpuActiveCount);
    }

    [Fact]
    public async Task EqualCapacityEmitterReplacementResetsResidentTopology()
    {
        var firstFrame = Frame(withEmitter: true);
        var replacement = firstFrame.ParticleEmitters.Single() with { EntityId = "replacement", EntityName = "Replacement" };
        var secondFrame = firstFrame with { FrameIndex = 2, ElapsedSeconds = 2.0 / 60.0, ParticleEmitters = [replacement] };
        var output = TestPaths.CreateTempDirectory();
        using var capture = new RekallAgeNativeVulkanSceneCapture();

        var first = await capture.CaptureSceneAsync(firstFrame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        var second = await capture.CaptureSceneAsync(secondFrame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        var firstReport = Assert.IsType<RekallAgeHighFidelityParticleReport>(Assert.IsType<RekallAgeHighFidelityFrameReport>(first.HighFidelityFrame).Particles);
        var secondReport = Assert.IsType<RekallAgeHighFidelityParticleReport>(Assert.IsType<RekallAgeHighFidelityFrameReport>(second.HighFidelityFrame).Particles);
        Assert.False(secondReport.PreviousStateReused);
        Assert.True(secondReport.StateResourceGeneration > firstReport.StateResourceGeneration);
    }

    [Fact]
    public async Task ParticleOnlySceneExecutesNativeComputeIndirectDrawAndCapture()
    {
        var frame = Frame(withEmitter: true) with { Renderables = [] };
        var output = TestPaths.CreateTempDirectory();
        using var capture = new RekallAgeNativeVulkanSceneCapture();

        var result = await capture.CaptureSceneAsync(frame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        Assert.True(result.Captured, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(0, result.MeshCount);
        var report = Assert.IsType<RekallAgeHighFidelityFrameReport>(result.HighFidelityFrame);
        Assert.True(Assert.IsType<RekallAgeHighFidelityParticleReport>(report.Particles).GpuActiveCount > 0);
        Assert.Contains(report.Passes, pass => pass.Name == "particle-simulate" && pass.Executed);
        Assert.Contains(report.Passes, pass => pass.Name == "transparent-particles" && pass.Executed);
    }

    [Fact]
    public async Task ParticleOverdrawEvidenceExcludesOrdinaryGeometry()
    {
        var output = TestPaths.CreateTempDirectory();
        using var withGeometryCapture = new RekallAgeNativeVulkanSceneCapture();
        using var particleOnlyCapture = new RekallAgeNativeVulkanSceneCapture();

        var geometryFrame = Frame(withEmitter: true);
        var isolatedGeometry = geometryFrame.Renderables.Single() with
        {
            X = 2.5,
            Y = 1.5,
            Z = 4,
            ScaleX = 0.2,
            ScaleY = 0.2,
            ScaleZ = 0.2,
            MaterialColor = "#ffffff"
        };
        var withGeometry = await withGeometryCapture.CaptureSceneAsync(geometryFrame with { Renderables = [isolatedGeometry] }, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        var particleOnly = await particleOnlyCapture.CaptureSceneAsync(Frame(withEmitter: true) with { Renderables = [] }, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        var geometryOverdraw = Assert.Single(Assert.IsType<RekallAgeHighFidelityFrameReport>(withGeometry.HighFidelityFrame).ParticleDebugCaptures, item => item.Kind == "overdraw");
        var particleOnlyOverdraw = Assert.Single(Assert.IsType<RekallAgeHighFidelityFrameReport>(particleOnly.HighFidelityFrame).ParticleDebugCaptures, item => item.Kind == "overdraw");
        Assert.Equal(geometryOverdraw.ByteChecksum, particleOnlyOverdraw.ByteChecksum);
        Assert.Equal(geometryOverdraw.GpuSampleCount, particleOnlyOverdraw.GpuSampleCount);
    }

    [Fact]
    public async Task AuthoredMiddleCurveKeyChangesNativeGpuParticleOutput()
    {
        var baseEmitter = HighFidelityRenderGraphTests.ParticleEmitter(8) with
        {
            SpawnRate = 0,
            Bursts = [new(1.0 / 60.0, 8)],
            LifetimeSeconds = 1,
            SizeCurve = [new(0, 0.25), new(0.5, 0.5), new(1, 0.25)],
            ColorCurve = [new(0, "#ff0000ff"), new(0.5, "#00ff00ff"), new(1, "#ff0000ff")],
            SoftParticleFade = 0
        };
        var first = Frame(withEmitter: false) with { Renderables = [], ParticleEmitters = [baseEmitter] };
        var middle = first with { FrameIndex = 2, ElapsedSeconds = 0.5 + 1.0 / 60.0, DeltaSeconds = 0.5 };
        var flatFirst = first with
        {
            ParticleEmitters = [baseEmitter with
            {
                SizeCurve = [new(0, 0.25), new(0.5, 0.25), new(1, 0.25)],
                ColorCurve = [new(0, "#ff0000ff"), new(0.5, "#ff0000ff"), new(1, "#ff0000ff")]
            }]
        };
        var flatMiddle = flatFirst with { FrameIndex = 2, ElapsedSeconds = 0.5 + 1.0 / 60.0, DeltaSeconds = 0.5 };
        var output = TestPaths.CreateTempDirectory();
        using var curvedCapture = new RekallAgeNativeVulkanSceneCapture();
        using var flatCapture = new RekallAgeNativeVulkanSceneCapture();

        await curvedCapture.CaptureSceneAsync(first, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        var curved = await curvedCapture.CaptureSceneAsync(middle, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        await flatCapture.CaptureSceneAsync(flatFirst, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        var flat = await flatCapture.CaptureSceneAsync(flatMiddle, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        Assert.True(curved.Captured, string.Join(Environment.NewLine, curved.Errors));
        Assert.True(flat.Captured, string.Join(Environment.NewLine, flat.Errors));
        Assert.NotEqual(flat.ByteChecksum, curved.ByteChecksum);
        var curvedBounds = Assert.Single(Assert.IsType<RekallAgeHighFidelityFrameReport>(curved.HighFidelityFrame).ParticleDebugCaptures, item => item.Kind == "bounds");
        var flatBounds = Assert.Single(Assert.IsType<RekallAgeHighFidelityFrameReport>(flat.HighFidelityFrame).ParticleDebugCaptures, item => item.Kind == "bounds");
        Assert.NotEqual(flatBounds.ByteChecksum, curvedBounds.ByteChecksum);
    }

    [Fact]
    public async Task InvalidPackInputsSurfaceStableNativeReportDiagnosticsBeforeGpuAllocation()
    {
        var valid = HighFidelityRenderGraphTests.ParticleEmitter(8);
        var frame = Frame(withEmitter: false) with
        {
            ParticleEmitters =
            [
                valid with { EntityId = "space", SimulationSpace = "screen" },
                valid with { EntityId = "cone", VelocityConeDegrees = 90 },
                valid with { EntityId = "speed", MinimumSpeed = -1 },
                valid with { EntityId = "drag", Drag = -1 },
                valid with { EntityId = "emission", SpawnRate = -1 },
                valid with { EntityId = "size", SizeCurve = [new(0, -1)] },
                valid with { EntityId = "fade", SoftParticleFade = -1 },
                valid with { EntityId = "color", ColorCurve = [new(0, "bad")] }
            ]
        };
        var output = TestPaths.CreateTempDirectory();
        using var capture = new RekallAgeNativeVulkanSceneCapture();

        var result = await capture.CaptureSceneAsync(frame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        Assert.True(result.Captured, string.Join(Environment.NewLine, result.Errors));
        var particles = Assert.IsType<RekallAgeHighFidelityParticleReport>(Assert.IsType<RekallAgeHighFidelityFrameReport>(result.HighFidelityFrame).Particles);
        Assert.Equal(0, particles.AllocatedCapacity);
        Assert.Equal(8, particles.RejectedEntityIds.Count);
        Assert.Contains(particles.Diagnostics, item => item.StartsWith("REKALL_PARTICLE_SIMULATION_SPACE_UNSUPPORTED:", StringComparison.Ordinal));
        Assert.Contains(particles.Diagnostics, item => item.StartsWith("REKALL_PARTICLE_MOTION_INVALID:", StringComparison.Ordinal));
        Assert.Contains(particles.Diagnostics, item => item.StartsWith("REKALL_PARTICLE_EMISSION_INVALID:", StringComparison.Ordinal));
        Assert.Contains(particles.Diagnostics, item => item.StartsWith("REKALL_PARTICLE_SIZE_CURVE_INVALID:", StringComparison.Ordinal));
        Assert.Contains(particles.Diagnostics, item => item.StartsWith("REKALL_PARTICLE_APPEARANCE_INVALID:", StringComparison.Ordinal));
        Assert.Contains(particles.Diagnostics, item => item.StartsWith("REKALL_PARTICLE_COLOR_INVALID:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScaledRenderExtentReadsAuthenticCountersThenResolvesOverdrawToOutputExtent()
    {
        var scaledFrame = Frame(withEmitter: true, resolutionScale: 0.5, width: 96, height: 64) with { Renderables = [] };
        var referenceFrame = Frame(withEmitter: true, resolutionScale: 1, width: 48, height: 32) with { Renderables = [] };
        var output = TestPaths.CreateTempDirectory();
        using var scaledCapture = new RekallAgeNativeVulkanSceneCapture();
        using var referenceCapture = new RekallAgeNativeVulkanSceneCapture();

        var scaled = await scaledCapture.CaptureSceneAsync(scaledFrame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        var reference = await referenceCapture.CaptureSceneAsync(referenceFrame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        Assert.True(scaled.Captured, string.Join(Environment.NewLine, scaled.Errors));
        Assert.True(reference.Captured, string.Join(Environment.NewLine, reference.Errors));
        var scaledOverdraw = Assert.Single(Assert.IsType<RekallAgeHighFidelityFrameReport>(scaled.HighFidelityFrame).ParticleDebugCaptures, item => item.Kind == "overdraw");
        var referenceOverdraw = Assert.Single(Assert.IsType<RekallAgeHighFidelityFrameReport>(reference.HighFidelityFrame).ParticleDebugCaptures, item => item.Kind == "overdraw");
        Assert.Equal(48, scaledOverdraw.EvidenceWidth);
        Assert.Equal(32, scaledOverdraw.EvidenceHeight);
        Assert.Equal(96, scaledOverdraw.OutputWidth);
        Assert.Equal(64, scaledOverdraw.OutputHeight);
        Assert.Equal(referenceOverdraw.GpuSampleCount, scaledOverdraw.GpuSampleCount);
        Assert.Equal(referenceOverdraw.GpuEvidenceChecksum, scaledOverdraw.GpuEvidenceChecksum);
        Assert.True(scaledOverdraw.NonBlank);
        Assert.Equal("gpu-particle-fragment-counter-readback", scaledOverdraw.Source);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("zero-emission")]
    public async Task IntentionallyInactiveParticleOnlyScenesUseQuietTruthfulClearCapture(string scenario)
    {
        var emitter = HighFidelityRenderGraphTests.ParticleEmitter(8);
        emitter = scenario switch
        {
            "disabled" => emitter with { Enabled = false },
            "zero-emission" => emitter with { SpawnRate = 0, Bursts = [] },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var frame = Frame(withEmitter: false) with { Renderables = [], ParticleEmitters = [emitter] };
        var output = TestPaths.CreateTempDirectory();
        using var capture = new RekallAgeNativeVulkanSceneCapture();

        var result = await capture.CaptureSceneAsync(frame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        Assert.True(result.Captured, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(0, result.MeshCount);
        Assert.Null(result.HighFidelityFrame);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("simulation-space", "REKALL_PARTICLE_SIMULATION_SPACE_UNSUPPORTED")]
    [InlineData("size", "REKALL_PARTICLE_SIZE_CURVE_INVALID")]
    public async Task RejectedParticleOnlyScenesClearWithoutExecutionAndPreserveDiagnostic(string scenario, string expectedCode)
    {
        var emitter = HighFidelityRenderGraphTests.ParticleEmitter(8);
        emitter = scenario switch
        {
            "simulation-space" => emitter with { SimulationSpace = "screen" },
            "size" => emitter with { SizeCurve = [new(0, -1)] },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var frame = Frame(withEmitter: false) with { Renderables = [], ParticleEmitters = [emitter] };
        var output = TestPaths.CreateTempDirectory();
        using var capture = new RekallAgeNativeVulkanSceneCapture();

        var result = await capture.CaptureSceneAsync(frame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        Assert.True(result.Captured, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(0, result.MeshCount);
        Assert.Empty(result.Errors);
        var report = Assert.IsType<RekallAgeHighFidelityFrameReport>(result.HighFidelityFrame);
        Assert.False(report.Executed);
        Assert.Empty(report.Resources);
        Assert.Empty(report.Passes);
        Assert.Contains(report.Diagnostics, item => item.StartsWith(expectedCode, StringComparison.Ordinal));
        var particles = Assert.IsType<RekallAgeHighFidelityParticleReport>(report.Particles);
        Assert.False(particles.Enabled);
        Assert.Equal(0, particles.AllocatedCapacity);
        Assert.Equal(0, particles.SimulationDispatchCount);
        Assert.Equal(0, particles.DrawCount);
        Assert.Equal([emitter.EntityId], particles.RejectedEntityIds);
        Assert.Contains(particles.Diagnostics, item => item.StartsWith(expectedCode, StringComparison.Ordinal));
    }

    [Fact]
    public async Task HugeFiniteParticleTimestepRejectsBeforePackingAndSurfacesNonExecutedNativeReport()
    {
        var emitter = HighFidelityRenderGraphTests.ParticleEmitter(8);
        var frame = Frame(withEmitter: false) with
        {
            Renderables = [],
            ParticleEmitters = [emitter],
            DeltaSeconds = double.MaxValue
        };
        var output = TestPaths.CreateTempDirectory();
        using var capture = new RekallAgeNativeVulkanSceneCapture();

        var result = await capture.CaptureSceneAsync(frame, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        Assert.True(result.Captured, string.Join(Environment.NewLine, result.Errors));
        Assert.Empty(result.Errors);
        var report = Assert.IsType<RekallAgeHighFidelityFrameReport>(result.HighFidelityFrame);
        Assert.False(report.Executed);
        var particles = Assert.IsType<RekallAgeHighFidelityParticleReport>(report.Particles);
        Assert.False(particles.Enabled);
        Assert.Equal(0, particles.AllocatedCapacity);
        Assert.Equal(0, particles.PlannedSpawnCount);
        Assert.Equal(0, particles.SimulationDispatchCount);
        Assert.Equal(0, particles.DrawCount);
        Assert.Equal(0, particles.DeltaSeconds);
        Assert.Equal([emitter.EntityId], particles.RejectedEntityIds);
        Assert.Contains(particles.Diagnostics, item => item.StartsWith("REKALL_PARTICLE_TIMESTEP_INVALID:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FloatMaximumDirectionPacksToTheSameGpuMotionAsItsUnitVector()
    {
        var emitter = HighFidelityRenderGraphTests.ParticleEmitter(8) with
        {
            SpawnRate = 0,
            Bursts = [new(1.0 / 60.0, 8)],
            LifetimeSeconds = 1,
            VelocityDirectionX = 1,
            VelocityDirectionY = 0,
            VelocityDirectionZ = 0,
            VelocityConeDegrees = 0,
            MinimumSpeed = 1,
            MaximumSpeed = 1,
            GravityX = 0,
            GravityY = 0,
            GravityZ = 0,
            Drag = 0
        };
        var unitFirst = Frame(withEmitter: false) with { Renderables = [], ParticleEmitters = [emitter] };
        var unitSecond = unitFirst with { FrameIndex = 2, ElapsedSeconds = 0.25 + 1.0 / 60.0, DeltaSeconds = 0.25 };
        var maximumFirst = unitFirst with { ParticleEmitters = [emitter with { VelocityDirectionX = float.MaxValue }] };
        var maximumSecond = maximumFirst with { FrameIndex = 2, ElapsedSeconds = 0.25 + 1.0 / 60.0, DeltaSeconds = 0.25 };
        var output = TestPaths.CreateTempDirectory();
        using var unitCapture = new RekallAgeNativeVulkanSceneCapture();
        using var maximumCapture = new RekallAgeNativeVulkanSceneCapture();

        await unitCapture.CaptureSceneAsync(unitFirst, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        var unit = await unitCapture.CaptureSceneAsync(unitSecond, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        await maximumCapture.CaptureSceneAsync(maximumFirst, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);
        var maximum = await maximumCapture.CaptureSceneAsync(maximumSecond, RekallAgeRuntimeViewportAssetSet.Empty, output, "discrete-gpu", CancellationToken.None);

        Assert.True(unit.Captured, string.Join(Environment.NewLine, unit.Errors));
        Assert.True(maximum.Captured, string.Join(Environment.NewLine, maximum.Errors));
        Assert.Equal(unit.ByteChecksum, maximum.ByteChecksum);
        var unitBounds = Assert.Single(Assert.IsType<RekallAgeHighFidelityFrameReport>(unit.HighFidelityFrame).ParticleDebugCaptures, item => item.Kind == "bounds");
        var maximumBounds = Assert.Single(Assert.IsType<RekallAgeHighFidelityFrameReport>(maximum.HighFidelityFrame).ParticleDebugCaptures, item => item.Kind == "bounds");
        Assert.Equal(unitBounds.ByteChecksum, maximumBounds.ByteChecksum);
    }

    private static RekallAgeRuntimeViewportFrame Frame(
        bool withEmitter,
        double resolutionScale = 1,
        int width = 96,
        int height = 64)
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera", "Camera", "Camera3D", true,
            0, 0, -4,
            FieldOfViewDegrees: 65,
            NearClip: 0.1,
            FarClip: 50,
            ClearColor: "#020306");
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High", ResolutionScale: resolutionScale, Bloom: false, MaximumActiveParticles: 64),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("particle-native-test"),
            width,
            height);
        return new RekallAgeRuntimeViewportFrame(
            "Particle Native Test", 1, 1.0 / 60.0, width, height,
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
