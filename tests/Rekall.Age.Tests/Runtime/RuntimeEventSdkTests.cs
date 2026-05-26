using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeEventSdkTests
{
    [Fact]
    public void RuntimeModuleSdkQueriesEventsByTypeAndEntity()
    {
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            4,
            TimeSpan.FromSeconds(1),
            [],
            RekallAgeRuntimeSubsystemViews.Empty with
            {
                Events = new RekallAgeRuntimeEventView(
                [
                    new RekallAgeRuntimeEvent(
                        4,
                        "entity.begin",
                        "ent_player",
                        "Player",
                        "runtime.lifecycle",
                        "spawn",
                        new JsonObject { ["reason"] = "first-frame" }),
                    new RekallAgeRuntimeEvent(
                        4,
                        "entity.tick",
                        "ent_player",
                        "Player",
                        "runtime.lifecycle",
                        "patrol",
                        new JsonObject { ["deltaSeconds"] = 1.0 / 60.0 }),
                    new RekallAgeRuntimeEvent(
                        4,
                        "entity.tick",
                        "ent_door",
                        "Door",
                        "runtime.lifecycle",
                        "openDoor",
                        new JsonObject())
                ])
            },
            []);

        Assert.Equal(2, world.EventsOfType("entity.tick").Count);
        Assert.Equal(2, world.EventsFor("ent_player").Count);
        Assert.True(world.WasEventRaised("ent_player", "entity.begin"));
        Assert.True(world.WasEventRaised("ent_player", "ENTITY.TICK"));
        Assert.False(world.WasEventRaised("ent_door", "entity.begin"));
        Assert.Empty(world.EventsFor(""));
    }

    [Fact]
    public void RuntimeModuleSdkEmitsCustomEvents()
    {
        var entity = CreateEntity("quest", "Quest");
        var payload = new JsonObject { ["questId"] = "intro" };
        var world = CreateWorld(entity);

        var updated = world.EmitEvent(
            entity,
            "quest.started",
            "game.quest",
            "beginIntroQuest",
            payload);
        payload["questId"] = "mutated";

        var runtimeEvent = Assert.Single(updated.Subsystems.Events.Events);
        Assert.Equal(world.FrameIndex, runtimeEvent.Frame);
        Assert.Equal("quest.started", runtimeEvent.Type);
        Assert.Equal("quest", runtimeEvent.EntityId);
        Assert.Equal("Quest", runtimeEvent.EntityName);
        Assert.Equal("game.quest", runtimeEvent.Source);
        Assert.Equal("beginIntroQuest", runtimeEvent.Handler);
        Assert.Equal("intro", runtimeEvent.Payload["questId"]!.GetValue<string>());
    }

    [Fact]
    public void RuntimeModuleSdkEmitsOnlyMatchingBoundEvents()
    {
        var entity = CreateEntity(
            "score",
            "Score",
            new JsonArray
            {
                new JsonObject { ["event"] = "score.changed", ["handler"] = "updateHud" },
                new JsonObject { ["event"] = "score.changed", ["handler"] = "ignored", ["active"] = false },
                new JsonObject { ["event"] = "quest.started", ["handler"] = "ignored" }
            });
        var world = CreateWorld(entity);

        var updated = world.EmitBoundEvents(
            entity,
            "score.changed",
            "game.score",
            new JsonObject { ["amount"] = 10 });

        var runtimeEvent = Assert.Single(updated.Subsystems.Events.Events);
        Assert.Equal("score.changed", runtimeEvent.Type);
        Assert.Equal("game.score", runtimeEvent.Source);
        Assert.Equal("updateHud", runtimeEvent.Handler);
        Assert.Equal(10, runtimeEvent.Payload["amount"]!.GetValue<int>());
    }

    private static RekallAgeRuntimeWorld CreateWorld(params RekallAgeRuntimeEntity[] entities)
    {
        return new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            7,
            TimeSpan.FromSeconds(1),
            entities,
            RekallAgeRuntimeSubsystemViews.Empty,
            []);
    }

    private static RekallAgeRuntimeEntity CreateEntity(
        string id,
        string name,
        JsonArray? events = null)
    {
        var components = new List<RekallAgeRuntimeComponent>();
        if (events is not null)
        {
            components.Add(new RekallAgeRuntimeComponent(
                "Rekall.EventBindings",
                new JsonObject { ["events"] = events }));
        }

        return new RekallAgeRuntimeEntity(
            id,
            name,
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            components);
    }
}
