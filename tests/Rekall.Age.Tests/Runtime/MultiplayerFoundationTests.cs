using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class MultiplayerFoundationTests
{
    [Fact]
    public void RuntimeProjectionBuildsServerAuthoritativeMultiplayerView()
    {
        var player = RekallAgeEntityDocument.Create("Player Ship", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 10, ["y"] = 2 }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.NetworkIdentity", new JsonObject
            {
                ["networkId"] = "ship-1",
                ["ownerClientId"] = "client-a",
                ["authority"] = "server"
            }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.NetworkTransform", new JsonObject
            {
                ["replicatePosition"] = true,
                ["replicateRotation"] = true,
                ["replicateScale"] = false,
                ["prediction"] = "client-predicted",
                ["priority"] = 12
            }));
        var scene = RekallAgeSceneDocument.Create("Arena", ["world", "multiplayer"])
            .AddEntity(RekallAgeEntityDocument.Create("Server Session", ["network"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MultiplayerSession", new JsonObject
                {
                    ["role"] = "server",
                    ["authority"] = "server",
                    ["tickRate"] = 60,
                    ["snapshotRate"] = 20,
                    ["maxPlayers"] = 16,
                    ["transport"] = "udp",
                    ["address"] = "0.0.0.0",
                    ["port"] = 7777,
                    ["clientPrediction"] = true,
                    ["interpolationDelayMilliseconds"] = 100
                })))
            .AddEntity(player);

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var session = Assert.Single(world.Subsystems.Multiplayer.Sessions);
        Assert.Equal("server", session.Role);
        Assert.Equal("server", session.Authority);
        Assert.Equal(60, session.TickRate);
        Assert.Equal(20, session.SnapshotRate);
        Assert.Equal(16, session.MaxPlayers);
        Assert.Equal("udp", session.Transport);
        Assert.Equal("0.0.0.0", session.Address);
        Assert.Equal(7777, session.Port);
        Assert.True(session.ClientPrediction);
        Assert.Equal(100, session.InterpolationDelayMilliseconds);
        var networkEntity = Assert.Single(world.Subsystems.Multiplayer.Entities);
        Assert.Equal(player.Id, networkEntity.EntityId);
        Assert.Equal("ship-1", networkEntity.NetworkId);
        Assert.Equal("client-a", networkEntity.OwnerClientId);
        Assert.Equal("server", networkEntity.Authority);
        Assert.True(networkEntity.ReplicatePosition);
        Assert.True(networkEntity.ReplicateRotation);
        Assert.False(networkEntity.ReplicateScale);
        Assert.Equal("client-predicted", networkEntity.Prediction);
        Assert.Equal(12, networkEntity.Priority);
    }

    [Fact]
    public async Task AuthoritativeSessionValidatesInputsAndPublishesSnapshots()
    {
        var scene = RekallAgeSceneDocument.Create("Arena", ["world", "multiplayer"])
            .AddEntity(RekallAgeEntityDocument.Create("Player Ship", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 4, ["z"] = -2 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.NetworkIdentity", new JsonObject
                {
                    ["networkId"] = "ship-1",
                    ["ownerClientId"] = "client-a",
                    ["authority"] = "server"
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.NetworkTransform", new JsonObject
                {
                    ["replicatePosition"] = true,
                    ["replicateRotation"] = false,
                    ["replicateScale"] = false,
                    ["prediction"] = "client-predicted",
                    ["priority"] = 7
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var session = new RekallAgeAuthoritativeMultiplayerSession(world, RekallAgeRuntimeExecutionLoop.CreateDefault());

        session.ConnectClient("client-a", "Alice");
        session.ConnectClient("client-b", "Bob");
        var accepted = session.EnqueueInput(new RekallAgeMultiplayerInputCommand(
            "client-a",
            1,
            "ship-1",
            new RekallAgeRuntimeInputFrame(PressedKeys: ["W"])));
        var stale = session.EnqueueInput(new RekallAgeMultiplayerInputCommand(
            "client-a",
            1,
            "ship-1",
            new RekallAgeRuntimeInputFrame(PressedKeys: ["S"])));
        var foreignOwner = session.EnqueueInput(new RekallAgeMultiplayerInputCommand(
            "client-b",
            2,
            "ship-1",
            new RekallAgeRuntimeInputFrame(PressedKeys: ["W"])));

        var snapshot = await session.TickAsync(CancellationToken.None);

        Assert.True(accepted.Accepted, accepted.Reason);
        Assert.False(stale.Accepted);
        Assert.Equal("stale-sequence", stale.Reason);
        Assert.False(foreignOwner.Accepted);
        Assert.Equal("not-owner", foreignOwner.Reason);
        Assert.Equal(1, snapshot.ServerTick);
        Assert.Equal(1, snapshot.LastProcessedInputSequenceByClient["client-a"]);
        Assert.False(snapshot.LastProcessedInputSequenceByClient.ContainsKey("client-b"));
        var entityState = Assert.Single(snapshot.Entities);
        Assert.Equal("ship-1", entityState.NetworkId);
        Assert.Equal("client-a", entityState.OwnerClientId);
        Assert.Equal(4, entityState.Position.X);
        Assert.Equal(-2, entityState.Position.Z);
        Assert.Equal("server", entityState.Authority);
        Assert.True(entityState.ReplicatePosition);
        Assert.False(entityState.ReplicateRotation);
        Assert.False(entityState.ReplicateScale);
        Assert.Equal("client-predicted", entityState.Prediction);
        Assert.Equal(7, entityState.Priority);
    }

    [Fact]
    public void SnapshotInterpolatorSamplesRemoteEntityBetweenAuthoritativeUpdates()
    {
        var previous = new RekallAgeMultiplayerSnapshot(
            10,
            1.0,
            [
                new RekallAgeMultiplayerEntityState(
                    "remote-1",
                    "entity-1",
                    "Remote Ship",
                    "client-b",
                    new RekallAgeRuntimeVector3(0, 0, 0),
                    new RekallAgeRuntimeVector3(0, 10, 0),
                    new RekallAgeRuntimeVector3(1, 1, 1))
                {
                    Prediction = "interpolated",
                    Priority = 1
                }
            ],
            new Dictionary<string, int>());
        var next = new RekallAgeMultiplayerSnapshot(
            12,
            1.2,
            [
                new RekallAgeMultiplayerEntityState(
                    "remote-1",
                    "entity-1",
                    "Remote Ship",
                    "client-b",
                    new RekallAgeRuntimeVector3(10, 0, 0),
                    new RekallAgeRuntimeVector3(0, 30, 0),
                    new RekallAgeRuntimeVector3(2, 2, 2))
                {
                    ReplicateRotation = false,
                    Prediction = "client-predicted",
                    Priority = 9
                }
            ],
            new Dictionary<string, int>());

        var interpolated = RekallAgeMultiplayerSnapshotInterpolator.Sample(previous, next, 1.1);

        var state = Assert.Single(interpolated);
        Assert.Equal("remote-1", state.NetworkId);
        Assert.Equal(5, state.Position.X, 6);
        Assert.Equal(20, state.Rotation.Y, 6);
        Assert.Equal(1.5, state.Scale.X, 6);
        Assert.False(state.ReplicateRotation);
        Assert.Equal("client-predicted", state.Prediction);
        Assert.Equal(9, state.Priority);
    }

    [Fact]
    public void ClientReconcilerDropsAcknowledgedInputsAndReportsCorrectionDistance()
    {
        var pending = new[]
        {
            new RekallAgeMultiplayerInputCommand("client-a", 4, "ship-1", new RekallAgeRuntimeInputFrame()),
            new RekallAgeMultiplayerInputCommand("client-a", 5, "ship-1", new RekallAgeRuntimeInputFrame()),
            new RekallAgeMultiplayerInputCommand("client-a", 6, "ship-1", new RekallAgeRuntimeInputFrame())
        };
        var authoritative = new RekallAgeMultiplayerEntityState(
            "ship-1",
            "entity-1",
            "Player Ship",
            "client-a",
            new RekallAgeRuntimeVector3(10, 0, 0),
            new RekallAgeRuntimeVector3(0, 0, 0),
            new RekallAgeRuntimeVector3(1, 1, 1));
        var predicted = authoritative with
        {
            Position = new RekallAgeRuntimeVector3(12.5, 0, 0)
        };

        var result = RekallAgeMultiplayerClientReconciler.Reconcile(
            "client-a",
            5,
            authoritative,
            predicted,
            pending,
            correctionThreshold: 0.25);

        Assert.True(result.RequiresCorrection);
        Assert.Equal(2.5, result.PositionError);
        Assert.Equal(10, result.CorrectedState.Position.X);
        var remaining = Assert.Single(result.PendingInputs);
        Assert.Equal(6, remaining.Sequence);
    }

    [Fact]
    public void SnapshotApplierUpdatesOnlyReplicatedTransformChannels()
    {
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Arena",
            4,
            TimeSpan.FromSeconds(0.4),
            [
                new RekallAgeRuntimeEntity(
                    "entity-1",
                    "Player Ship",
                    [],
                    null,
                    null,
                    true,
                    false,
                    RekallAgeRuntimeTransform.Identity with
                    {
                        Position3D = new RekallAgeRuntimeVector3(1, 2, 3),
                        Rotation3D = new RekallAgeRuntimeVector3(4, 5, 6),
                        Scale3D = new RekallAgeRuntimeVector3(1, 1, 1)
                    },
                    []),
                new RekallAgeRuntimeEntity(
                    "entity-2",
                    "Local Decoration",
                    [],
                    null,
                    null,
                    true,
                    false,
                    RekallAgeRuntimeTransform.Identity with
                    {
                        Position3D = new RekallAgeRuntimeVector3(9, 9, 9)
                    },
                    [])
            ],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);
        var snapshot = new RekallAgeMultiplayerSnapshot(
            12,
            1.2,
            [
                new RekallAgeMultiplayerEntityState(
                    "ship-1",
                    "entity-1",
                    "Player Ship",
                    "client-a",
                    new RekallAgeRuntimeVector3(10, 20, 30),
                    new RekallAgeRuntimeVector3(40, 50, 60),
                    new RekallAgeRuntimeVector3(2, 3, 4))
                {
                    ReplicatePosition = true,
                    ReplicateRotation = false,
                    ReplicateScale = true
                },
                new RekallAgeMultiplayerEntityState(
                    "missing",
                    "missing-entity",
                    "Missing",
                    null,
                    new RekallAgeRuntimeVector3(100, 100, 100),
                    new RekallAgeRuntimeVector3(100, 100, 100),
                    new RekallAgeRuntimeVector3(100, 100, 100))
            ],
            new Dictionary<string, int>());

        var updated = RekallAgeMultiplayerSnapshotApplier.Apply(world, snapshot);

        Assert.Equal(12, updated.FrameIndex);
        Assert.Equal(1.2, updated.ElapsedTime.TotalSeconds, precision: 6);
        var player = updated.Entities.Single(entity => entity.Id == "entity-1");
        Assert.Equal(new RekallAgeRuntimeVector3(10, 20, 30), player.Transform.Position3D);
        Assert.Equal(new RekallAgeRuntimeVector3(4, 5, 6), player.Transform.Rotation3D);
        Assert.Equal(new RekallAgeRuntimeVector3(2, 3, 4), player.Transform.Scale3D);
        Assert.Equal(new RekallAgeRuntimeVector3(9, 9, 9), updated.Entities.Single(entity => entity.Id == "entity-2").Transform.Position3D);
    }

    [Fact]
    public void SnapshotDeltaTracksChangedAndRemovedNetworkEntities()
    {
        var previous = new RekallAgeMultiplayerSnapshot(
            3,
            0.3,
            [
                CreateEntityState("ship-1", "entity-1", new RekallAgeRuntimeVector3(1, 0, 0)),
                CreateEntityState("ship-2", "entity-2", new RekallAgeRuntimeVector3(2, 0, 0)),
                CreateEntityState("removed", "entity-removed", new RekallAgeRuntimeVector3(3, 0, 0))
            ],
            new Dictionary<string, int> { ["client-a"] = 4 });
        var next = new RekallAgeMultiplayerSnapshot(
            4,
            0.4,
            [
                CreateEntityState("ship-1", "entity-1", new RekallAgeRuntimeVector3(1, 0, 0)),
                CreateEntityState("ship-2", "entity-2", new RekallAgeRuntimeVector3(20, 0, 0)) with { Priority = 7 },
                CreateEntityState("new", "entity-new", new RekallAgeRuntimeVector3(5, 0, 0))
            ],
            new Dictionary<string, int> { ["client-a"] = 5 });

        var delta = RekallAgeMultiplayerSnapshotDeltaBuilder.Build(previous, next);

        Assert.Equal(3, delta.FromServerTick);
        Assert.Equal(4, delta.ToServerTick);
        Assert.Equal(0.4, delta.ToServerTimeSeconds, precision: 6);
        Assert.Equal(["new", "ship-2"], delta.ChangedEntities.Select(entity => entity.NetworkId));
        Assert.Equal(["removed"], delta.RemovedNetworkIds);
        Assert.Equal(5, delta.LastProcessedInputSequenceByClient["client-a"]);
    }

    [Fact]
    public void SnapshotDeltaAppliesChangesAndRemovalsToPreviousSnapshot()
    {
        var previous = new RekallAgeMultiplayerSnapshot(
            3,
            0.3,
            [
                CreateEntityState("ship-1", "entity-1", new RekallAgeRuntimeVector3(1, 0, 0)),
                CreateEntityState("ship-2", "entity-2", new RekallAgeRuntimeVector3(2, 0, 0)),
                CreateEntityState("removed", "entity-removed", new RekallAgeRuntimeVector3(3, 0, 0))
            ],
            new Dictionary<string, int> { ["client-a"] = 4 });
        var delta = new RekallAgeMultiplayerSnapshotDelta(
            3,
            4,
            0.4,
            [
                CreateEntityState("ship-2", "entity-2", new RekallAgeRuntimeVector3(20, 0, 0)),
                CreateEntityState("new", "entity-new", new RekallAgeRuntimeVector3(5, 0, 0))
            ],
            ["removed"],
            new Dictionary<string, int> { ["client-a"] = 5 });

        var next = RekallAgeMultiplayerSnapshotDeltaBuilder.Apply(previous, delta);

        Assert.Equal(4, next.ServerTick);
        Assert.Equal(0.4, next.ServerTimeSeconds, precision: 6);
        Assert.Equal(["new", "ship-1", "ship-2"], next.Entities.Select(entity => entity.NetworkId));
        Assert.Equal(20, next.Entities.Single(entity => entity.NetworkId == "ship-2").Position.X);
        Assert.Equal(5, next.LastProcessedInputSequenceByClient["client-a"]);
    }

    private static RekallAgeMultiplayerEntityState CreateEntityState(
        string networkId,
        string entityId,
        RekallAgeRuntimeVector3 position)
    {
        return new RekallAgeMultiplayerEntityState(
            networkId,
            entityId,
            entityId,
            null,
            position,
            new RekallAgeRuntimeVector3(0, 0, 0),
            new RekallAgeRuntimeVector3(1, 1, 1));
    }
}
