using Rekall.Age.AssetPipeline;
using Rekall.Age.Assets;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign;
using Rekall.Age.Rendering;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Core;

public sealed class EngineOwnedJsonStoreTests
{
    [Fact]
    public async Task EngineOwnedStoresRoundTripThroughOneBoundedAtomicPolicy()
    {
        var root = TestPaths.CreateTempDirectory();
        var catalog = new RekallAgeAssetCatalogStore();
        var pipeline = new RekallAgeAssetPipelineStore();
        var prefab = new RekallAgePrefabStore();
        var render = new RekallAgeRenderPlanStore();
        var transactions = new RekallAgeTransactionLogStore();
        var prefabDocument = RekallAgePrefabDocument.Create(
            "Crate",
            RekallAgeEntityDocument.Create("Crate", ["prop"]));

        await catalog.SaveAsync(root, RekallAgeAssetCatalogDocument.Empty, CancellationToken.None);
        await pipeline.SaveAsync(root, RekallAgeAssetPipelineDocument.Empty, CancellationToken.None);
        await prefab.SaveAsync(root, prefabDocument, CancellationToken.None);
        await render.SaveAsync(root, RekallAgeRenderPlanDocument.Create("vulkan", "Main"), CancellationToken.None);
        await transactions.AppendAsync(root, RekallAgeTransaction.Begin("persist"), "test", CancellationToken.None);

        Assert.Empty((await catalog.LoadAsync(root, CancellationToken.None)).Assets);
        Assert.Empty((await pipeline.LoadAsync(root, CancellationToken.None)).Sources);
        Assert.Equal(prefabDocument.Id, (await prefab.LoadAsync(root, prefabDocument.Id, CancellationToken.None)).Id);
        Assert.Equal("vulkan", (await render.LoadAsync(root, CancellationToken.None)).BackendId);
        Assert.Single((await transactions.LoadAsync(root, CancellationToken.None)).Transactions);
        Assert.Empty(Directory.GetFiles(root, ".*.tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AssetCatalogRejectsSparseFileAboveSharedLimitBeforeJsonAllocation()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeAssetCatalogStore();
        var path = store.GetCatalogPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(RekallAgePersistedJson.MaximumDocumentBytes + 1);
        }

        var error = await Assert.ThrowsAsync<RekallAgeBoundedFileSnapshotException>(
            () => store.LoadAsync(root, CancellationToken.None).AsTask());

        Assert.Equal("REKALL_FILE_SNAPSHOT_TOO_LARGE", error.Code);
    }

    [Fact]
    public async Task RenderPlanUsesSharedJsonDepthForBoundedUnknownData()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeRenderPlanStore();
        var path = store.GetPlanPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var nested = new string('[', 80) + "0" + new string(']', 80);
        await File.WriteAllTextAsync(
            path,
            $$"""{"name":"Deep","backendId":"vulkan","resources":[],"pipelines":[],"commandBuffers":[],"extension":{{nested}}}""");

        var plan = await store.LoadAsync(root, CancellationToken.None);

        Assert.Equal("Deep", plan.Name);
        Assert.Equal(128, RekallAgePersistedJson.MaximumDocumentDepth);
    }
}
