using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimePointerEventSystemTests
{
    [Fact]
    public async Task PointerRayEmitsHitEnterAndDownEventsForBoundTarget()
    {
        var world = CreateWorld(
            CreatePointer("pointer", "Pointer", DirectionZ: 1),
            CreateTarget(
                "target",
                "Target",
                z: 5,
                [
                    new JsonObject { ["event"] = "pointer.hit", ["handler"] = "inspect" },
                    new JsonObject { ["event"] = "pointer.enter", ["handler"] = "highlight" },
                    new JsonObject { ["event"] = "pointer.down", ["handler"] = "press" }
                ]));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(
                    PressedButtons: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Left" },
                    PressedButtonsThisFrame: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Left" }));

        var events = result.World.Subsystems.Events.Events;
        var hit = Assert.Single(events, runtimeEvent => runtimeEvent.Type == "pointer.hit");
        var enter = Assert.Single(events, runtimeEvent => runtimeEvent.Type == "pointer.enter");
        var down = Assert.Single(events, runtimeEvent => runtimeEvent.Type == "pointer.down");
        Assert.Equal("Target", hit.EntityName);
        Assert.Equal("runtime.pointer", hit.Source);
        Assert.Equal("inspect", hit.Handler);
        Assert.Equal("highlight", enter.Handler);
        Assert.Equal("press", down.Handler);
        Assert.Equal("Pointer", hit.Payload["sourceEntityName"]!.GetValue<string>());
        Assert.Equal("primary", hit.Payload["pointerId"]!.GetValue<string>());
        Assert.Equal("Left", down.Payload["button"]!.GetValue<string>());
        Assert.Equal(4.5, hit.Payload["distance"]!.GetValue<double>(), precision: 6);

        var source = result.World.Entities.Single(entity => entity.Id == "pointer");
        var state = source.Components.Single(component => component.Type == "Rekall.PointerState");
        Assert.Equal("target", state.Properties["hoveredEntityId"]!.GetValue<string>());
        Assert.Equal("target", state.Properties["pressedEntityId"]!.GetValue<string>());
    }

    [Fact]
    public async Task PointerRayEmitsUpAndClickWhenReleasedOverPressedTarget()
    {
        var world = CreateWorld(
            CreatePointer(
                "pointer",
                "Pointer",
                DirectionZ: 1,
                hoveredEntityId: "target",
                pressedEntityId: "target"),
            CreateTarget(
                "target",
                "Target",
                z: 5,
                [
                    new JsonObject { ["event"] = "pointer.up", ["handler"] = "release" },
                    new JsonObject { ["event"] = "pointer.click", ["handler"] = "activate" }
                ]));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(
                    ReleasedButtonsThisFrame: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Left" }));

        var up = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "pointer.up");
        var click = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "pointer.click");
        Assert.Equal("release", up.Handler);
        Assert.Equal("activate", click.Handler);
        Assert.Equal("target", click.EntityId);

        var source = result.World.Entities.Single(entity => entity.Id == "pointer");
        var state = source.Components.Single(component => component.Type == "Rekall.PointerState");
        Assert.Equal("target", state.Properties["hoveredEntityId"]!.GetValue<string>());
        Assert.False(state.Properties.ContainsKey("pressedEntityId"));
    }

    [Fact]
    public async Task PointerRayEmitsLeaveWhenPreviousTargetIsNoLongerHit()
    {
        var world = CreateWorld(
            CreatePointer("pointer", "Pointer", DirectionX: 1, DirectionZ: 0, hoveredEntityId: "target"),
            CreateTarget(
                "target",
                "Target",
                z: 5,
                [
                    new JsonObject { ["event"] = "pointer.leave", ["handler"] = "unhighlight" }
                ]));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var leave = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "pointer.leave");
        Assert.Equal("Target", leave.EntityName);
        Assert.Equal("unhighlight", leave.Handler);
        Assert.Equal("pointer", leave.Payload["sourceEntityId"]!.GetValue<string>());

        var source = result.World.Entities.Single(entity => entity.Id == "pointer");
        var state = source.Components.Single(component => component.Type == "Rekall.PointerState");
        Assert.False(state.Properties.ContainsKey("hoveredEntityId"));
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

    private static RekallAgeRuntimeEntity CreatePointer(
        string id,
        string name,
        double DirectionX = 0,
        double DirectionZ = 1,
        string? hoveredEntityId = null,
        string? pressedEntityId = null)
    {
        var components = new List<RekallAgeRuntimeComponent>
        {
            new(
                "Rekall.PointerRay",
                new JsonObject
                {
                    ["pointerId"] = "primary",
                    ["originX"] = 0,
                    ["originY"] = 0,
                    ["originZ"] = 0,
                    ["directionX"] = DirectionX,
                    ["directionY"] = 0,
                    ["directionZ"] = DirectionZ,
                    ["range"] = 10,
                    ["button"] = "Left"
                })
        };
        var state = new JsonObject();
        if (!string.IsNullOrWhiteSpace(hoveredEntityId))
        {
            state["hoveredEntityId"] = hoveredEntityId;
        }

        if (!string.IsNullOrWhiteSpace(pressedEntityId))
        {
            state["pressedEntityId"] = pressedEntityId;
        }

        if (state.Count > 0)
        {
            components.Add(new RekallAgeRuntimeComponent("Rekall.PointerState", state));
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

    private static RekallAgeRuntimeEntity CreateTarget(
        string id,
        string name,
        double z,
        JsonArray events)
    {
        return new RekallAgeRuntimeEntity(
            id,
            name,
            ["target"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(0, 0, z)
            },
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["radius"] = 0.5 }),
                new RekallAgeRuntimeComponent(
                    "Rekall.EventBindings",
                    new JsonObject { ["events"] = events })
            ]);
    }
}
