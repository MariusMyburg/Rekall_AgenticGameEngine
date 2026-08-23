using Rekall.Age.Assets;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Tests.Assets;

public sealed class AssetCatalogRevisionTests
{
    [Fact]
    public async Task VersionedCatalogCreationReturnsItsExactFileRevision()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeAssetCatalogStore();
        var before = await store.LoadVersionedAsync(root, default);
        var catalog = RekallAgeAssetCatalogDocument.Empty.AddOrReplace(Asset("hero-model", "Hero"));

        var savedRevision = await store.SaveIfRevisionAsync(
            root,
            catalog,
            RekallAgeDocumentRevision.Missing,
            default);
        var loaded = await store.LoadVersionedAsync(root, default);

        Assert.Equal(RekallAgeDocumentRevision.Missing, before.Revision);
        Assert.Empty(before.Value.Assets);
        Assert.Matches("^[0-9a-f]{64}$", savedRevision);
        Assert.Equal(savedRevision, loaded.Revision);
        Assert.Equal(["hero-model"], loaded.Value.Assets.Select(asset => asset.Id).ToArray());
    }

    [Fact]
    public async Task StaleCatalogSaveRetainsTheConcurrentRevision()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeAssetCatalogStore();
        var initial = RekallAgeAssetCatalogDocument.Empty.AddOrReplace(Asset("hero-model", "Hero"));
        var initialRevision = await store.SaveIfRevisionAsync(root, initial, RekallAgeDocumentRevision.Missing, default);
        var concurrent = initial.AddOrReplace(Asset("concurrent-audio", "Concurrent"));
        var concurrentRevision = await store.SaveIfRevisionAsync(root, concurrent, initialRevision, default);

        var error = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(() =>
            store.SaveIfRevisionAsync(
                root,
                initial.AddOrReplace(Asset("stale-texture", "Stale")),
                initialRevision,
                default).AsTask());

        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", error.Code);
        var loaded = await store.LoadVersionedAsync(root, default);
        Assert.Equal(concurrentRevision, loaded.Revision);
        Assert.Equal(["concurrent-audio", "hero-model"], loaded.Value.Assets.Select(asset => asset.Id).Order().ToArray());
    }

    [Fact]
    public async Task LegacyCatalogSaveAndLoadRemainCompatible()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeAssetCatalogStore();
        await store.SaveAsync(
            root,
            RekallAgeAssetCatalogDocument.Empty.AddOrReplace(Asset("first", "First")),
            default);

        var replacement = RekallAgeAssetCatalogDocument.Empty.AddOrReplace(Asset("replacement", "Replacement"));
        await store.SaveAsync(root, replacement, default);

        var loaded = await store.LoadAsync(root, default);
        var asset = Assert.Single(loaded.Assets);
        Assert.Equal("replacement", asset.Id);
        Assert.Equal("Replacement", asset.DisplayName);
    }

    [Fact]
    public async Task ConcurrentCatalogMutationsRetryAndPreserveIndependentAssets()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeAssetCatalogStore();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCalls = 0;
        var secondCalls = 0;

        var first = store.MutateAsync(root, async (catalog, cancellationToken) =>
        {
            if (Interlocked.Increment(ref firstCalls) == 1)
            {
                firstEntered.SetResult();
                await secondEntered.Task.WaitAsync(cancellationToken);
            }
            return catalog.AddOrReplace(Asset("hero-model", "Hero"));
        }, default).AsTask();
        var second = store.MutateAsync(root, async (catalog, cancellationToken) =>
        {
            if (Interlocked.Increment(ref secondCalls) == 1)
            {
                secondEntered.SetResult();
                await firstEntered.Task.WaitAsync(cancellationToken);
            }
            return catalog.AddOrReplace(Asset("concurrent-audio", "Concurrent"));
        }, default).AsTask();

        await Task.WhenAll(first, second);

        var loaded = await store.LoadAsync(root, default);
        Assert.Equal(["concurrent-audio", "hero-model"], loaded.Assets.Select(asset => asset.Id).Order().ToArray());
        Assert.True(firstCalls > 1 || secondCalls > 1, "One stale mutation must retry against the winner's revision.");
    }

    private static RekallAgeAssetDocument Asset(string id, string displayName) =>
        new(
            id,
            id,
            displayName,
            "model",
            $"C:\\project\\Modeling\\Meshes\\{id}.age.mesh.json",
            $"C:\\project\\Assets\\Models\\Compiled\\{id}.age.compiled-mesh.json",
            new string('a', 64));
}
