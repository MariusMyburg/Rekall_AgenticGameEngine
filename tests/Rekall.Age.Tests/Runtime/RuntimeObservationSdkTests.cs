using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeObservationSdkTests
{
    [Fact]
    public void RuntimeModuleSdkEmitsEntityTargetedObservations()
    {
        var entity = CreateEntity("door", "Door");
        var world = CreateWorld(entity);

        var updated = world.EmitObservation(
            entity,
            "GAME_LOCKED_DOOR_NO_KEY",
            "Warning",
            "gameplay",
            "DoorSystem",
            "Door is locked but no key entity was authored.",
            [" rekall.context.scene ", "", "rekall.world.inspect_entity"]);

        var observation = Assert.Single(updated.Observations);
        Assert.Equal(world.FrameIndex, observation.Frame);
        Assert.Equal("GAME_LOCKED_DOOR_NO_KEY", observation.Code);
        Assert.Equal("warning", observation.Severity);
        Assert.Equal("gameplay", observation.Subsystem);
        Assert.Equal("door", observation.TargetId);
        Assert.Equal("Door", observation.TargetName);
        Assert.Equal("DoorSystem", observation.System);
        Assert.Equal("Door is locked but no key entity was authored.", observation.Message);
        Assert.Equal(["rekall.context.scene", "rekall.world.inspect_entity"], observation.SuggestedCommands);
    }

    [Fact]
    public void RuntimeModuleSdkEmitsSceneTargetedObservationsAndQueriesThem()
    {
        var entity = CreateEntity("spawner", "Spawner");
        var world = CreateWorld(entity)
            .EmitSceneObservation(
                "GAME_NO_SPAWN_POINTS",
                "blocking",
                "gameplay",
                "SpawnSystem",
                "No spawn points were authored.")
            .EmitObservation(
                entity,
                "GAME_SPAWNER_DISABLED",
                "info",
                "gameplay",
                "SpawnSystem",
                "Spawner is disabled.");

        Assert.Equal("GAME_NO_SPAWN_POINTS", Assert.Single(world.ObservationsWithCode("game_no_spawn_points")).Code);
        Assert.Equal("GAME_SPAWNER_DISABLED", Assert.Single(world.ObservationsFor("spawner")).Code);
        Assert.Equal(2, world.ObservationsWithSeverity("INFO").Count + world.ObservationsWithSeverity("blocking").Count);
        Assert.True(world.HasBlockingObservations());

        var sceneObservation = Assert.Single(world.ObservationsForScene());
        Assert.Equal(world.SceneId, sceneObservation.TargetId);
        Assert.Equal(world.SceneName, sceneObservation.TargetName);
    }

    private static RekallAgeRuntimeWorld CreateWorld(params RekallAgeRuntimeEntity[] entities)
    {
        return new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            12,
            TimeSpan.FromSeconds(2),
            entities,
            RekallAgeRuntimeSubsystemViews.Empty,
            []);
    }

    private static RekallAgeRuntimeEntity CreateEntity(string id, string name)
    {
        return new RekallAgeRuntimeEntity(
            id,
            name,
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            []);
    }
}
