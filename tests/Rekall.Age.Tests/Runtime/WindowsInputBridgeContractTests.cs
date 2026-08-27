using System.Text.Json.Nodes;
using Rekall.Age.Player.Windows;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class WindowsInputBridgeContractTests
{
    [Fact]
    public void TracksKeyHeldPressedAndReleasedEdgesAcrossFrames()
    {
        var bridge = new RekallAgeWindowsInputBridge();

        bridge.RecordKey("Up", down: true);
        var frame1 = bridge.ConsumeRuntimeInput(0, 0, 1280, 720, null);
        Assert.Contains("Up", frame1.PressedKeys!);
        Assert.Contains("Up", frame1.PressedKeysThisFrame!);
        Assert.Empty(frame1.ReleasedKeysThisFrame ?? new HashSet<string>());

        // Still held, no new key events recorded: the runtime input reports it held but the
        // one-shot "this frame" edge is gone.
        var frame2 = bridge.ConsumeRuntimeInput(0, 0, 1280, 720, null);
        Assert.Contains("Up", frame2.PressedKeys!);
        Assert.Empty(frame2.PressedKeysThisFrame ?? new HashSet<string>());

        bridge.RecordKey("Up", down: false);
        var frame3 = bridge.ConsumeRuntimeInput(0, 0, 1280, 720, null);
        Assert.DoesNotContain("Up", frame3.PressedKeys ?? new HashSet<string>());
        Assert.Contains("Up", frame3.ReleasedKeysThisFrame!);
    }

    [Fact]
    public void RecordKeyReturnsTrueOnlyOnAnActualEdge()
    {
        var bridge = new RekallAgeWindowsInputBridge();

        Assert.True(bridge.RecordKey("Up", down: true));
        Assert.False(bridge.RecordKey("Up", down: true));
        Assert.True(bridge.RecordKey("Up", down: false));
        Assert.False(bridge.RecordKey("Up", down: false));
    }

    [Fact]
    public void TracksMouseButtonHeldPressedAndReleasedEdges()
    {
        var bridge = new RekallAgeWindowsInputBridge();

        bridge.RecordMouseButton("Left", down: true);
        bridge.RecordMouseButton("Right", down: true);
        var pressed = bridge.ConsumeRuntimeInput(0, 0, 1280, 720, null);
        Assert.Contains("Left", pressed.PressedButtons!);
        Assert.Contains("Right", pressed.PressedButtons!);

        bridge.RecordMouseButton("Left", down: false);
        var released = bridge.ConsumeRuntimeInput(0, 0, 1280, 720, null);
        Assert.DoesNotContain("Left", released.PressedButtons ?? new HashSet<string>());
        Assert.Contains("Right", released.PressedButtons!);
        Assert.Contains("Left", released.ReleasedButtonsThisFrame!);
    }

    [Fact]
    public void ExposesMouseDeltaWheelAndViewportSizeThenClearsPerFrameDeltas()
    {
        var bridge = new RekallAgeWindowsInputBridge();
        bridge.RecordMouseDelta(4, -2);
        bridge.RecordMouseWheel(15);

        var frame1 = bridge.ConsumeRuntimeInput(120, 64, 960, 540, null);
        Assert.Equal(120, frame1.MouseX);
        Assert.Equal(64, frame1.MouseY);
        Assert.Equal(4, frame1.MouseDeltaX);
        Assert.Equal(-2, frame1.MouseDeltaY);
        Assert.Equal(15, frame1.MouseWheelDelta);
        Assert.Equal(960, frame1.ViewportWidth);
        Assert.Equal(540, frame1.ViewportHeight);

        // Nothing new recorded: idle fast path reports zeroed deltas, not the stale prior values.
        var frame2 = bridge.ConsumeRuntimeInput(120, 64, 960, 540, null);
        Assert.Equal(0, frame2.MouseDeltaX);
        Assert.Equal(0, frame2.MouseWheelDelta);
    }

    [Fact]
    public void PassesControllersThroughUnchanged()
    {
        var bridge = new RekallAgeWindowsInputBridge();
        var controllers = new[]
        {
            new RekallAgeRuntimeControllerState(
                "gamepad-0", "gamepad", 0, [], [], [], [], [])
        };

        var state = bridge.ConsumeRuntimeInput(0, 0, 1280, 720, controllers);

        Assert.Same(controllers, state.Controllers);
    }

    [Fact]
    public void IsPressedAndIsPressedThisFrameReflectLiveBridgeStateBeforeConsuming()
    {
        var bridge = new RekallAgeWindowsInputBridge();

        Assert.False(bridge.IsPressed("Up"));
        bridge.RecordKey("Up", down: true);
        Assert.True(bridge.IsPressed("Up"));
        Assert.True(bridge.IsPressedThisFrame("Up"));

        bridge.ConsumeRuntimeInput(0, 0, 1280, 720, null);
        Assert.True(bridge.IsPressed("Up"));
        Assert.False(bridge.IsPressedThisFrame("Up"));
    }

    /// <summary>
    /// End-to-end reproduction of the exact real bug this bridge was extracted to catch: a player
    /// holding the physical Up arrow, authored as an ordinary `Rekall.InputActionMap` `positiveKey`,
    /// must actually reach a declared gameplay action -- the one thing every other test this engine had
    /// before this file skipped, since they all inject `semanticActions` directly and never exercise the
    /// physical-key capture path a real Windows player uses.
    /// </summary>
    [Fact]
    public async Task HeldPhysicalUpArrowReachesADeclaredThrottleActionThroughTheRealCapturePath()
    {
        var bridge = new RekallAgeWindowsInputBridge();
        bridge.RecordKey("Up", down: true);
        var capturedInput = bridge.ConsumeRuntimeInput(0, 0, 1280, 720, null);

        var world = CreateWorld(new JsonArray
        {
            new JsonObject
            {
                ["name"] = "throttle",
                ["positiveKey"] = "Up",
                ["negativeKey"] = "Down"
            }
        });

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None, capturedInput);

        var throttle = Assert.Single(result.World.Subsystems.Input.Actions, action => action.Name == "throttle");
        Assert.True(throttle.IsDown);
        Assert.Equal(1, throttle.Value);
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
