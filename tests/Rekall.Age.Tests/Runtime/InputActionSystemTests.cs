using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class InputActionSystemTests
{
    [Fact]
    public async Task InputActionMapProjectsButtonActionsFromKeyboardState()
    {
        var world = CreateWorld(new JsonArray
        {
            new JsonObject
            {
                ["name"] = "thrust",
                ["key"] = "W"
            },
            new JsonObject
            {
                ["name"] = "fire",
                ["key"] = "Space"
            }
        });

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(
                    PressedKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "W", "Space" },
                    PressedKeysThisFrame: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Space" }));

        var thrust = Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "thrust");
        Assert.True(thrust.IsDown);
        Assert.False(thrust.WasPressed);
        Assert.Equal(1, thrust.Value);
        var fire = Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "fire");
        Assert.True(fire.IsDown);
        Assert.True(fire.WasPressed);
    }

    [Fact]
    public async Task InputActionMapProjectsAxisAndWheelActions()
    {
        var world = CreateWorld(new JsonArray
        {
            new JsonObject
            {
                ["name"] = "strafe",
                ["positiveKey"] = "D",
                ["negativeKey"] = "A"
            },
            new JsonObject
            {
                ["name"] = "zoom",
                ["mouseWheelScale"] = 0.5
            }
        });

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(
                    MouseWheelDelta: -2,
                    PressedKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" }));

        var strafe = Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "strafe");
        Assert.True(strafe.IsDown);
        Assert.Equal(-1, strafe.Value);
        var zoom = Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "zoom");
        Assert.True(zoom.IsDown);
        Assert.Equal(-1, zoom.Value);
    }

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
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.InputActionMap",
                    new JsonObject
                    {
                        ["active"] = true,
                        ["actions"] = actions
                    })
            ]);
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
