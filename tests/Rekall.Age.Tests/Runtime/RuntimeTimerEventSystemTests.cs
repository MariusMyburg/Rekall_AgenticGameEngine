using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeTimerEventSystemTests
{
    [Fact]
    public async Task TimerSystemEmitsElapsedWhenDurationIsReached()
    {
        var world = CreateWorld(CreateTimerEntity(
            "Spawner",
            durationSeconds: 1.0 / 60.0,
            repeat: false,
            [
                new JsonObject { ["event"] = "timer.elapsed", ["handler"] = "spawnWave" }
            ]));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var elapsed = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "timer.elapsed");
        Assert.Equal("Spawner", elapsed.EntityName);
        Assert.Equal("runtime.timer", elapsed.Source);
        Assert.Equal("spawnWave", elapsed.Handler);
        Assert.Equal("spawn", elapsed.Payload["timerId"]!.GetValue<string>());
        Assert.False(elapsed.Payload["repeat"]!.GetValue<bool>());
        Assert.Equal(1, elapsed.Payload["completedCount"]!.GetValue<int>());

        var state = result.World.Entities.Single().Components.Single(component =>
            component.Type == "Rekall.TimerState");
        Assert.True(state.Properties["completed"]!.GetValue<bool>());
        Assert.Equal(1, state.Properties["completedCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task OneShotTimerDoesNotEmitAgainAfterCompletion()
    {
        var world = CreateWorld(CreateTimerEntity(
            "Spawner",
            durationSeconds: 1.0 / 60.0,
            repeat: false,
            [
                new JsonObject { ["event"] = "timer.elapsed", ["handler"] = "spawnWave" }
            ]));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 2, CancellationToken.None);

        Assert.DoesNotContain(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "timer.elapsed");

        var state = result.World.Entities.Single().Components.Single(component =>
            component.Type == "Rekall.TimerState");
        Assert.True(state.Properties["completed"]!.GetValue<bool>());
        Assert.Equal(1, state.Properties["completedCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task RepeatingTimerEmitsElapsedOnEachInterval()
    {
        var world = CreateWorld(CreateTimerEntity(
            "Spawner",
            durationSeconds: 1.0 / 60.0,
            repeat: true,
            [
                new JsonObject { ["event"] = "timer.elapsed", ["handler"] = "spawnWave" }
            ]));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 2, CancellationToken.None);

        var elapsed = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "timer.elapsed");
        Assert.True(elapsed.Payload["repeat"]!.GetValue<bool>());
        Assert.Equal(2, elapsed.Payload["completedCount"]!.GetValue<int>());

        var state = result.World.Entities.Single().Components.Single(component =>
            component.Type == "Rekall.TimerState");
        Assert.False(state.Properties["completed"]!.GetValue<bool>());
        Assert.Equal(2, state.Properties["completedCount"]!.GetValue<int>());
    }

    private static RekallAgeRuntimeWorld CreateWorld(RekallAgeRuntimeEntity entity)
    {
        return new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [entity],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);
    }

    private static RekallAgeRuntimeEntity CreateTimerEntity(
        string name,
        double durationSeconds,
        bool repeat,
        JsonArray events)
    {
        return new RekallAgeRuntimeEntity(
            "timer",
            name,
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.Timer",
                    new JsonObject
                    {
                        ["timerId"] = "spawn",
                        ["durationSeconds"] = durationSeconds,
                        ["repeat"] = repeat
                    }),
                new RekallAgeRuntimeComponent(
                    "Rekall.EventBindings",
                    new JsonObject { ["events"] = events })
            ]);
    }
}
