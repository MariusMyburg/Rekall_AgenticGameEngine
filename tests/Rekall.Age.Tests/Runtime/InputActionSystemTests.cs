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
    public async Task HeldDesktopKeyRemainsDownAcrossCatchUpStepWithoutRepeatingPressEdge()
    {
        var world = CreateWorld(new JsonArray
        {
            new JsonObject
            {
                ["name"] = "move.horizontal",
                ["positiveKey"] = "D"
            }
        });
        var capturedInput = new RekallAgeRuntimeInputState(
            MouseDeltaX: 12,
            MouseWheelDelta: 1,
            PressedKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "D" },
            PressedKeysThisFrame: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "D" });

        var first = RekallAgeRuntimeInputPersistence.ForSimulationStep(capturedInput, 0);
        var catchUp = RekallAgeRuntimeInputPersistence.ForSimulationStep(capturedInput, 1);
        var firstResult = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None, first);
        var catchUpResult = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(firstResult.World, 1, CancellationToken.None, catchUp);

        var firstAction = Assert.Single(firstResult.World.Subsystems.Input.Actions);
        Assert.True(firstAction.IsDown);
        Assert.True(firstAction.WasPressed);
        var catchUpAction = Assert.Single(catchUpResult.World.Subsystems.Input.Actions);
        Assert.True(catchUpAction.IsDown);
        Assert.False(catchUpAction.WasPressed);
        Assert.Contains("D", catchUp.PressedKeys!);
        Assert.Null(catchUp.PressedKeysThisFrame);
        Assert.Equal(0, catchUp.MouseDeltaX);
        Assert.Equal(0, catchUp.MouseWheelDelta);
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

    [Fact]
    public async Task InputActionMapProjectsMouseDeltaAxes()
    {
        var world = CreateWorld(new JsonArray
        {
            new JsonObject
            {
                ["name"] = "lookX",
                ["mouseAxis"] = "x",
                ["mouseScale"] = 0.25
            },
            new JsonObject
            {
                ["name"] = "lookY",
                ["mouseAxis"] = "y",
                ["mouseScale"] = -0.5
            }
        });

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(MouseDeltaX: 8, MouseDeltaY: -4));

        var lookX = Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "lookX");
        Assert.True(lookX.IsDown);
        Assert.True(lookX.WasPressed);
        Assert.Equal(2, lookX.Value);
        var lookY = Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "lookY");
        Assert.True(lookY.IsDown);
        Assert.True(lookY.WasPressed);
        Assert.Equal(2, lookY.Value);
    }

    [Fact]
    public async Task NonRenderedInputConfigurationStillProjectsActions()
    {
        var world = CreateWorld(
            new JsonArray(new JsonObject
            {
                ["name"] = "move.horizontal",
                ["positiveKey"] = "D"
            }),
            visible: false,
            active: true);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(
                    SemanticActions: [new("move.horizontal", Value: 1, IsDown: true)]));

        var action = Assert.Single(result.World.Subsystems.Input.Actions);
        Assert.Equal("move.horizontal", action.Name);
        Assert.Equal(1, action.Value);
        Assert.True(action.IsDown);
        Assert.Equal("Gameplay Input", action.SourceEntityName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitlyInactiveInputMapDoesNotProjectActions(bool visible)
    {
        var world = CreateWorld(
            new JsonArray(new JsonObject { ["name"] = "move.horizontal", ["positiveKey"] = "D" }),
            visible,
            active: false);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(
                    PressedKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "D" }));

        Assert.Empty(result.World.Subsystems.Input.Actions);
    }

    private static RekallAgeRuntimeWorld CreateWorld(
        JsonArray actions,
        bool visible = true,
        bool active = true)
    {
        var input = new RekallAgeRuntimeEntity(
            "input",
            "Gameplay Input",
            ["input"],
            null,
            null,
            visible,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.InputActionMap",
                    new JsonObject
                    {
                        ["active"] = active,
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
