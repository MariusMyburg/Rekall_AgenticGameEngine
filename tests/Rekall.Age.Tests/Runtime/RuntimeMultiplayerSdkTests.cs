using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeMultiplayerSdkTests
{
    [Fact]
    public void RuntimeModuleSdkQueriesNetworkSessionsAndEntitiesDeterministically()
    {
        var player = CreateEntity("ent_player", "Player");
        var remote = CreateEntity("ent_remote", "Remote");
        var prop = CreateEntity("ent_prop", "Prop");
        var world = CreateWorld(player, remote, prop);

        Assert.Equal("Server Session", world.PrimaryNetworkSession()?.EntityName);
        Assert.Equal(["ship-1", "ship-2", "prop-1"], world.NetworkEntities().Select(entity => entity.NetworkId));
        Assert.Equal("ship-1", world.NetworkEntityForEntity("ent_player")?.NetworkId);
        Assert.Equal("ent_remote", world.NetworkEntityByNetworkId("ship-2")?.EntityId);
        Assert.Null(world.NetworkEntityByNetworkId("missing"));
    }

    [Fact]
    public void RuntimeModuleSdkQueriesOwnershipAndReplicatedRuntimeEntities()
    {
        var player = CreateEntity("ent_player", "Player");
        var remote = CreateEntity("ent_remote", "Remote");
        var prop = CreateEntity("ent_prop", "Prop");
        var world = CreateWorld(player, remote, prop);

        Assert.Equal(["ship-1"], world.NetworkEntitiesOwnedBy("client-a").Select(entity => entity.NetworkId));
        Assert.Equal(["ent_player"], world.RuntimeEntitiesOwnedBy("client-a").Select(entity => entity.Id));
        Assert.Equal(["ent_player", "ent_remote"], world.ReplicatedRuntimeEntities().Select(entity => entity.Id));
        Assert.True(world.IsNetworkOwner(player, "client-a"));
        Assert.False(world.IsNetworkOwner(remote, "client-a"));
        Assert.True(world.NetworkEntityByNetworkId("ship-1")!.IsReplicated());
        Assert.False(world.NetworkEntityByNetworkId("prop-1")!.IsReplicated());
    }

    private static RekallAgeRuntimeWorld CreateWorld(params RekallAgeRuntimeEntity[] entities)
    {
        return new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            entities,
            RekallAgeRuntimeSubsystemViews.Empty with
            {
                Multiplayer = new RekallAgeRuntimeMultiplayerView(
                [
                    new RekallAgeRuntimeNetworkSession(
                        "session",
                        "Server Session",
                        "server",
                        "server",
                        60,
                        20,
                        8,
                        "websocket",
                        "127.0.0.1",
                        7777,
                        true,
                        100)
                ],
                [
                    new RekallAgeRuntimeNetworkEntity(
                        "ent_prop",
                        "Prop",
                        "prop-1",
                        null,
                        "server",
                        false,
                        false,
                        false,
                        "none",
                        1),
                    new RekallAgeRuntimeNetworkEntity(
                        "ent_remote",
                        "Remote",
                        "ship-2",
                        "client-b",
                        "server",
                        true,
                        false,
                        false,
                        "interpolated",
                        5),
                    new RekallAgeRuntimeNetworkEntity(
                        "ent_player",
                        "Player",
                        "ship-1",
                        "client-a",
                        "server",
                        true,
                        true,
                        false,
                        "client-predicted",
                        10)
                ])
            },
            []);
    }

    private static RekallAgeRuntimeEntity CreateEntity(string id, string name)
    {
        return new RekallAgeRuntimeEntity(
            id,
            name,
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            []);
    }
}
