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
                    Scale = Lerp(previousEntity.Scale, nextEntity.Scale, alpha),
                    Authority = nextEntity.Authority,
                    ReplicatePosition = nextEntity.ReplicatePosition,
                    ReplicateRotation = nextEntity.ReplicateRotation,
                    ReplicateScale = nextEntity.ReplicateScale,
                    Prediction = nextEntity.Prediction,
                    Priority = nextEntity.Priority
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

public static class RekallAgeMultiplayerSnapshotApplier
{
    public static RekallAgeRuntimeWorld Apply(
        RekallAgeRuntimeWorld world,
        RekallAgeMultiplayerSnapshot snapshot)
    {
        var statesByEntityId = snapshot.Entities.ToDictionary(entity => entity.EntityId, StringComparer.Ordinal);
        var entities = world.Entities.Select(entity =>
        {
            if (!statesByEntityId.TryGetValue(entity.Id, out var state))
            {
                return entity;
            }

            return entity with
            {
                Transform = entity.Transform with
                {
                    Position3D = state.ReplicatePosition ? state.Position : entity.Transform.Position3D,
                    Rotation3D = state.ReplicateRotation ? state.Rotation : entity.Transform.Rotation3D,
                    Scale3D = state.ReplicateScale ? state.Scale : entity.Transform.Scale3D
                }
            };
        }).ToArray();

        return world with
        {
            FrameIndex = snapshot.ServerTick,
            ElapsedTime = TimeSpan.FromSeconds(Math.Max(0, snapshot.ServerTimeSeconds)),
            Entities = entities
        };
    }
}

public sealed record RekallAgeMultiplayerSnapshotDelta(
    int FromServerTick,
    int ToServerTick,
    double ToServerTimeSeconds,
    IReadOnlyList<RekallAgeMultiplayerEntityState> ChangedEntities,
    IReadOnlyList<string> RemovedNetworkIds,
    IReadOnlyDictionary<string, int> LastProcessedInputSequenceByClient);

public static class RekallAgeMultiplayerSnapshotDeltaBuilder
{
    public static RekallAgeMultiplayerSnapshotDelta Build(
        RekallAgeMultiplayerSnapshot previous,
        RekallAgeMultiplayerSnapshot next)
    {
        var previousByNetworkId = previous.Entities.ToDictionary(entity => entity.NetworkId, StringComparer.Ordinal);
        var nextByNetworkId = next.Entities.ToDictionary(entity => entity.NetworkId, StringComparer.Ordinal);
        var changed = next.Entities
            .Where(entity =>
                !previousByNetworkId.TryGetValue(entity.NetworkId, out var previousEntity)
                || !Equals(previousEntity, entity))
            .OrderBy(entity => entity.NetworkId, StringComparer.Ordinal)
            .ToArray();
        var removed = previous.Entities
            .Where(entity => !nextByNetworkId.ContainsKey(entity.NetworkId))
            .Select(entity => entity.NetworkId)
            .OrderBy(networkId => networkId, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeMultiplayerSnapshotDelta(
            previous.ServerTick,
            next.ServerTick,
            next.ServerTimeSeconds,
            changed,
            removed,
            new Dictionary<string, int>(next.LastProcessedInputSequenceByClient, StringComparer.Ordinal));
    }

    public static RekallAgeMultiplayerSnapshot Apply(
        RekallAgeMultiplayerSnapshot previous,
        RekallAgeMultiplayerSnapshotDelta delta)
    {
        var removed = delta.RemovedNetworkIds.ToHashSet(StringComparer.Ordinal);
        var entities = previous.Entities
            .Where(entity => !removed.Contains(entity.NetworkId))
            .ToDictionary(entity => entity.NetworkId, StringComparer.Ordinal);

        foreach (var changed in delta.ChangedEntities)
        {
            entities[changed.NetworkId] = changed;
        }

        return new RekallAgeMultiplayerSnapshot(
            delta.ToServerTick,
            delta.ToServerTimeSeconds,
            entities.Values
                .OrderBy(entity => entity.NetworkId, StringComparer.Ordinal)
                .ToArray(),
            new Dictionary<string, int>(delta.LastProcessedInputSequenceByClient, StringComparer.Ordinal));
    }
}
