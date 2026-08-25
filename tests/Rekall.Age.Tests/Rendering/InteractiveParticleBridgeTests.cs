using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class InteractiveParticleBridgeTests
{
    [Fact]
    public void AuthoredEmitterProducesBoundedDeterministicVisibleParticles()
    {
        var emitter = HighFidelityRenderGraphTests.ParticleEmitter(64);
        var range = new RekallAgeVulkanParticleEmitterRange(
            emitter.EntityId, emitter.EntityName, 0, 64, 0, 1, emitter.DeterministicSeed,
            emitter.SimulationSpace, emitter.DrawMode, emitter.BlendMode, emitter.Lit,
            emitter.EmissiveIntensity, emitter.SoftParticleFade, emitter.TextureAssetId,
            emitter.Priority, emitter.VisibilityDistance, emitter.Layer, emitter);
        var plan = new RekallAgeVulkanParticlePlan(
            [range], 64, 1, new(1, 1, 1), [], [], []);
        var bridge = new RekallAgeInteractiveParticleBridge();

        var first = bridge.Build(plan, 1.25, 1.0 / 60.0);
        var second = bridge.Build(plan, 1.25, 1.0 / 60.0);

        Assert.Equal("cpu-deterministic-sim/gpu-quad-draw", first.ExecutionMode);
        Assert.NotEmpty(first.Particles);
        Assert.Equal(first.Particles, second.Particles);
        Assert.InRange(first.ActiveParticleCount, 1, RekallAgeInteractiveParticleBridge.MaximumParticles);
    }
}
