using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimePhysicsQuerySdkTests
{
    [Fact]
    public void Raycast2DReturnsOnlyPlanarCollidersInStableDistanceOrder()
    {
        var world = CreateWorld(
            CreateCircle2D("far", 5, "target"),
            CreateSphere3D("three-d", 1, "target"),
            CreateCircle2D("near", 2, "target"));

        var hits = world.Raycast2D(
            new RekallAgeRuntimeVector2(0, 0),
            new RekallAgeRuntimeVector2(1, 0),
            10,
            tag: "target");

        Assert.Collection(
            hits,
            hit =>
            {
                Assert.Equal("near", hit.Entity.Id);
                Assert.Equal(1.5, hit.Distance, precision: 6);
                Assert.Equal(new RekallAgeRuntimeVector2(1.5, 0), hit.Point);
                Assert.Equal("Rekall.CircleCollider2D", hit.ColliderType);
            },
            hit =>
            {
                Assert.Equal("far", hit.Entity.Id);
                Assert.Equal(4.5, hit.Distance, precision: 6);
            });
    }

    [Fact]
    public void Raycast2DRejectsZeroDirectionAndNonPositiveRange()
    {
        var world = CreateWorld(CreateCircle2D("target", 2, "target"));

        Assert.Empty(world.Raycast2D(
            new RekallAgeRuntimeVector2(0, 0),
            new RekallAgeRuntimeVector2(0, 0),
            10));
        Assert.Empty(world.Raycast2D(
            new RekallAgeRuntimeVector2(0, 0),
            new RekallAgeRuntimeVector2(1, 0),
            0));
    }

    [Fact]
    public void Raycast2DIntersectsBoxShapeInsteadOfItsBoundingCircle()
    {
        var box = new RekallAgeRuntimeEntity(
            "box",
            "box",
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position2D = new RekallAgeRuntimeVector2(4, 0)
            },
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.BoxCollider2D",
                    new JsonObject { ["width"] = 2, ["height"] = 2 })
            ]);

        var hit = Assert.Single(CreateWorld(box).Raycast2D(
            new RekallAgeRuntimeVector2(0, 0),
            new RekallAgeRuntimeVector2(1, 0),
            10));

        Assert.Equal(3, hit.Distance, precision: 6);
        Assert.Equal(new RekallAgeRuntimeVector2(3, 0), hit.Point);
    }

    [Fact]
    public void Raycast2DRespectsAuthoredBoxRotation()
    {
        var box = new RekallAgeRuntimeEntity(
            "box",
            "box",
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position2D = new RekallAgeRuntimeVector2(4, 0),
                Rotation2D = 90
            },
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.BoxCollider2D",
                    new JsonObject { ["width"] = 4, ["height"] = 1 })
            ]);

        var hit = Assert.Single(CreateWorld(box).Raycast2D(
            new RekallAgeRuntimeVector2(0, 0),
            new RekallAgeRuntimeVector2(1, 0),
            10));

        Assert.Equal(3.5, hit.Distance, precision: 6);
        Assert.Equal(new RekallAgeRuntimeVector2(3.5, 0), hit.Point);
    }

    private static RekallAgeRuntimeWorld CreateWorld(params RekallAgeRuntimeEntity[] entities)
    {
        return new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            entities,
            RekallAgeRuntimeSubsystemViews.Empty,
            []);
    }

    private static RekallAgeRuntimeEntity CreateCircle2D(string id, double x, string tag)
    {
        return new RekallAgeRuntimeEntity(
            id,
            id,
            [tag],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position2D = new RekallAgeRuntimeVector2(x, 0)
            },
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.CircleCollider2D",
                    new JsonObject { ["radius"] = 0.5 })
            ]);
    }

    private static RekallAgeRuntimeEntity CreateSphere3D(string id, double x, string tag)
    {
        return new RekallAgeRuntimeEntity(
            id,
            id,
            [tag],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(x, 0, 0)
            },
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["radius"] = 0.5 })
            ]);
    }
}
