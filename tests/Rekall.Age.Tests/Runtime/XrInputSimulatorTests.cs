using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class XrInputSimulatorTests
{
    [Fact]
    public void SimulatorAddsTrackedHeadAndHandPoses()
    {
        var input = RekallAgeXrInputSimulator.CreateFrame(
            RekallAgeRuntimeInputState.Empty,
            TimeSpan.FromSeconds(1));

        Assert.NotNull(input.XrPoses);
        Assert.Contains(input.XrPoses!, pose => pose.Source == "head" && pose.IsTracked);
        Assert.Contains(input.XrPoses!, pose => pose.Source == "left-hand" && pose.IsTracked);
        Assert.Contains(input.XrPoses!, pose => pose.Source == "right-hand" && pose.IsTracked);
        var head = input.XrPoses!.Single(pose => pose.Source == "head");
        Assert.InRange(head.Y, 1.6, 1.9);
    }

    [Fact]
    public void SimulatorMapsMouseAndKeyboardToGenericXrActions()
    {
        var input = RekallAgeXrInputSimulator.CreateFrame(
            new RekallAgeRuntimeInputState(
                PressedKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Space", "LeftShift" },
                PressedButtons: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Left" }),
            TimeSpan.Zero);

        Assert.NotNull(input.XrActions);
        Assert.Contains(input.XrActions!, action =>
            action.Hand == "right"
            && action.Name == "trigger"
            && action.IsDown
            && action.Value == 1);
        Assert.Contains(input.XrActions!, action =>
            action.Hand == "left"
            && action.Name == "grip"
            && action.IsDown
            && action.Value == 1);
    }

    [Fact]
    public void SimulatorPreservesExistingNonSimulatedXrInput()
    {
        var input = RekallAgeXrInputSimulator.CreateFrame(
            new RekallAgeRuntimeInputState(
                XrPoses:
                [
                    new RekallAgeRuntimeXrPose("waist", true, Y: 1)
                ],
                XrActions:
                [
                    new RekallAgeRuntimeXrAction("left", "thumbstick-x", 0.25, true, false, false)
                ]),
            TimeSpan.Zero);

        Assert.Contains(input.XrPoses!, pose => pose.Source == "waist");
        Assert.Contains(input.XrActions!, action => action.Name == "thumbstick-x");
    }
}
