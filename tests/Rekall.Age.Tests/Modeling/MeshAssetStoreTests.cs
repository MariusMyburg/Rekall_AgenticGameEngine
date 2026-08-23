using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshAssetStoreTests
{
    [Fact]
    public async Task PersistsCanonicalVersionedMeshAndListsLogicalIds()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMeshAssetStore();
        var mesh = CreateTriangle();

        await store.SaveAsync(root, mesh, CancellationToken.None);
        var loaded = await store.LoadVersionedAsync(root, mesh.AssetId, CancellationToken.None);

        Assert.Equal(store.Serialize(mesh), store.Serialize(loaded.Value));
        Assert.Equal([mesh.AssetId], store.ListAssetIds(root));
        Assert.Matches("^[0-9a-f]{64}$", loaded.Revision);
        Assert.EndsWith(Path.Combine("Modeling", "Meshes", "triangle.age.mesh.json"), store.GetMeshPath(root, mesh.AssetId));
        var json = await File.ReadAllTextAsync(store.GetMeshPath(root, mesh.AssetId));
        Assert.EndsWith(Environment.NewLine, json, StringComparison.Ordinal);
        Assert.Equal(json, store.Serialize(await store.LoadAsync(root, mesh.AssetId, CancellationToken.None)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.GetMeshPath(root, mesh.AssetId))!, ".triangle.age.mesh.json.tmp-*"));
    }

    [Fact]
    public async Task RevisionConflictDoesNotOverwriteAndPreviousVersionCanBeRestored()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMeshAssetStore();
        await store.SaveAsync(root, CreateTriangle(), CancellationToken.None);
        var first = await store.LoadVersionedAsync(root, "triangle", CancellationToken.None);
        var secondMesh = first.Value with { Name = "Second", Revision = 2 };

        await store.SaveIfRevisionAsync(root, secondMesh, first.Revision, CancellationToken.None);
        var second = await store.LoadVersionedAsync(root, "triangle", CancellationToken.None);
        var conflict = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(
            () => store.SaveIfRevisionAsync(
                root,
                first.Value with { Name = "Stale", Revision = 2 },
                first.Revision,
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", conflict.Code);
        Assert.Equal("Second", (await store.LoadAsync(root, "triangle", CancellationToken.None)).Name);
        var recovery = await store.InspectRecoveryAsync(root, "triangle", CancellationToken.None);
        Assert.True(recovery.Recoverable);
        var restored = await store.RestorePreviousAsync(root, "triangle", second.Revision, CancellationToken.None);
        Assert.Equal("Triangle", restored.Value.Name);
        Assert.Equal(1, restored.Value.Revision);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/mesh")]
    [InlineData(".")]
    [InlineData("")]
    public void RejectsUnsafeLogicalAssetIds(string assetId)
    {
        var store = new RekallAgeMeshAssetStore();
        Assert.Throws<ArgumentException>(() => store.GetMeshPath("C:\\safe-project", assetId));
    }

    [Fact]
    public async Task RejectsInvalidMeshWithoutPublishingIt()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMeshAssetStore();
        var invalid = CreateTriangle() with
        {
            Topology = CreateTriangle().Topology with { Positions = [new(double.NaN, 0, 0), new(1, 0, 0), new(0, 1, 0)] }
        };

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(root, invalid, CancellationToken.None).AsTask());

        Assert.Contains("REKALL_MESH_POSITION_NONFINITE", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(store.GetMeshPath(root, invalid.AssetId)));
    }

    [Fact]
    public async Task FutureSchemaFailsClosedBeforeDeserialization()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMeshAssetStore();
        await store.SaveAsync(root, CreateTriangle(), CancellationToken.None);
        var path = store.GetMeshPath(root, "triangle");
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal));

        var error = await Assert.ThrowsAsync<RekallAgeDocumentCompatibilityException>(
            () => store.LoadAsync(root, "triangle", CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_SCHEMA_FUTURE", error.Code);
        Assert.Equal("mesh asset", error.DocumentKind);
    }

    private static RekallAgeMeshAsset CreateTriangle()
    {
        return RekallAgeMeshAsset.Create(
            "triangle",
            "Triangle",
            new RekallAgeMeshTopology(
                PointIds: [1, 2, 3],
                Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                EdgeIds: [11, 12, 13],
                EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0)],
                FaceIds: [21],
                FaceOffsets: [0, 3],
                CornerIds: [31, 32, 33],
                CornerPointIndices: [0, 1, 2],
                CornerEdgeIndices: [0, 1, 2]));
    }
}
