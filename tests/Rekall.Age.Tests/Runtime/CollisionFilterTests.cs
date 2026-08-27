using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class CollisionFilterTests
{
    [Fact]
    public void EntitiesWithNoFilterComponentAlwaysCollide()
    {
        Assert.True(RekallAgeCollisionFilter.Allows(Entity("a", null), Entity("b", null)));
    }

    [Fact]
    public void AnAbsentCollidesWithListMeansCollidesWithEverything()
    {
        var withFilter = Entity("a", new JsonObject { ["layer"] = "player" });
        var noFilter = Entity("b", null);
        Assert.True(RekallAgeCollisionFilter.Allows(withFilter, noFilter));
    }

    [Fact]
    public void BothSidesMustAcceptEachOthersLayerSymmetrically()
    {
        var accepts = Entity("a", new JsonObject
        {
            ["layer"] = "player",
            ["collidesWith"] = new JsonArray("enemy")
        });
        var rejects = Entity("b", new JsonObject
        {
            ["layer"] = "enemy",
            ["collidesWith"] = new JsonArray("terrain")
        });

        Assert.False(RekallAgeCollisionFilter.Allows(accepts, rejects));
    }

    [Fact]
    public void MatchingLayersOnBothSidesCollide()
    {
        var a = Entity("a", new JsonObject
        {
            ["layer"] = "player",
            ["collidesWith"] = new JsonArray("enemy")
        });
        var b = Entity("b", new JsonObject
        {
            ["layer"] = "enemy",
            ["collidesWith"] = new JsonArray("player")
        });

        Assert.True(RekallAgeCollisionFilter.Allows(a, b));
    }

    [Fact]
    public void EmptyCollidesWithArrayMeansCollidesWithEverything()
    {
        var a = Entity("a", new JsonObject
        {
            ["layer"] = "player",
            ["collidesWith"] = new JsonArray()
        });
        var b = Entity("b", new JsonObject { ["layer"] = "terrain" });

        Assert.True(RekallAgeCollisionFilter.Allows(a, b));
    }

    private static RekallAgeRuntimeEntity Entity(string id, JsonObject? filterProperties)
    {
        var components = filterProperties is null
            ? Array.Empty<RekallAgeRuntimeComponent>()
            : [new RekallAgeRuntimeComponent("Rekall.CollisionFilter", filterProperties)];
        return new RekallAgeRuntimeEntity(
            id,
            id,
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            components);
    }
}
