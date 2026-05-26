using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeLifecycleEventSystemTests
{
    [Fact]
    public async Task LifecycleSystemEmitsBeginAndTickForOptedInEntities()
    {
        var world = CreateWorld(
            "Player",
            true,
            new JsonArray
            {
                new JsonObject { ["event"] = "entity.begin", ["handler"] = "spawn" },
                new JsonObject { ["event"] = "entity.tick", ["handler"] = "patrol" }
            });

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var events = result.World.Subsystems.Events.Events;
        var begin = Assert.Single(events, runtimeEvent => runtimeEvent.Type == "entity.begin");
        var tick = Assert.Single(events, runtimeEvent => runtimeEvent.Type == "entity.tick");
        Assert.Equal("Player", begin.EntityName);
        Assert.Equal("spawn", begin.Handler);
        Assert.Equal("runtime.lifecycle", begin.Source);
        Assert.Equal("first-frame", begin.Payload["reason"]!.GetValue<string>());
        Assert.Equal("patrol", tick.Handler);
        Assert.Equal(1.0 / 60.0, tick.Payload["deltaSeconds"]!.GetValue<double>(), precision: 6);
        Assert.Contains("runtime.events.lifecycle", result.SystemsRun);
    }

    [Fact]
    public async Task LifecycleSystemDoesNotRepeatBeginAfterFirstFrame()
    {
        var world = CreateWorld(
            "Player",
            true,
            new JsonArray
            {
                new JsonObject { ["event"] = "entity.begin", ["handler"] = "spawn" },
                new JsonObject { ["event"] = "entity.tick", ["handler"] = "patrol" }
            });

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 2, CancellationToken.None);

        Assert.DoesNotContain(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "entity.begin");
        Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "entity.tick" && runtimeEvent.Frame == 2);
    }

    [Fact]
    public async Task LifecycleSystemSkipsInactiveBindingsAndInvisibleEntities()
    {
        var visibleInactive = CreateEntity(
            "visible",
            "Visible Inactive",
            true,
            new JsonArray
            {
                new JsonObject { ["event"] = "entity.tick", ["handler"] = "ignored", ["active"] = false }
            });
        var hiddenActive = CreateEntity(
            "hidden",
            "Hidden Active",
            false,
            new JsonArray
            {
                new JsonObject { ["event"] = "entity.tick", ["handler"] = "ignored" }
            });
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [visibleInactive, hiddenActive],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        Assert.Empty(result.World.Subsystems.Events.Events);
    }

    [Fact]
    public async Task LifecycleSystemPreservesExistingRuntimeEvents()
    {
        var world = CreateWorld(
            "Player",
            true,
            new JsonArray
            {
                new JsonObject { ["event"] = "entity.tick", ["handler"] = "patrol" }
            }) with
        {
            Subsystems = RekallAgeRuntimeSubsystemViews.Empty with
            {
                Events = new RekallAgeRuntimeEventView(
                [
                    new RekallAgeRuntimeEvent(
                        1,
                        "pointer.hit",
                        "target",
                        "Target",
                        "runtime.pointer",
                        "inspect",
                        new JsonObject())
                ])
            }
        };

        var result = await new RekallAgeLifecycleEventSystem()
            .UpdateAsync(
                world,
                new RekallAgeRuntimeWorldFrameContext(
                    1,
                    TimeSpan.FromSeconds(1.0 / 60.0),
                    TimeSpan.FromSeconds(1.0 / 60.0),
                    CancellationToken.None));

        Assert.Contains(result.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "pointer.hit");
        Assert.Contains(result.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "entity.tick");
    }

    private static RekallAgeRuntimeWorld CreateWorld(
        string entityName,
        bool visible,
        JsonArray events)
    {
        return new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [CreateEntity("player", entityName, visible, events)],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);
    }

    private static RekallAgeRuntimeEntity CreateEntity(
        string id,
        string name,
        bool visible,
        JsonArray events)
    {
        return new RekallAgeRuntimeEntity(
            id,
            name,
            [],
            null,
            null,
            visible,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.EventBindings",
                    new JsonObject
                    {
                        ["events"] = events
                    })
            ]);
    }
}
