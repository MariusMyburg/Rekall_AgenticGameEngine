using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class ControllerInputActionSystemTests
{
    [Fact]
    public async Task ControllerAxisAppliesRescaledDeadzoneSaturationAndInversion()
    {
        var world = CreateWorld(new JsonArray
        {
            new JsonObject
            {
                ["name"] = "move.horizontal",
                ["controllerAxis"] = "LeftX",
                ["deadzone"] = 0.2,
                ["saturation"] = 1.0
            },
            new JsonObject
            {
                ["name"] = "look.vertical",
                ["gamepadAxis"] = "RightY",
                ["deadzone"] = 0.1,
                ["controllerAxisScale"] = 0.5,
                ["invert"] = true
            }
        });
        var controller = Controller(
            "pad-alpha",
            "gamepad",
            0,
            axes: [new("LeftX", 0.6), new("RightY", -0.55)]);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            world,
            1,
            CancellationToken.None,
            new RekallAgeRuntimeInputState(Controllers: [controller]));

        var move = Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "move.horizontal");
        Assert.Equal(0.5, move.Value, precision: 6);
        Assert.True(move.IsDown);
        Assert.Equal("pad-alpha", move.PhysicalDeviceId);
        Assert.Equal("gamepad", move.PhysicalDeviceKind);
        Assert.Equal("pad-alpha", Assert.Single(result.World.Subsystems.Input.Controllers).DeviceId);
        var look = Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "look.vertical");
        Assert.Equal(0.25, look.Value, precision: 6);
    }

    [Fact]
    public void ControllerTrackerProducesStableButtonEdgesAndDisconnectRelease()
    {
        var tracker = new RekallAgeControllerInputTracker();

        var first = Assert.Single(tracker.Update(
        [
            new RekallAgeRuntimeControllerState(
                "pad-alpha", "gamepad", 0, [], ["A"], [], [], [])
        ]));
        Assert.Equal(["A"], first.PressedButtonsThisFrame);
        Assert.Empty(first.ReleasedButtonsThisFrame);

        var held = Assert.Single(tracker.Update(
        [
            new RekallAgeRuntimeControllerState(
                "pad-alpha", "gamepad", 0, [], ["A"], [], [], [])
        ]));
        Assert.Empty(held.PressedButtonsThisFrame);
        Assert.Empty(held.ReleasedButtonsThisFrame);

        var released = Assert.Single(tracker.Update(
        [
            new RekallAgeRuntimeControllerState(
                "pad-alpha", "gamepad", 0, [], [], [], [], [])
        ]));
        Assert.Equal(["A"], released.ReleasedButtonsThisFrame);

        Assert.Empty(tracker.Update([]));
        Assert.Empty(tracker.ConnectedDeviceIds);
    }

    [Fact]
    public async Task ControllerButtonFiltersPlayerAndPreservesPressedReleasedEdges()
    {
        var world = CreateWorld(new JsonArray
        {
            new JsonObject
            {
                ["name"] = "player2.fire",
                ["controllerButton"] = "A",
                ["deviceKind"] = "gamepad",
                ["playerIndex"] = 1
            }
        });
        var playerOne = Controller("pad-one", "gamepad", 0, held: ["A"], pressed: ["A"]);
        var playerTwo = Controller("pad-two", "gamepad", 1, held: ["A"], released: ["A"]);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            world,
            1,
            CancellationToken.None,
            new RekallAgeRuntimeInputState(Controllers: [playerOne, playerTwo]));

        var fire = Assert.Single(result.World.Subsystems.Input.Actions);
        Assert.Equal(1, fire.Value);
        Assert.True(fire.IsDown);
        Assert.False(fire.WasPressed);
        Assert.True(fire.WasReleased);
        Assert.Equal("pad-two", fire.PhysicalDeviceId);
    }

    [Fact]
    public async Task RawJoystickAxisButtonAndHatAliasesProjectGenericActions()
    {
        var world = CreateWorld(new JsonArray
        {
            new JsonObject { ["name"] = "throttle", ["joystickAxis"] = "Axis2" },
            new JsonObject { ["name"] = "utility", ["joystickButton"] = "Button7" },
            new JsonObject
            {
                ["name"] = "menu.left",
                ["controllerHat"] = "Hat0",
                ["controllerHatDirection"] = "left"
            }
        });
        var joystick = Controller(
            "stick-alpha",
            "joystick",
            0,
            axes: [new("Axis2", -0.75)],
            held: ["Button7"],
            pressed: ["Button7"],
            hats: [new("Hat0", -1, 0)]);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            world,
            1,
            CancellationToken.None,
            new RekallAgeRuntimeInputState(Controllers: [joystick]));

        Assert.Equal(-0.75, Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "throttle").Value);
        Assert.True(Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "utility").WasPressed);
        Assert.Equal(1, Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "menu.left").Value);
    }

    [Fact]
    public async Task ControllerDeviceFilterExcludesOtherMatchingDevices()
    {
        var world = CreateWorld(new JsonArray
        {
            new JsonObject
            {
                ["name"] = "steer",
                ["controllerAxis"] = "LeftX",
                ["deviceId"] = "pad-target"
            }
        });

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            world,
            1,
            CancellationToken.None,
            new RekallAgeRuntimeInputState(Controllers:
            [
                Controller("pad-other", "gamepad", 0, axes: [new("LeftX", -1)]),
                Controller("pad-target", "gamepad", 1, axes: [new("LeftX", 0.4)])
            ]));

        var steer = Assert.Single(result.World.Subsystems.Input.Actions);
        Assert.Equal(0.4, steer.Value, precision: 6);
        Assert.Equal("pad-target", steer.PhysicalDeviceId);
    }

    private static RekallAgeRuntimeControllerState Controller(
        string id,
        string kind,
        int player,
        IReadOnlyList<RekallAgeRuntimeControllerAxis>? axes = null,
        IReadOnlyList<string>? held = null,
        IReadOnlyList<string>? pressed = null,
        IReadOnlyList<string>? released = null,
        IReadOnlyList<RekallAgeRuntimeControllerHat>? hats = null) =>
        new(
            id,
            kind,
            player,
            axes ?? [],
            held ?? [],
            pressed ?? [],
            released ?? [],
            hats ?? []);

    private static RekallAgeRuntimeWorld CreateWorld(JsonArray actions)
    {
        var input = new RekallAgeRuntimeEntity(
            "input",
            "Gameplay Input",
            ["input"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [new RekallAgeRuntimeComponent("Rekall.InputActionMap", new JsonObject { ["actions"] = actions })]);
        return new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [input],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);
    }
}
