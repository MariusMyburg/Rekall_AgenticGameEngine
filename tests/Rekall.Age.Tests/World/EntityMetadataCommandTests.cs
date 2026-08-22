using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.World;

public sealed class EntityMetadataCommandTests
{
    [Fact]
    public async Task UpdateMetadataChangesOnlyRequestedFieldsAndRecordsTheScene()
    {
        var root = TestPaths.CreateTempDirectory();
        try
        {
            var store = new RekallAgeSceneStore();
            var parent = RekallAgeEntityDocument.Create("Parent", ["group"]);
            var child = RekallAgeEntityDocument.Create("Child", ["prop"]) with
            {
                Visible = true,
                Locked = false
            };
            await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(parent)
                .AddEntity(child), CancellationToken.None);
            var transaction = RekallAgeTransaction.Begin("metadata update");

            var result = await new UpdateEntityMetadataCommand().ExecuteAsync(
                new UpdateEntityMetadataRequest(
                    root,
                    "Main",
                    child.Id,
                    Name: "Renamed",
                    Visible: false,
                    Locked: true,
                    ParentId: parent.Id),
                new RekallAgeCommandContext("test", transaction, CancellationToken.None));

            Assert.True(result.Ok, result.Summary);
            var updated = result.Value.Scene.GetRequiredEntity(child.Id);
            Assert.Equal("Renamed", updated.Name);
            Assert.False(updated.Visible);
            Assert.True(updated.Locked);
            Assert.Equal(parent.Id, updated.ParentId);
            Assert.Equal(["prop"], updated.Tags);
            Assert.Contains(store.GetScenePath(root, "Main"), transaction.ChangedResources);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateMetadataCanClearParentWithoutChangingOtherMetadata()
    {
        var root = TestPaths.CreateTempDirectory();
        try
        {
            var store = new RekallAgeSceneStore();
            var parent = RekallAgeEntityDocument.Create("Parent", []);
            var child = (RekallAgeEntityDocument.Create("Child", []) with
            {
                ParentId = parent.Id,
                Visible = false,
                Locked = true
            });
            await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(parent)
                .AddEntity(child), CancellationToken.None);

            var result = await new UpdateEntityMetadataCommand().ExecuteAsync(
                new UpdateEntityMetadataRequest(root, "Main", child.Id, ClearParent: true),
                Context("clear parent"));

            Assert.True(result.Ok, result.Summary);
            var updated = result.Value.Scene.GetRequiredEntity(child.Id);
            Assert.Null(updated.ParentId);
            Assert.Equal("Child", updated.Name);
            Assert.False(updated.Visible);
            Assert.True(updated.Locked);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("self")]
    [InlineData("descendant")]
    [InlineData("missing")]
    public async Task UpdateMetadataRejectsInvalidParentWithoutMutatingScene(string invalidKind)
    {
        var root = TestPaths.CreateTempDirectory();
        try
        {
            var store = new RekallAgeSceneStore();
            var rootEntity = RekallAgeEntityDocument.Create("Root", []);
            var child = RekallAgeEntityDocument.Create("Child", []) with { ParentId = rootEntity.Id };
            var grandchild = RekallAgeEntityDocument.Create("Grandchild", []) with { ParentId = child.Id };
            await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(rootEntity)
                .AddEntity(child)
                .AddEntity(grandchild), CancellationToken.None);
            var parentId = invalidKind switch
            {
                "self" => rootEntity.Id,
                "descendant" => grandchild.Id,
                _ => "ent_missing"
            };

            var result = await new UpdateEntityMetadataCommand().ExecuteAsync(
                new UpdateEntityMetadataRequest(root, "Main", rootEntity.Id, ParentId: parentId),
                Context("invalid parent"));

            Assert.False(result.Ok);
            var expectedCode = invalidKind switch
            {
                "self" => "REKALL_PARENT_SELF",
                "descendant" => "REKALL_PARENT_CYCLE",
                _ => "REKALL_PARENT_NOT_FOUND"
            };
            Assert.Contains(result.Errors, error => error.Code == expectedCode);
            var unchanged = await store.LoadAsync(root, "Main", CancellationToken.None);
            Assert.Null(unchanged.GetRequiredEntity(rootEntity.Id).ParentId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateMetadataRejectsBlankNameWithoutMutatingScene()
    {
        var root = TestPaths.CreateTempDirectory();
        try
        {
            var store = new RekallAgeSceneStore();
            var entity = RekallAgeEntityDocument.Create("Keep Me", []);
            await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity), CancellationToken.None);

            var result = await new UpdateEntityMetadataCommand().ExecuteAsync(
                new UpdateEntityMetadataRequest(root, "Main", entity.Id, Name: "   "),
                Context("blank rename"));

            Assert.False(result.Ok);
            Assert.Contains(result.Errors, error => error.Code == "REKALL_ENTITY_NAME_REQUIRED");
            Assert.Equal("Keep Me", (await store.LoadAsync(root, "Main", CancellationToken.None))
                .GetRequiredEntity(entity.Id).Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RekallAgeCommandContext Context(string name) =>
        new("test", RekallAgeTransaction.Begin(name), CancellationToken.None);
}
