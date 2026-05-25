using Rekall.Age.World;

namespace Rekall.Age.Tests.World;

public sealed class SceneHierarchyMetadataTests
{
    [Fact]
    public void EntitySupportsParentPrefabAndEditorFlags()
    {
        var parent = RekallAgeEntityDocument.Create("Root", ["level"]);
        var child = RekallAgeEntityDocument.Create("Child", ["prop"]) with
        {
            ParentId = parent.Id,
            PrefabSourceId = "prefab_crate",
            Visible = false,
            Locked = true
        };

        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(child)
            .AddEntity(parent);

        Assert.Same(child, scene.GetRequiredEntity(child.Id));
        Assert.Equal(parent.Id, scene.GetRequiredEntity(child.Id).ParentId);
        Assert.Equal("prefab_crate", scene.GetRequiredEntity(child.Id).PrefabSourceId);
        Assert.False(scene.GetRequiredEntity(child.Id).Visible);
        Assert.True(scene.GetRequiredEntity(child.Id).Locked);
    }

    [Fact]
    public void SceneCanReplaceEntityByStableId()
    {
        var entity = RekallAgeEntityDocument.Create("Player", ["player"]);
        var scene = RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity);

        var updated = scene.ReplaceEntity(entity with { Name = "Hero" });

        Assert.Equal("Hero", updated.GetRequiredEntity(entity.Id).Name);
    }
}
