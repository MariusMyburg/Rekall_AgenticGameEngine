using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

/// <summary>
/// Runtime systems run every fixed step whether or not the scene contains anything they act
/// on, and the usual <c>world.Entities.Select(...).ToArray()</c> shape rebuilds the whole
/// entity array each time. Systems now guard that pass with a component-presence check.
///
/// These tests pin the two halves of that contract: an idle system must leave the world
/// untouched (reference-identical, so the saving is real and not just equal-valued), and a
/// system with work to do must still do it.
/// </summary>
public sealed class IdleSystemSkipTests
{
    private static RekallAgeRuntimeWorld WorldWith(params (string Type, JsonObject Properties)[] components)
    {
        var entity = RekallAgeEntityDocument.Create("Prop", ["prop"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()));
        foreach (var (type, properties) in components)
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(type, properties));
        }

        return new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity));
    }

    private static RekallAgeRuntimeWorldFrameContext Context() => new(
        FrameIndex: 1,
        DeltaTime: TimeSpan.FromSeconds(1.0 / 60.0),
        ElapsedTime: TimeSpan.FromSeconds(1.0 / 60.0),
        CancellationToken: CancellationToken.None);

    private static async Task<RekallAgeRuntimeWorld> Run(
        IRekallAgeRuntimeWorldSystem system,
        RekallAgeRuntimeWorld world) =>
        await system.UpdateAsync(world, Context());

    public static TheoryData<string, IRekallAgeRuntimeWorldSystem> IdleSystems() => new()
    {
        { "kepler", new RekallAgeKeplerOrbitSystem() },
        { "celestial-rotation", new RekallAgeCelestialRotationSystem() },
        { "morph", new RekallAgeMorphWeightSystem() },
        { "transform-animation", new RekallAgeTransformAnimationSystem(null) },
        { "trigger", new RekallAgeTriggerEventSystem() },
        { "collision", new RekallAgeCollisionEventSystem() },
        { "ui-layout", new RekallAgeUiLayoutSystem() },
        { "physics", new RekallAgeBepuPhysicsSystem() },
    };

    [Theory]
    [MemberData(nameof(IdleSystems))]
    public async Task SystemWithNothingToDoLeavesTheWorldUntouched(
        string label,
        IRekallAgeRuntimeWorldSystem system)
    {
        // A bare transform-only entity gives every one of these systems nothing to act on.
        var world = WorldWith();

        var result = await Run(system, world);

        Assert.True(
            ReferenceEquals(world.Entities, result.Entities),
            $"System '{label}' rebuilt the entity list despite having no work to do.");
    }

    [Fact]
    public async Task CelestialRotationStillRotatesWhenTheComponentIsPresent()
    {
        var world = WorldWith(("Rekall.CelestialRotation",
            new JsonObject { ["active"] = true, ["siderealPeriodSeconds"] = 0.05 }));

        var result = await Run(new RekallAgeCelestialRotationSystem(), world);

        Assert.NotEqual(
            world.Entities[0].Transform.Rotation3D.Y,
            result.Entities[0].Transform.Rotation3D.Y);
    }

    [Fact]
    public async Task MorphWeightStillProjectsStateWhenWeightsArePresent()
    {
        var world = WorldWith(("Rekall.MorphWeights",
            new JsonObject { ["weights"] = new JsonArray(0.5, 0.25) }));

        var result = await Run(new RekallAgeMorphWeightSystem(), world);

        Assert.Contains(
            result.Entities[0].Components,
            component => component.Type.Equals("Rekall.MorphState", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PhysicsStillSimulatesWhenAColliderIsPresent()
    {
        var world = WorldWith(
            ("Rekall.Rigidbody3D", new JsonObject { ["Mass"] = 1 }),
            ("Rekall.SphereCollider3D", new JsonObject { ["Radius"] = 0.5 }));

        var result = await Run(new RekallAgeBepuPhysicsSystem(), world);

        Assert.False(
            ReferenceEquals(world.Entities, result.Entities),
            "Physics skipped a scene that contains a rigidbody and collider.");
    }
}
