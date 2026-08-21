using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeEntityQuerySdkTests
{
    [Fact]
    public void RuntimeModuleSdkQueriesEntitiesByTagAndComponent()
    {
        var enemy = CreateEntity(
            "enemy_a",
            "Enemy A",
            ["Enemy", "target"],
            new RekallAgeRuntimeComponent("Game.Health", new JsonObject { ["value"] = 10 }));
        var pickup = CreateEntity(
            "pickup",
            "Pickup",
            ["target"],
            new RekallAgeRuntimeComponent("Game.Pickup", new JsonObject { ["kind"] = "coin" }));
        var hiddenEnemy = CreateEntity(
            "enemy_hidden",
            "Enemy Hidden",
            ["enemy"],
            new RekallAgeRuntimeComponent("Game.Health", new JsonObject { ["value"] = 5 }),
            visible: false);
        var world = CreateWorld(pickup, hiddenEnemy, enemy);

        Assert.Equal(["enemy_a", "enemy_hidden"], world.EntitiesWithTag("enemy").Select(entity => entity.Id));
        Assert.Equal(["enemy_a", "enemy_hidden"], world.EntitiesWithComponent("Game.Health").Select(entity => entity.Id));
        Assert.Equal(["enemy_a", "enemy_hidden"], world.EntitiesWithTagAndComponent("enemy", "Game.Health").Select(entity => entity.Id));
        Assert.Equal(["pickup"], world.EntitiesWithTagAndComponent("target", "Game.Pickup").Select(entity => entity.Id));
    }

    [Fact]
    public void RuntimeModuleSdkFindsEntitiesByStableIdentifiers()
    {
        var world = CreateWorld(
            CreateEntity("door", "Door", ["interactive"]),
            CreateEntity("door_clone", "Door", ["interactive"]),
            CreateEntity("player", "Player", ["actor"]));

        Assert.Equal("door", world.FindEntity("door")?.Id);
        Assert.Equal("player", world.FindEntity("PLAYER")?.Id);
        Assert.Equal(["door", "door_clone"], world.EntitiesNamed("Door").Select(entity => entity.Id));
        Assert.Null(world.FindEntity("Door"));
        Assert.Null(world.FindEntity("missing"));
        Assert.Empty(world.EntitiesNamed(""));
    }

    [Fact]
    public void RuntimeModuleSdkFindEntityGivesExactIdPrecedenceOverUniqueNameFallback()
    {
        var world = CreateWorld(
            CreateEntity("target", "By Id", []),
            CreateEntity("other", "target", []));

        Assert.Equal("target", world.FindEntity("target")?.Id);
        Assert.Equal("other", world.FindEntity("TARGET")?.Id);
    }

    [Fact]
    public void RuntimeModuleSdkUpdatesEntitiesWithoutManualWorldListSurgery()
    {
        var actor = CreateEntity("actor", "Actor", ["unit"]);
        var prop = CreateEntity("prop", "Prop", ["decor"]);
        var world = CreateWorld(actor, prop);

        var updated = world
            .UpdateEntity("actor", entity => entity
                .WithPosition3D(new RekallAgeRuntimeVector3(1, 2, 3))
                .WithTag("selected"))
            .UpdateEntitiesWithTag("decor", entity => entity.WithVisible(false));

        Assert.Equal(new RekallAgeRuntimeVector3(1, 2, 3), updated.FindEntity("actor")!.Transform.Position3D);
        Assert.Equal(["selected", "unit"], updated.FindEntity("actor")!.Tags);
        Assert.False(updated.FindEntity("prop")!.Visible);
        Assert.Same(updated, updated.UpdateEntity("missing", entity => entity.WithVisible(false)));
    }

    [Fact]
    public void RuntimeModuleSdkUpdatesEntitiesByComponentAndCanRemoveTags()
    {
        var enemy = CreateEntity(
            "enemy",
            "Enemy",
            ["enemy", "active"],
            new RekallAgeRuntimeComponent("Game.Health", new JsonObject { ["value"] = 10 }));
        var pickup = CreateEntity(
            "pickup",
            "Pickup",
            ["active"],
            new RekallAgeRuntimeComponent("Game.Pickup", new JsonObject()));
        var world = CreateWorld(enemy, pickup);

        var updated = world
            .UpdateEntitiesWithComponent("Game.Health", entity => entity.WithoutTag("active"))
            .ReplaceEntity(pickup.WithTag("collected"));

        Assert.Equal(["enemy"], updated.FindEntity("enemy")!.Tags);
        Assert.Equal(["active", "collected"], updated.FindEntity("pickup")!.Tags);
        Assert.Same(updated, updated.ReplaceEntity(CreateEntity("missing", "Missing", [])));
    }

    [Fact]
    public void RuntimeModuleSdkRemovesAnEntityWithoutSentinelValuesOrManualListSurgery()
    {
        var world = CreateWorld(
            CreateEntity("player", "Player", ["actor"]),
            CreateEntity("pickup", "Pickup", ["collectible"]));

        var updated = world.RemoveEntity("pickup");

        Assert.Equal(["player"], updated.Entities.Select(entity => entity.Id));
        Assert.Same(updated, updated.RemoveEntity("missing"));
        Assert.Same(updated, updated.RemoveEntity(" "));
    }

    [Fact]
    public void RuntimeModuleSdkCreatesAndAddsAnEntityWithoutManualWorldListSurgery()
    {
        var world = CreateWorld(CreateEntity("player", "Player", ["actor"]));
        var spawned = RekallAgeRuntimeModuleSdk.CreateEntity(" block_1 ", " Falling Block ")
            .WithTag("spawned")
            .WithPosition3D(new RekallAgeRuntimeVector3(0, 8, 0))
            .WithRotation3D(new RekallAgeRuntimeVector3(10, 20, 30))
            .WithComponentNumber("Rekall.BoxCollider3D", "width", 1)
            .WithComponentNumber("Rekall.Rigidbody3D", "mass", 1);

        var updated = world.AddEntity(spawned);

        Assert.Equal(["player", "block_1"], updated.Entities.Select(entity => entity.Id));
        Assert.Equal("Falling Block", updated.FindEntity("block_1")!.Name);
        Assert.Equal(["spawned"], updated.FindEntity("block_1")!.Tags);
        Assert.Equal(8, updated.FindEntity("block_1")!.Transform.Position3D.Y);
        Assert.Equal(20, updated.FindEntity("block_1")!.Transform.Rotation3D.Y);
        var authoredTransform = updated.FindEntity("block_1")!.FindComponent("Rekall.Transform3D");
        Assert.NotNull(authoredTransform);
        Assert.Equal(8, authoredTransform.Properties["y"]!.GetValue<double>());
        Assert.Equal(20, authoredTransform.Properties["yaw"]!.GetValue<double>());
        Assert.Equal(1, updated.FindEntity("block_1")!.ComponentNumber("Rekall.Rigidbody3D", "mass"));
        Assert.Same(updated, updated.AddEntity(spawned));
    }

    [Fact]
    public void RuntimeModuleSdkProvidesStatelessDeterministicRandomValues()
    {
        var first = RekallAgeRuntimeModuleSdk.DeterministicUnit(42, 7);
        var repeated = RekallAgeRuntimeModuleSdk.DeterministicUnit(42, 7);
        var next = RekallAgeRuntimeModuleSdk.DeterministicUnit(42, 8);
        var ranged = RekallAgeRuntimeModuleSdk.DeterministicRange(42, 7, -180, 180);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, next);
        Assert.InRange(first, 0, Math.BitDecrement(1d));
        Assert.Equal(-180 + (first * 360), ranged);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RekallAgeRuntimeModuleSdk.DeterministicRange(42, 7, 1, 1));
    }

    [Fact]
    public void RuntimeModuleSdkReadsAndWritesComponentStateWithoutJsonBoilerplate()
    {
        var entity = CreateEntity(
            "runner",
            "Runner",
            ["actor"],
            new RekallAgeRuntimeComponent("Game.Runner", new JsonObject
            {
                ["speed"] = 12.5,
                ["enabled"] = true,
                ["label"] = "ready"
            }));

        Assert.Equal(12.5, entity.ComponentNumber("Game.Runner", "speed", 1));
        Assert.True(entity.ComponentBoolean("Game.Runner", "enabled", false));
        Assert.Equal("ready", entity.ComponentString("Game.Runner", "label", "missing"));
        Assert.Equal(7, entity.ComponentNumber("Missing", "speed", 7));

        var updated = entity
            .WithComponentNumber("Game.Runner", "score", 3)
            .WithComponentBoolean("Game.Runner", "enabled", false)
            .WithComponentString("Game.Runner", "label", "charged");

        Assert.Equal(3, updated.ComponentNumber("Game.Runner", "score", 0));
        Assert.False(updated.ComponentBoolean("Game.Runner", "enabled", true));
        Assert.Equal("charged", updated.ComponentString("Game.Runner", "label"));
        Assert.Equal(new RekallAgeRuntimeVector3(0, 0, 0), updated.Transform.Position3D);
    }

    private static RekallAgeRuntimeWorld CreateWorld(params RekallAgeRuntimeEntity[] entities)
    {
        return new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            entities,
            RekallAgeRuntimeSubsystemViews.Empty,
            []);
    }

    private static RekallAgeRuntimeEntity CreateEntity(
        string id,
        string name,
        IReadOnlyList<string> tags,
        RekallAgeRuntimeComponent? component = null,
        bool visible = true)
    {
        return new RekallAgeRuntimeEntity(
            id,
            name,
            tags,
            null,
            null,
            visible,
            false,
            RekallAgeRuntimeTransform.Identity,
            component is null ? [] : [component]);
    }
}
