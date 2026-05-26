using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public static class RekallAgeMultiplayerSnapshotInterpolator
{
    public static IReadOnlyList<RekallAgeMultiplayerEntityState> Sample(
        RekallAgeMultiplayerSnapshot previous,
        RekallAgeMultiplayerSnapshot next,
        double renderTimeSeconds)
    {
        var duration = Math.Max(0.000001, next.ServerTimeSeconds - previous.ServerTimeSeconds);
        var alpha = Math.Clamp((renderTimeSeconds - previous.ServerTimeSeconds) / duration, 0, 1);
        var nextByNetworkId = next.Entities.ToDictionary(entity => entity.NetworkId, StringComparer.Ordinal);
        return previous.Entities
            .Select(previousEntity =>
            {
                if (!nextByNetworkId.TryGetValue(previousEntity.NetworkId, out var nextEntity))
                {
                    return previousEntity;
                }

                return previousEntity with
                {
                    Position = Lerp(previousEntity.Position, nextEntity.Position, alpha),
                    Rotation = Lerp(previousEntity.Rotation, nextEntity.Rotation, alpha),
                    Scale = Lerp(previousEntity.Scale, nextEntity.Scale, alpha)
                };
            })
            .OrderBy(entity => entity.NetworkId, StringComparer.Ordinal)
            .ToArray();
    }

    private static RekallAgeRuntimeVector3 Lerp(
        RekallAgeRuntimeVector3 from,
        RekallAgeRuntimeVector3 to,
        double alpha)
    {
        return new RekallAgeRuntimeVector3(
            from.X + (to.X - from.X) * alpha,
            from.Y + (to.Y - from.Y) * alpha,
            from.Z + (to.Z - from.Z) * alpha);
    }
}

public static class RekallAgeMultiplayerClientReconciler
{
    public static RekallAgeMultiplayerReconciliationResult Reconcile(
        string clientId,
        int authoritativeInputSequence,
        RekallAgeMultiplayerEntityState authoritativeState,
        RekallAgeMultiplayerEntityState predictedState,
        IEnumerable<RekallAgeMultiplayerInputCommand> pendingInputs,
        double correctionThreshold = 0.05)
    {
        var remainingInputs = pendingInputs
            .Where(input =>
                input.ClientId.Equals(clientId, StringComparison.Ordinal)
                && input.Sequence > authoritativeInputSequence)
            .OrderBy(input => input.Sequence)
            .ToArray();
        var positionError = Distance(authoritativeState.Position, predictedState.Position);
        return new RekallAgeMultiplayerReconciliationResult(
            positionError > Math.Max(0, correctionThreshold),
            positionError,
            authoritativeState,
            remainingInputs);
    }

    private static double Distance(
        RekallAgeRuntimeVector3 from,
        RekallAgeRuntimeVector3 to)
    {
        var x = to.X - from.X;
        var y = to.Y - from.Y;
        var z = to.Z - from.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }
}

public sealed record RekallAgeMultiplayerReconciliationResult(
    bool RequiresCorrection,
    double PositionError,
    RekallAgeMultiplayerEntityState CorrectedState,
    IReadOnlyList<RekallAgeMultiplayerInputCommand> PendingInputs);
