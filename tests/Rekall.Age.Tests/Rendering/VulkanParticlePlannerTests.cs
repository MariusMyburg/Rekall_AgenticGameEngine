using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanParticlePlannerTests
{
    [Fact]
    public void SameEmitterSeedFrameAndDeltaProduceIdenticalStableSpawnRanges()
    {
        var frame = Frame(frameIndex: 15, elapsedSeconds: 0.25, Emitter("emitter", priority: 2, capacity: 64) with
        {
            SpawnRate = 120,
            DeterministicSeed = 8675309
        });
        var quality = new RekallAgeResolvedParticleQuality(64);

        var first = new RekallAgeVulkanParticlePlanner().Plan(frame, quality, deltaSeconds: 1.0 / 60.0);
        var second = new RekallAgeVulkanParticlePlanner().Plan(frame, quality, deltaSeconds: 1.0 / 60.0);

        Assert.Equal(first.Emitters, second.Emitters);
        var emitter = Assert.Single(first.Emitters);
        Assert.Equal("emitter", emitter.EntityId);
        Assert.Equal(0, emitter.AllocationOffset);
        Assert.Equal(64, emitter.AllocationCapacity);
        Assert.Equal(2, emitter.SpawnCount);
        Assert.InRange(emitter.SpawnStart, 0, 63);
        Assert.Equal(new RekallAgeVulkanParticleDispatch(1, 1, 1), first.SimulationDispatch);
    }

    [Fact]
    public void OverflowAllocatesByAuthoredPriorityThenStableEntityIdAndReportsRemainder()
    {
        var frame = Frame(
            Emitter("low", priority: 1, capacity: 8),
            Emitter("z-high", priority: 9, capacity: 8),
            Emitter("a-high", priority: 9, capacity: 8));

        var plan = new RekallAgeVulkanParticlePlanner().Plan(
            frame,
            new RekallAgeResolvedParticleQuality(10),
            deltaSeconds: 1.0 / 60.0);

        Assert.Equal(["a-high", "z-high"], plan.Emitters.Select(item => item.EntityId));
        Assert.Equal([(0, 8), (8, 2)], plan.Emitters.Select(item => (item.AllocationOffset, item.AllocationCapacity)));
        Assert.Equal(["low", "z-high"], plan.OverflowEntityIds);
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_CAPACITY_OVERFLOW"
            && item.EntityIds.SequenceEqual(["low", "z-high"]));
    }

    [Fact]
    public void UnsafeLifetimeCurveAndCapacityAreRejectedBeforeAllocation()
    {
        var frame = Frame(
            Emitter("infinite-life", priority: 5, capacity: 16) with { LifetimeSeconds = double.PositiveInfinity },
            Emitter("nonfinite-curve", priority: 4, capacity: 16) with
            {
                SizeCurve = [new RekallAgeRuntimeViewportParticleScalarKey(0.5, double.NaN)]
            },
            Emitter("unsafe-capacity", priority: 3, capacity: RekallAgeVulkanParticlePlanner.MaximumEmitterCapacity + 1),
            Emitter("valid", priority: 1, capacity: 4));

        var plan = new RekallAgeVulkanParticlePlanner().Plan(
            frame,
            new RekallAgeResolvedParticleQuality(RekallAgeVulkanParticlePlanner.MaximumGlobalCapacity),
            deltaSeconds: 1.0 / 60.0);

        var valid = Assert.Single(plan.Emitters);
        Assert.Equal("valid", valid.EntityId);
        Assert.Equal(4, plan.AllocatedCapacity);
        Assert.Equal(["infinite-life", "nonfinite-curve", "unsafe-capacity"], plan.RejectedEntityIds);
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_LIFETIME_INVALID");
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_SIZE_CURVE_INVALID");
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_EMITTER_CAPACITY_UNSAFE");
    }

    [Fact]
    public void InvalidFlipbookAndBlendParametersAreRejectedBeforeGpuPacking()
    {
        var frame = Frame(
            Emitter("bad-flipbook", 3, 8) with { FlipbookColumns = 0, FlipbookFramesPerSecond = double.NaN },
            Emitter("bad-blend", 2, 8) with { BlendMode = "multiply" },
            Emitter("valid-additive", 1, 8) with { BlendMode = "additive", FlipbookColumns = 4, FlipbookRows = 2 });

        var plan = new RekallAgeVulkanParticlePlanner().Plan(frame, new(24), 1.0 / 60.0);

        Assert.Equal(["valid-additive"], plan.Emitters.Select(item => item.EntityId));
        Assert.Equal(["bad-blend", "bad-flipbook"], plan.RejectedEntityIds);
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_FLIPBOOK_INVALID");
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_BLEND_MODE_UNSUPPORTED");
    }

    [Fact]
    public void DisabledAndZeroRateEmittersAllocateNoActiveSlots()
    {
        var frame = Frame(
            Emitter("disabled", priority: 10, capacity: 64) with { Enabled = false },
            Emitter("zero", priority: 9, capacity: 64) with { SpawnRate = 0, Bursts = [] });

        var plan = new RekallAgeVulkanParticlePlanner().Plan(
            frame,
            new RekallAgeResolvedParticleQuality(128),
            deltaSeconds: 1.0 / 60.0);

        Assert.Empty(plan.Emitters);
        Assert.Equal(0, plan.AllocatedCapacity);
        Assert.Equal(0, plan.PlannedSpawnCount);
        Assert.Equal(new RekallAgeVulkanParticleDispatch(0, 0, 0), plan.SimulationDispatch);
    }

    [Fact]
    public void UnsupportedDrawModesLayerMasksAndVisibilityDistanceDegradeExplicitly()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera", "Camera", "Camera3D", true, 0, 0, 0, CullingMask: "effects");
        var frame = Frame(
            Emitter("beam", 9, 8) with { DrawMode = "beam", Layer = "effects" },
            Emitter("masked", 8, 8) with { Layer = "other" },
            Emitter("distant", 7, 8) with
            {
                Layer = "effects",
                VisibilityDistance = 2,
                Transform = new RekallAgeRuntimeViewportTransform(0, 0, 20, 0, 0, 0, 1, 1, 1)
            },
            Emitter("quad", 1, 8) with { Layer = "effects" }) with
        {
            ActiveCamera = camera,
            Cameras = [camera]
        };

        var plan = new RekallAgeVulkanParticlePlanner().Plan(frame, new(64), 1.0 / 60.0);

        Assert.Equal(["quad"], plan.Emitters.Select(item => item.EntityId));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_DRAW_MODE_UNSUPPORTED"
            && item.EntityIds.SequenceEqual(["beam"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_CAMERA_CULLED"
            && item.EntityIds.SequenceEqual(["distant", "masked"]));
    }

    [Fact]
    public void PersistentStateRequiresAnIdenticalEmitterRangeTopology()
    {
        var planner = new RekallAgeVulkanParticlePlanner();
        var initial = planner.Plan(
            Frame(10, 1, Emitter("a", 2, 8), Emitter("b", 1, 8)),
            new(16),
            1.0 / 60.0);
        var history = new RekallAgeVulkanParticleHistory(
            initial.AllocatedCapacity,
            10,
            LastDestinationIsA: false,
            initial.TopologyFingerprint);

        var unchanged = planner.Plan(
            Frame(11, 1 + 1.0 / 60.0, Emitter("b", 1, 8), Emitter("a", 2, 8)),
            new(16),
            1.0 / 60.0,
            history);
        var replacement = planner.Plan(
            Frame(11, 1 + 1.0 / 60.0, Emitter("a", 2, 8), Emitter("c", 1, 8)),
            new(16),
            1.0 / 60.0,
            history);
        var reorderedRanges = planner.Plan(
            Frame(11, 1 + 1.0 / 60.0, Emitter("a", 1, 8), Emitter("b", 2, 8)),
            new(16),
            1.0 / 60.0,
            history);
        var removalWithEqualCapacity = planner.Plan(
            Frame(11, 1 + 1.0 / 60.0, Emitter("a", 2, 16)),
            new(16),
            1.0 / 60.0,
            history);

        Assert.True(unchanged.PreviousStateReused);
        Assert.Equal(initial.TopologyFingerprint, unchanged.TopologyFingerprint);
        Assert.False(replacement.PreviousStateReused);
        Assert.False(reorderedRanges.PreviousStateReused);
        Assert.False(removalWithEqualCapacity.PreviousStateReused);
        Assert.NotEqual(initial.TopologyFingerprint, replacement.TopologyFingerprint);
        Assert.NotEqual(initial.TopologyFingerprint, reorderedRanges.TopologyFingerprint);
        Assert.NotEqual(initial.TopologyFingerprint, removalWithEqualCapacity.TopologyFingerprint);
    }

    [Fact]
    public void InvalidSimulationAndPackableParametersAreRejectedWithStableDiagnostics()
    {
        var plan = new RekallAgeVulkanParticlePlanner().Plan(
            Frame(
                Emitter("color", 10, 8) with { ColorCurve = [new(0, "not-a-color"), new(1, "#ffffffff")] },
                Emitter("size", 9, 8) with { SizeCurve = [new(0, 1), new(0.5, -1), new(1, 1)] },
                Emitter("cone", 8, 8) with { VelocityConeDegrees = 90 },
                Emitter("speed", 7, 8) with { MinimumSpeed = -1 },
                Emitter("drag", 6, 8) with { Drag = -1 },
                Emitter("emission", 5, 8) with { SpawnRate = -1 },
                Emitter("emissive", 4, 8) with { EmissiveIntensity = -1 },
                Emitter("fade", 3, 8) with { SoftParticleFade = -1 },
                Emitter("space", 2, 8) with { SimulationSpace = "screen" }),
            new(72),
            1.0 / 60.0);

        Assert.Empty(plan.Emitters);
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_COLOR_INVALID" && item.EntityIds.SequenceEqual(["color"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_SIZE_CURVE_INVALID" && item.EntityIds.SequenceEqual(["size"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_MOTION_INVALID" && item.EntityIds.SequenceEqual(["cone"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_MOTION_INVALID" && item.EntityIds.SequenceEqual(["speed"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_MOTION_INVALID" && item.EntityIds.SequenceEqual(["drag"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_EMISSION_INVALID" && item.EntityIds.SequenceEqual(["emission"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_APPEARANCE_INVALID" && item.EntityIds.SequenceEqual(["emissive"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_APPEARANCE_INVALID" && item.EntityIds.SequenceEqual(["fade"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_SIMULATION_SPACE_UNSUPPORTED" && item.EntityIds.SequenceEqual(["space"]));
    }

    [Fact]
    public void FourKeyCurvesPreserveAuthoredMiddleKeysAndTimes()
    {
        var emitter = Emitter("curved", 1, 8) with
        {
            SizeCurve = [new(0, 1), new(0.25, 4), new(0.75, 2), new(1, 1)],
            ColorCurve = [new(0, "#ff0000ff"), new(0.4, "#00ff00ff"), new(0.8, "#0000ffff"), new(1, "#ffffffff")]
        };

        var range = Assert.Single(new RekallAgeVulkanParticlePlanner().Plan(Frame(emitter), new(8), 1.0 / 60.0).Emitters);

        Assert.Equal([0, 0.25, 0.75, 1], range.Source.SizeCurve.Select(key => key.NormalizedAge));
        Assert.Equal(["#ff0000ff", "#00ff00ff", "#0000ffff", "#ffffffff"], range.Source.ColorCurve.Select(key => key.Color));
    }

    [Fact]
    public void GpuPackedParticleNumbersAcceptExactFloatMaximumBoundary()
    {
        var maximum = (double)float.MaxValue;
        var frame = Frame(
            Emitter("speed", 8, 8) with { MinimumSpeed = maximum, MaximumSpeed = maximum },
            Emitter("drag", 7, 8) with { Drag = maximum },
            Emitter("gravity", 6, 8) with { GravityX = maximum },
            Emitter("direction", 5, 8) with { VelocityDirectionX = maximum, VelocityDirectionY = 0 },
            Emitter("size", 4, 8) with { SizeCurve = [new(0, maximum), new(1, maximum)] },
            Emitter("appearance", 3, 8) with { EmissiveIntensity = maximum, SoftParticleFade = maximum },
            Emitter("flipbook", 2, 8) with { FlipbookFramesPerSecond = maximum },
            Emitter("transform", 1, 8) with
            {
                Transform = new(maximum, maximum, maximum, 0, 0, 0, 1, 1, 1)
            });

        var plan = new RekallAgeVulkanParticlePlanner().Plan(frame, new(64), 1.0 / 60.0);

        Assert.Equal(8, plan.Emitters.Count);
        Assert.Empty(plan.RejectedEntityIds);
    }

    [Fact]
    public void FiniteNumbersBeyondFloatRangeRejectBeforeGpuPackingByCategory()
    {
        var above = Math.BitIncrement((double)float.MaxValue);
        var huge = double.MaxValue;
        var frame = Frame(
            Emitter("speed", 9, 8) with { MaximumSpeed = above },
            Emitter("drag", 8, 8) with { Drag = huge },
            Emitter("gravity", 7, 8) with { GravityZ = above },
            Emitter("direction", 6, 8) with { VelocityDirectionX = huge },
            Emitter("size", 5, 8) with { SizeCurve = [new(0, above)] },
            Emitter("emissive", 4, 8) with { EmissiveIntensity = huge },
            Emitter("fade", 3, 8) with { SoftParticleFade = above },
            Emitter("flipbook", 2, 8) with { FlipbookFramesPerSecond = huge },
            Emitter("transform", 1, 8) with
            {
                Transform = new(above, 0, 0, 0, 0, 0, 1, 1, 1)
            });

        var plan = new RekallAgeVulkanParticlePlanner().Plan(frame, new(72), 1.0 / 60.0);

        Assert.Empty(plan.Emitters);
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_MOTION_INVALID" && item.EntityIds.SequenceEqual(["speed"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_MOTION_INVALID" && item.EntityIds.SequenceEqual(["drag"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_MOTION_INVALID" && item.EntityIds.SequenceEqual(["gravity"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_MOTION_INVALID" && item.EntityIds.SequenceEqual(["direction"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_SIZE_CURVE_INVALID" && item.EntityIds.SequenceEqual(["size"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_APPEARANCE_INVALID" && item.EntityIds.SequenceEqual(["emissive"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_APPEARANCE_INVALID" && item.EntityIds.SequenceEqual(["fade"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_FLIPBOOK_INVALID" && item.EntityIds.SequenceEqual(["flipbook"]));
        Assert.Contains(plan.Diagnostics, item => item.Code == "REKALL_PARTICLE_TRANSFORM_INVALID" && item.EntityIds.SequenceEqual(["transform"]));
    }

    private static RekallAgeRuntimeViewportFrame Frame(params RekallAgeRuntimeViewportParticleEmitter[] emitters) =>
        Frame(1, 1.0 / 60.0, emitters);

    private static RekallAgeRuntimeViewportFrame Frame(
        int frameIndex,
        double elapsedSeconds,
        params RekallAgeRuntimeViewportParticleEmitter[] emitters) => new(
            "Particle Planner",
            frameIndex,
            elapsedSeconds,
            256,
            144,
            null,
            [],
            [],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            [])
        {
            ParticleEmitters = emitters
        };

    private static RekallAgeRuntimeViewportParticleEmitter Emitter(
        string id,
        int priority,
        int capacity) => new(
            id,
            id,
            Enabled: true,
            SimulationSpace: "world",
            Capacity: capacity,
            SpawnRate: 60,
            Bursts: [],
            LifetimeSeconds: 2,
            DeterministicSeed: 1,
            VelocityDirectionX: 0,
            VelocityDirectionY: 1,
            VelocityDirectionZ: 0,
            VelocityConeDegrees: 0,
            MinimumSpeed: 1,
            MaximumSpeed: 1,
            GravityX: 0,
            GravityY: 0,
            GravityZ: 0,
            Drag: 0,
            SizeCurve: [new RekallAgeRuntimeViewportParticleScalarKey(0, 1), new RekallAgeRuntimeViewportParticleScalarKey(1, 1)],
            ColorCurve: [new RekallAgeRuntimeViewportParticleColorKey(0, "#ffffffff"), new RekallAgeRuntimeViewportParticleColorKey(1, "#ffffff00")],
            DrawMode: "quad",
            Lit: false,
            EmissiveIntensity: 1,
            SoftParticleFade: 0,
            TextureAssetId: null,
            FlipbookColumns: 1,
            FlipbookRows: 1,
            FlipbookFramesPerSecond: 0,
            BlendMode: "alpha",
            Priority: priority,
            VisibilityDistance: 100,
            Layer: "default",
            Transform: RekallAgeRuntimeViewportTransform.Identity);
}
