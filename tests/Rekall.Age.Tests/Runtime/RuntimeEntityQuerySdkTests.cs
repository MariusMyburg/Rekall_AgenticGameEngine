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
        Assert.Equal(["door", "door_clone"], world.EntitiesNamed("Door").Select(entity => entity.Id));
        Assert.Null(world.FindEntity("missing"));
        Assert.Empty(world.EntitiesNamed(""));
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
