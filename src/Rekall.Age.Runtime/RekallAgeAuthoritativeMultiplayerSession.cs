using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeAuthoritativeMultiplayerSession
{
    private readonly RekallAgeRuntimeExecutionLoop _loop;
    private readonly Dictionary<string, RekallAgeMultiplayerClient> _clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lastAcceptedSequenceByClient = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lastProcessedSequenceByClient = new(StringComparer.Ordinal);
    private readonly List<RekallAgeMultiplayerInputCommand> _pendingInputs = [];

    public RekallAgeAuthoritativeMultiplayerSession(
        RekallAgeRuntimeWorld initialWorld,
        RekallAgeRuntimeExecutionLoop loop)
    {
        World = initialWorld;
        _loop = loop;
    }

    public RekallAgeRuntimeWorld World { get; private set; }

    public int ServerTick { get; private set; }

    public IReadOnlyCollection<RekallAgeMultiplayerClient> Clients => _clients.Values;

    public RekallAgeMultiplayerClient ConnectClient(string clientId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        var client = new RekallAgeMultiplayerClient(
            clientId.Trim(),
            string.IsNullOrWhiteSpace(displayName) ? clientId.Trim() : displayName.Trim(),
            true);
        _clients[client.ClientId] = client;
        return client;
    }

    public void DisconnectClient(string clientId)
    {
        _clients.Remove(clientId);
    }

    public RekallAgeMultiplayerInputResult EnqueueInput(RekallAgeMultiplayerInputCommand input)
    {
        if (!_clients.ContainsKey(input.ClientId))
        {
            return new RekallAgeMultiplayerInputResult(false, "unknown-client");
        }

        var networkEntity = World.Subsystems.Multiplayer.Entities.FirstOrDefault(entity =>
            entity.NetworkId.Equals(input.NetworkId, StringComparison.Ordinal));
        if (networkEntity is null)
        {
            return new RekallAgeMultiplayerInputResult(false, "unknown-network-entity");
        }

        if (!string.IsNullOrWhiteSpace(networkEntity.OwnerClientId)
            && !networkEntity.OwnerClientId.Equals(input.ClientId, StringComparison.Ordinal))
        {
            return new RekallAgeMultiplayerInputResult(false, "not-owner");
        }

        if (_lastAcceptedSequenceByClient.TryGetValue(input.ClientId, out var lastAccepted)
            && input.Sequence <= lastAccepted)
        {
            return new RekallAgeMultiplayerInputResult(false, "stale-sequence");
        }

        _lastAcceptedSequenceByClient[input.ClientId] = input.Sequence;
        _pendingInputs.Add(input);
        return new RekallAgeMultiplayerInputResult(true, "accepted");
    }

    public async ValueTask<RekallAgeMultiplayerSnapshot> TickAsync(CancellationToken cancellationToken)
    {
        RekallAgeRuntimeInputState? firstInput = null;
        foreach (var input in _pendingInputs.OrderBy(input => input.Sequence))
        {
            _lastProcessedSequenceByClient[input.ClientId] = input.Sequence;
            firstInput ??= input.Input.ToState();
        }

        _pendingInputs.Clear();
        var result = await _loop.RunAsync(World, 1, cancellationToken, firstInput).ConfigureAwait(false);
        World = result.World;
        ServerTick = World.FrameIndex;
        return BuildSnapshot();
    }

    public RekallAgeMultiplayerSnapshot BuildSnapshot()
    {
        var states = World.Subsystems.Multiplayer.Entities
            .Select(entity =>
            {
                var runtimeEntity = World.Entities.First(item => item.Id.Equals(entity.EntityId, StringComparison.Ordinal));
                return new RekallAgeMultiplayerEntityState(
                    entity.NetworkId,
                    entity.EntityId,
                    entity.EntityName,
                    entity.OwnerClientId,
                    runtimeEntity.Transform.Position3D,
                    runtimeEntity.Transform.Rotation3D,
                    runtimeEntity.Transform.Scale3D);
            })
            .OrderBy(entity => entity.NetworkId, StringComparer.Ordinal)
            .ToArray();
        return new RekallAgeMultiplayerSnapshot(
            ServerTick,
            World.ElapsedTime.TotalSeconds,
            states,
            new Dictionary<string, int>(_lastProcessedSequenceByClient, StringComparer.Ordinal));
    }
}

public sealed record RekallAgeMultiplayerClient(
    string ClientId,
    string DisplayName,
    bool Connected);

public sealed record RekallAgeMultiplayerInputCommand(
    string ClientId,
    int Sequence,
    string NetworkId,
    RekallAgeRuntimeInputFrame Input,
    double ClientTimeSeconds = 0);

public sealed record RekallAgeMultiplayerInputResult(
    bool Accepted,
    string Reason);

public sealed record RekallAgeMultiplayerSnapshot(
    int ServerTick,
    double ServerTimeSeconds,
    IReadOnlyList<RekallAgeMultiplayerEntityState> Entities,
    IReadOnlyDictionary<string, int> LastProcessedInputSequenceByClient);

public sealed record RekallAgeMultiplayerEntityState(
    string NetworkId,
    string EntityId,
    string EntityName,
    string? OwnerClientId,
    RekallAgeRuntimeVector3 Position,
    RekallAgeRuntimeVector3 Rotation,
    RekallAgeRuntimeVector3 Scale);
