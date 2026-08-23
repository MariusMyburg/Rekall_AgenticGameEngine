using Rekall.Age.Player.Web;

namespace Rekall.Age.Tests.Runtime;

public sealed class WebInputBridgeContractTests
{
    [Fact]
    public void NormalizesHeldPressedAndReleasedKeysAcrossFrames()
    {
        var bridge = new RekallAgeWebInputBridge();

        var frame1 = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with
        {
            HeldKeyCodes = ["KeyW", "Space"]
        });
        Assert.Contains("W", frame1.PressedKeys!);
        Assert.Contains("Space", frame1.PressedKeys!);
        Assert.Contains("W", frame1.PressedKeysThisFrame!);
        Assert.Contains("Space", frame1.PressedKeysThisFrame!);
        Assert.Empty(frame1.ReleasedKeysThisFrame!);

        // Same held set the next frame: still held, but no longer "pressed this frame" (one-shot edge consumption).
        var frame2 = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with
        {
            HeldKeyCodes = ["KeyW", "Space"]
        });
        Assert.Contains("W", frame2.PressedKeys!);
        Assert.Empty(frame2.PressedKeysThisFrame!);
        Assert.Empty(frame2.ReleasedKeysThisFrame!);

        var frame3 = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with
        {
            HeldKeyCodes = ["KeyW"]
        });
        Assert.Contains("W", frame3.PressedKeys!);
        Assert.DoesNotContain("Space", frame3.PressedKeys!);
        Assert.Contains("Space", frame3.ReleasedKeysThisFrame!);
        Assert.Empty(frame3.PressedKeysThisFrame!);
    }

    [Theory]
    [InlineData("KeyW", "W")]
    [InlineData("KeyD", "D")]
    [InlineData("ArrowUp", "Up")]
    [InlineData("ArrowDown", "Down")]
    [InlineData("Digit1", "Number1")]
    [InlineData("ShiftLeft", "ShiftLeft")]
    [InlineData("Numpad5", "Keypad5")]
    [InlineData("Backquote", "GraveAccent")]
    public void NormalizesBrowserKeyCodesToTheSameNamesTheWindowsPlayerProduces(string browserCode, string expectedAgeKeyName)
    {
        var bridge = new RekallAgeWebInputBridge();

        var state = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with { HeldKeyCodes = [browserCode] });

        Assert.Contains(expectedAgeKeyName, state.PressedKeys!);
    }

    [Fact]
    public void ExposesPointerCoordinatesDeltasWheelAndViewportSize()
    {
        var bridge = new RekallAgeWebInputBridge();

        var state = bridge.Capture(new RekallAgeWebInputSnapshot(
            HeldKeyCodes: [],
            PointerX: 120,
            PointerY: 64,
            PointerDeltaX: 4,
            PointerDeltaY: -2,
            WheelDeltaY: 15,
            HeldPointerButtons: [0],
            ViewportWidth: 960,
            ViewportHeight: 540));

        Assert.Equal(120, state.MouseX);
        Assert.Equal(64, state.MouseY);
        Assert.Equal(4, state.MouseDeltaX);
        Assert.Equal(-2, state.MouseDeltaY);
        Assert.Equal(15, state.MouseWheelDelta);
        Assert.Equal(960, state.ViewportWidth);
        Assert.Equal(540, state.ViewportHeight);
        Assert.Contains("Left", state.PressedButtons!);
        Assert.Contains("Left", state.PressedButtonsThisFrame!);
    }

    [Fact]
    public void TracksPointerButtonHeldPressedAndReleasedEdges()
    {
        var bridge = new RekallAgeWebInputBridge();

        bridge.Capture(RekallAgeWebInputSnapshot.Empty() with { HeldPointerButtons = [0, 2] });
        var released = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with { HeldPointerButtons = [2] });

        Assert.Contains("Right", released.PressedButtons!);
        Assert.DoesNotContain("Left", released.PressedButtons!);
        Assert.Contains("Left", released.ReleasedButtonsThisFrame!);
        Assert.Empty(released.PressedButtonsThisFrame!);
    }

    [Fact]
    public void ReleasesEveryHeldKeyAndButtonOnFocusLoss()
    {
        var bridge = new RekallAgeWebInputBridge();
        bridge.Capture(RekallAgeWebInputSnapshot.Empty() with
        {
            HeldKeyCodes = ["KeyW"],
            HeldPointerButtons = [0]
        });

        var afterFocusLoss = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with
        {
            HeldKeyCodes = ["KeyW"],
            HeldPointerButtons = [0],
            Focused = false
        });

        Assert.Empty(afterFocusLoss.PressedKeys!);
        Assert.Empty(afterFocusLoss.PressedButtons!);
        Assert.Contains("W", afterFocusLoss.ReleasedKeysThisFrame!);
        Assert.Contains("Left", afterFocusLoss.ReleasedButtonsThisFrame!);
    }

    [Fact]
    public void ExposesGamepadIdentityAxesAndButtonEdges()
    {
        var bridge = new RekallAgeWebInputBridge();

        var gamepad = new RekallAgeWebGamepadSample(0, "Test Pad (Vendor: 0000)", true, [0.5, -0.25], [true, false]);
        var frame1 = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with { Gamepads = [gamepad] });

        Assert.Single(frame1.Controllers!);
        var controller = frame1.Controllers![0];
        Assert.Equal("gamepad", controller.Kind);
        Assert.Equal(0, controller.PlayerIndex);
        Assert.Equal(2, controller.Axes.Count);
        Assert.Equal(0.5, controller.Axes[0].Value);
        Assert.Contains("Button0", controller.PressedButtons);
        Assert.Contains("Button0", controller.PressedButtonsThisFrame);

        var gamepadStillHeld = gamepad with { Axes = [0.5, -0.25] };
        var frame2 = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with { Gamepads = [gamepadStillHeld] });
        Assert.Empty(frame2.Controllers![0].PressedButtonsThisFrame);

        var gamepadReleased = gamepad with { HeldButtons = [false, false] };
        var frame3 = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with { Gamepads = [gamepadReleased] });
        Assert.Contains("Button0", frame3.Controllers![0].ReleasedButtonsThisFrame);
    }

    [Fact]
    public void DropsControllerStateOnceAGamepadDisconnects()
    {
        var bridge = new RekallAgeWebInputBridge();
        var gamepad = new RekallAgeWebGamepadSample(0, "Test Pad", true, [], [true]);
        bridge.Capture(RekallAgeWebInputSnapshot.Empty() with { Gamepads = [gamepad] });

        var afterDisconnect = bridge.Capture(RekallAgeWebInputSnapshot.Empty() with { Gamepads = [] });

        Assert.Empty(afterDisconnect.Controllers!);
    }

    [Theory]
    [InlineData(true, "REKALL_WEB_VIEWPORT_VISIBLE")]
    [InlineData(false, "REKALL_WEB_VIEWPORT_HIDDEN")]
    public void ReportsStableVisibilityDiagnosticCodes(bool visible, string expectedCode)
    {
        var lifecycleEvent = RekallAgeWebInputLifecycle.VisibilityChanged(visible);
        Assert.Equal(expectedCode, lifecycleEvent.Code);
    }

    [Fact]
    public void ReportsStableResizeFullscreenAndDeviceLossDiagnostics()
    {
        Assert.Equal("REKALL_WEB_VIEWPORT_RESIZED", RekallAgeWebInputLifecycle.Resized(1280, 720).Code);
        Assert.Equal("REKALL_WEB_PLAYER_PAUSED", RekallAgeWebInputLifecycle.Paused("tab hidden").Code);
        Assert.Equal("REKALL_WEB_PLAYER_RESUMED", RekallAgeWebInputLifecycle.Resumed().Code);
        Assert.Equal("REKALL_WEB_FULLSCREEN_ENTERED", RekallAgeWebInputLifecycle.FullscreenChanged(true).Code);
        Assert.Equal("REKALL_WEB_FULLSCREEN_EXITED", RekallAgeWebInputLifecycle.FullscreenChanged(false).Code);
        var deviceLost = RekallAgeWebInputLifecycle.DeviceLost("context lost");
        Assert.Equal("REKALL_WEB_GPU_DEVICE_LOST", deviceLost.Code);
        Assert.Equal("context lost", deviceLost.Target);
    }

    [Fact]
    public void ParsesTheExactJsonShapeWebInputJsProduces()
    {
        const string json = """
            {
              "heldKeyCodes": ["KeyW", "Space"],
              "pointerX": 12.5,
              "pointerY": 8,
              "pointerDeltaX": 1.5,
              "pointerDeltaY": -2,
              "wheelDeltaY": 3,
              "heldPointerButtons": [0, 2],
              "viewportWidth": 1280,
              "viewportHeight": 720,
              "focused": true,
              "gamepads": [
                { "index": 0, "id": "Test Pad", "connected": true, "axes": [0.5, -0.25], "heldButtons": [true, false] }
              ]
            }
            """;

        var snapshot = RekallAgeWebInputSnapshotJson.Parse(json);

        Assert.Equal(["KeyW", "Space"], snapshot.HeldKeyCodes);
        Assert.Equal(12.5, snapshot.PointerX);
        Assert.Equal(8, snapshot.PointerY);
        Assert.Equal(1.5, snapshot.PointerDeltaX);
        Assert.Equal(-2, snapshot.PointerDeltaY);
        Assert.Equal(3, snapshot.WheelDeltaY);
        Assert.Equal([0, 2], snapshot.HeldPointerButtons);
        Assert.Equal(1280, snapshot.ViewportWidth);
        Assert.Equal(720, snapshot.ViewportHeight);
        Assert.True(snapshot.Focused);
        Assert.Single(snapshot.Gamepads!);
        Assert.Equal("Test Pad", snapshot.Gamepads![0].Id);
        Assert.Equal([0.5, -0.25], snapshot.Gamepads![0].Axes);
        Assert.Equal([true, false], snapshot.Gamepads![0].HeldButtons);

        // Feeding the parsed snapshot straight into the bridge proves the JSON contract and the C# contract agree.
        var bridge = new RekallAgeWebInputBridge();
        var state = bridge.Capture(snapshot);
        Assert.Contains("W", state.PressedKeys!);
        Assert.Contains("Left", state.PressedButtons!);
        Assert.Contains("Right", state.PressedButtons!);
    }

    [Fact]
    public void TreatsMissingOptionalFieldsAsEmptyInsteadOfThrowing()
    {
        var snapshot = RekallAgeWebInputSnapshotJson.Parse("{}");

        Assert.Empty(snapshot.HeldKeyCodes);
        Assert.Empty(snapshot.HeldPointerButtons);
        Assert.Empty(snapshot.Gamepads!);
        Assert.True(snapshot.Focused);
    }

    [Fact]
    public void RejectsOversizedSnapshotJson()
    {
        var oversized = "{\"heldKeyCodes\":[\"" + new string('x', RekallAgeWebInputSnapshotJson.MaximumJsonBytes) + "\"]}";

        Assert.Throws<FormatException>(() => RekallAgeWebInputSnapshotJson.Parse(oversized));
    }

    [Fact]
    public void FindsAResizeFactAmongOtherQueuedLifecycleEvents()
    {
        var json = """
        [
            {"code":"REKALL_WEB_VIEWPORT_VISIBLE"},
            {"code":"REKALL_WEB_VIEWPORT_RESIZED","width":128,"height":96},
            {"code":"REKALL_WEB_FULLSCREEN_ENTERED"}
        ]
        """;

        var resize = RekallAgeWebPlayerLifecycleEventsJson.TryGetLatestResize(json);

        Assert.Equal(new RekallAgeWebPlayerLifecycleEventsJson.ResizeFact(128, 96), resize);
    }

    [Fact]
    public void ReturnsTheLatestResizeWhenSeveralWereQueuedBetweenPolls()
    {
        var json = """
        [
            {"code":"REKALL_WEB_VIEWPORT_RESIZED","width":100,"height":100},
            {"code":"REKALL_WEB_VIEWPORT_RESIZED","width":200,"height":150}
        ]
        """;

        var resize = RekallAgeWebPlayerLifecycleEventsJson.TryGetLatestResize(json);

        Assert.Equal(new RekallAgeWebPlayerLifecycleEventsJson.ResizeFact(200, 150), resize);
    }

    [Fact]
    public void ReturnsNullWhenNoResizeWasQueued()
    {
        var resize = RekallAgeWebPlayerLifecycleEventsJson.TryGetLatestResize("[{\"code\":\"REKALL_WEB_VIEWPORT_HIDDEN\"}]");

        Assert.Null(resize);
    }

    [Fact]
    public void ReturnsNullForAnEmptyQueue()
    {
        Assert.Null(RekallAgeWebPlayerLifecycleEventsJson.TryGetLatestResize("[]"));
    }
}
