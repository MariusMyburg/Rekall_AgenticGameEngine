using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class SceneRuntimeFoundationTests
{
    [Fact]
    public void BuilderPreservesSceneIdsHierarchyVisibilityAndComponents()
    {
        var parent = RekallAgeEntityDocument.Create("Root", ["level"]);
        var child = RekallAgeEntityDocument.Create("Player", ["player"]) with
        {
            ParentId = parent.Id,
            PrefabSourceId = "prefab_player",
            Locked = true
        };
        child = child.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Transform2D",
            new JsonObject { ["x"] = 12.5, ["y"] = -2, ["rotation"] = 45, ["scaleX"] = 2, ["scaleY"] = 3 }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(parent).AddEntity(child);

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var runtimeChild = world.Entities.Single(entity => entity.Id == child.Id);

        Assert.Equal(scene.Id, world.SceneId);
        Assert.Equal("Main", world.SceneName);
        Assert.Equal(parent.Id, runtimeChild.ParentId);
        Assert.Equal("prefab_player", runtimeChild.PrefabSourceId);
        Assert.True(runtimeChild.Locked);
        Assert.True(runtimeChild.Visible);
        Assert.Equal("Rekall.Transform2D", Assert.Single(runtimeChild.Components).Type);
        Assert.Equal(12.5, runtimeChild.Transform.Position2D.X);
        Assert.Equal(-2, runtimeChild.Transform.Position2D.Y);
        Assert.Equal(45, runtimeChild.Transform.Rotation2D);
        Assert.Equal(2, runtimeChild.Transform.Scale2D.X);
        Assert.Equal(3, runtimeChild.Transform.Scale2D.Y);
    }

    [Fact]
    public void BuilderExtracts3DTransformAndDoesNotMutateAuthoringScene()
    {
        var entity = RekallAgeEntityDocument.Create("Camera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject
                {
                    ["x"] = 1,
                    ["y"] = 2,
                    ["z"] = 3,
                    ["pitch"] = 10,
                    ["yaw"] = 20,
                    ["roll"] = 30,
                    ["scaleX"] = 4,
                    ["scaleY"] = 5,
                    ["scaleZ"] = 6
                }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity);
        var before = scene.Entities.Single().Components.Single().Properties.ToJsonString();

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var after = scene.Entities.Single().Components.Single().Properties.ToJsonString();

        Assert.Equal(before, after);
        Assert.Equal(new RekallAgeRuntimeVector3(1, 2, 3), world.Entities.Single().Transform.Position3D);
        Assert.Equal(new RekallAgeRuntimeVector3(10, 20, 30), world.Entities.Single().Transform.Rotation3D);
        Assert.Equal(new RekallAgeRuntimeVector3(4, 5, 6), world.Entities.Single().Transform.Scale3D);
    }
}
