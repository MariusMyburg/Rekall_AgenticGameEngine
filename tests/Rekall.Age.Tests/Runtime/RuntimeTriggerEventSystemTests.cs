using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeTriggerEventSystemTests
{
    [Fact]
    public async Task TriggerSystemEmitsEnterForNewOccupants()
    {
        var world = CreateWorld(
            CreateTrigger(
                "zone",
                "Zone",
                x: 0,
                [
                    new JsonObject { ["event"] = "trigger.enter", ["handler"] = "enteredZone" }
                ]),
            CreateActor("actor", "Actor", x: 0.5));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var enter = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "trigger.enter");
        Assert.Equal("Zone", enter.EntityName);
        Assert.Equal("runtime.trigger", enter.Source);
        Assert.Equal("enteredZone", enter.Handler);
        Assert.Equal("actor", enter.Payload["otherEntityId"]!.GetValue<string>());
        Assert.Equal("Actor", enter.Payload["otherEntityName"]!.GetValue<string>());

        var state = result.World.Entities.Single(entity => entity.Id == "zone")
            .Components.Single(component => component.Type == "Rekall.TriggerState");
        Assert.Contains(state.Properties["occupants"]!.AsArray(), item =>
            item!.GetValue<string>() == "actor");
    }

    [Fact]
    public async Task TriggerSystemEmitsStayForPersistingOccupants()
    {
        var world = CreateWorld(
            CreateTrigger(
                "zone",
                "Zone",
                x: 0,
                [
                    new JsonObject { ["event"] = "trigger.enter", ["handler"] = "enteredZone" },
                    new JsonObject { ["event"] = "trigger.stay", ["handler"] = "insideZone" }
                ]),
            CreateActor("actor", "Actor", x: 0.5));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 2, CancellationToken.None);

        Assert.DoesNotContain(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "trigger.enter");
        var stay = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "trigger.stay");
        Assert.Equal("insideZone", stay.Handler);
        Assert.Equal("actor", stay.Payload["otherEntityId"]!.GetValue<string>());
    }

    [Fact]
    public async Task TriggerSystemEmitsExitForSeparatedPreviousOccupants()
    {
        var world = CreateWorld(
            CreateTrigger(
                "zone",
                "Zone",
                x: 0,
                [
                    new JsonObject { ["event"] = "trigger.exit", ["handler"] = "exitedZone" }
                ],
                previousOccupants: ["actor"]),
            CreateActor("actor", "Actor", x: 4));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var exit = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "trigger.exit");
        Assert.Equal("exitedZone", exit.Handler);
        Assert.Equal("actor", exit.Payload["otherEntityId"]!.GetValue<string>());

        var state = result.World.Entities.Single(entity => entity.Id == "zone")
            .Components.Single(component => component.Type == "Rekall.TriggerState");
        Assert.Empty(state.Properties["occupants"]!.AsArray());
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

    private static RekallAgeRuntimeEntity CreateTrigger(
        string id,
        string name,
        double x,
        JsonArray events,
        IReadOnlyList<string>? previousOccupants = null)
    {
        var components = new List<RekallAgeRuntimeComponent>
        {
            new(
                "Rekall.Trigger",
                new JsonObject
                {
                    ["shape"] = "sphere",
                    ["radius"] = 1
                }),
            new(
                "Rekall.EventBindings",
                new JsonObject { ["events"] = events })
        };

        if (previousOccupants is not null)
        {
            var occupants = new JsonArray();
            foreach (var occupant in previousOccupants)
            {
                occupants.Add(occupant);
            }

            components.Add(new RekallAgeRuntimeComponent(
                "Rekall.TriggerState",
                new JsonObject { ["occupants"] = occupants }));
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

    private static RekallAgeRuntimeEntity CreateActor(
        string id,
        string name,
        double x)
    {
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
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["radius"] = 0.5 })
            ]);
    }
}
