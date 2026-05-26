using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public static class RekallAgeXrInputSimulator
{
    public static RekallAgeRuntimeInputState CreateFrame(
        RekallAgeRuntimeInputState input,
        TimeSpan elapsedTime)
    {
        var seconds = elapsedTime.TotalSeconds;
        var yaw = Math.Sin(seconds * 0.7) * 8.0;
        var head = new RekallAgeRuntimeXrPose(
            "head",
            IsTracked: true,
            X: Math.Sin(seconds * 0.35) * 0.04,
            Y: 1.72 + Math.Sin(seconds * 1.1) * 0.015,
            Z: 0,
            Pitch: Math.Sin(seconds * 0.5) * 3.0,
            Yaw: yaw,
            Roll: Math.Sin(seconds * 0.4) * 1.0);
        var leftHand = new RekallAgeRuntimeXrPose(
            "left-hand",
            IsTracked: true,
            X: -0.28,
            Y: 1.28 + Math.Sin(seconds * 1.4) * 0.025,
            Z: -0.38,
            Pitch: -18,
            Yaw: yaw - 12,
            Roll: -8);
        var rightHand = new RekallAgeRuntimeXrPose(
            "right-hand",
            IsTracked: true,
            X: 0.28,
            Y: 1.28 + Math.Cos(seconds * 1.4) * 0.025,
            Z: -0.38,
            Pitch: -18,
            Yaw: yaw + 12,
            Roll: 8);

        var rightTrigger = IsPressed(input.PressedButtons, "Left") || IsPressed(input.PressedKeys, "Space");
        var leftGrip = IsPressed(input.PressedButtons, "Right") || IsPressed(input.PressedKeys, "LeftShift");
        var actions = new[]
        {
            new RekallAgeRuntimeXrAction("right", "trigger", rightTrigger ? 1 : 0, rightTrigger, rightTrigger, false),
            new RekallAgeRuntimeXrAction("left", "grip", leftGrip ? 1 : 0, leftGrip, leftGrip, false)
        };

        return input with
        {
            XrPoses = MergePoses(input.XrPoses, [head, leftHand, rightHand]),
            XrActions = MergeActions(input.XrActions, actions)
        };
    }

    private static IReadOnlyList<RekallAgeRuntimeXrPose> MergePoses(
        IReadOnlyList<RekallAgeRuntimeXrPose>? authored,
        IReadOnlyList<RekallAgeRuntimeXrPose> simulated)
    {
        return (authored ?? Array.Empty<RekallAgeRuntimeXrPose>())
            .Concat(simulated)
            .GroupBy(pose => pose.Source, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(pose => pose.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<RekallAgeRuntimeXrAction> MergeActions(
        IReadOnlyList<RekallAgeRuntimeXrAction>? authored,
        IReadOnlyList<RekallAgeRuntimeXrAction> simulated)
    {
        return (authored ?? Array.Empty<RekallAgeRuntimeXrAction>())
            .Concat(simulated)
            .GroupBy(action => $"{action.Hand}/{action.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(action => action.Hand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsPressed(IReadOnlySet<string>? values, string value)
    {
        return values is not null && values.Contains(value);
    }
}
