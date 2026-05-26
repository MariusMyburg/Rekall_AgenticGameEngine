using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeCollisionEventSystemTests
{
    [Fact]
    public async Task CollisionSystemEmitsBeginForNewOverlaps()
    {
        var world = CreateWorld(
            CreateSphere(
                "actor-a",
                "Actor A",
                x: 0,
                [
                    new JsonObject { ["event"] = "collision.begin", ["handler"] = "touchStarted" }
                ]),
            CreateSphere("actor-b", "Actor B", x: 0.75, []));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var collision = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "collision.begin");
        Assert.Equal("Actor A", collision.EntityName);
        Assert.Equal("runtime.collision", collision.Source);
        Assert.Equal("touchStarted", collision.Handler);
        Assert.Equal("actor-b", collision.Payload["otherEntityId"]!.GetValue<string>());
        Assert.Equal("Actor B", collision.Payload["otherEntityName"]!.GetValue<string>());

        var state = result.World.Entities.Single(entity => entity.Id == "actor-a")
            .Components.Single(component => component.Type == "Rekall.CollisionState");
        Assert.Contains(state.Properties["overlaps"]!.AsArray(), item =>
            item!.GetValue<string>() == "actor-b");
    }

    [Fact]
    public async Task CollisionSystemEmitsStayForPersistingOverlaps()
    {
        var world = CreateWorld(
            CreateSphere(
                "actor-a",
                "Actor A",
                x: 0,
                [
                    new JsonObject { ["event"] = "collision.begin", ["handler"] = "touchStarted" },
                    new JsonObject { ["event"] = "collision.stay", ["handler"] = "touchContinues" }
                ]),
            CreateSphere("actor-b", "Actor B", x: 0.75, []));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 2, CancellationToken.None);

        Assert.DoesNotContain(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "collision.begin");
        var stay = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "collision.stay");
        Assert.Equal("touchContinues", stay.Handler);
        Assert.Equal("actor-b", stay.Payload["otherEntityId"]!.GetValue<string>());
    }

    [Fact]
    public async Task CollisionSystemEmitsEndForSeparatedPreviousOverlaps()
    {
        var world = CreateWorld(
            CreateSphere(
                "actor-a",
                "Actor A",
                x: 0,
                [
                    new JsonObject { ["event"] = "collision.end", ["handler"] = "touchEnded" }
                ],
                previousOverlaps: ["actor-b"]),
            CreateSphere("actor-b", "Actor B", x: 4, []));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var ended = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "collision.end");
        Assert.Equal("touchEnded", ended.Handler);
        Assert.Equal("actor-b", ended.Payload["otherEntityId"]!.GetValue<string>());

        var state = result.World.Entities.Single(entity => entity.Id == "actor-a")
            .Components.Single(component => component.Type == "Rekall.CollisionState");
        Assert.Empty(state.Properties["overlaps"]!.AsArray());
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

    private static RekallAgeRuntimeEntity CreateSphere(
        string id,
        string name,
        double x,
        JsonArray events,
        IReadOnlyList<string>? previousOverlaps = null)
    {
        var components = new List<RekallAgeRuntimeComponent>
        {
            new(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.5 }),
            new(
                "Rekall.EventBindings",
                new JsonObject { ["events"] = events })
        };

        if (previousOverlaps is not null)
        {
            var overlaps = new JsonArray();
            foreach (var overlap in previousOverlaps)
            {
                overlaps.Add(overlap);
            }

            components.Add(new RekallAgeRuntimeComponent(
                "Rekall.CollisionState",
                new JsonObject { ["overlaps"] = overlaps }));
        }

        return new RekallAgeRuntimeEntity(
            id,
            name,
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(x, 0, 0)
            },
            components);
    }
}
