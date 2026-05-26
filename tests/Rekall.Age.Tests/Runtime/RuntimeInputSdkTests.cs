using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeInputSdkTests
{
    [Fact]
    public void RuntimeModuleSdkAggregatesSemanticInputActionsByName()
    {
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [],
            RekallAgeRuntimeSubsystemViews.Empty with
            {
                Input = new RekallAgeRuntimeInputView(
                [
                    new RekallAgeRuntimeInputAction("moveX", 1, true, false, false, "input-a", "Keyboard"),
                    new RekallAgeRuntimeInputAction("moveX", -0.25, true, true, false, "input-b", "Gamepad"),
                    new RekallAgeRuntimeInputAction("fire", 1, true, true, false, "input-a", "Keyboard")
                ])
            },
            []);

        Assert.Equal(0.75, world.InputActionValue("moveX"), precision: 6);
        Assert.True(world.IsInputActionDown("moveX"));
        Assert.True(world.WasInputActionPressed("moveX"));
        Assert.False(world.WasInputActionReleased("moveX"));
        Assert.Equal(42, world.InputActionValue("missing", 42), precision: 6);
        Assert.True(world.IsInputActionDown("fire"));
    }
}
