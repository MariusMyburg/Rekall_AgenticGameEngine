using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;

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
        using var firstAttemptBarrier = new Barrier(2);
        var firstCalls = 0;
        var secondCalls = 0;

        var first = Task.Run(async () => await store.MutateAsync(root, catalog =>
            {
                if (Interlocked.Increment(ref firstCalls) == 1)
                {
                    Assert.True(firstAttemptBarrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                }
                return catalog.AddOrReplace(Asset("hero-model", "Hero"));
            }, default));
        var second = Task.Run(async () => await store.MutateAsync(root, catalog =>
            {
                if (Interlocked.Increment(ref secondCalls) == 1)
                {
                    Assert.True(firstAttemptBarrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                }
                return catalog.AddOrReplace(Asset("concurrent-audio", "Concurrent"));
            }, default));

        await Task.WhenAll(first, second);

        var loaded = await store.LoadAsync(root, default);
        Assert.Equal(["concurrent-audio", "hero-model"], loaded.Assets.Select(asset => asset.Id).Order().ToArray());
        Assert.True(firstCalls > 1 || secondCalls > 1, "One stale mutation must retry against the winner's revision.");
    }

    [Fact]
    public async Task CatalogMutationExhaustionMapsSixteenRevisionConflictsToStableBusyError()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeAssetCatalogStore();
        using var attemptBarrier = new Barrier(2);
        var calls = 0;
        var competitor = Task.Run(async () =>
        {
            for (var attempt = 1; attempt <= RekallAgeAssetCatalogStore.MaximumMutationAttempts; attempt++)
            {
                Assert.True(attemptBarrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                var loaded = await store.LoadVersionedAsync(root, default);
                await store.SaveIfRevisionAsync(
                    root,
                    loaded.Value.AddOrReplace(Asset($"competitor-{attempt:D2}", $"Competitor {attempt:D2}")),
                    loaded.Revision,
                    default);
                Assert.True(attemptBarrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            }
        });

        var error = await Assert.ThrowsAsync<RekallAgeAssetCatalogBusyException>(() =>
            store.MutateAsync(root, catalog =>
            {
                Interlocked.Increment(ref calls);
                Assert.True(attemptBarrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                Assert.True(attemptBarrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                return catalog.AddOrReplace(Asset("hero-model", "Hero"));
            }, default).AsTask());
        await competitor;

        Assert.Equal("REKALL_ASSET_CATALOG_BUSY", error.Code);
        Assert.Equal(RekallAgeAssetCatalogStore.MaximumMutationAttempts, calls);
        Assert.IsType<RekallAgeDocumentRevisionException>(error.InnerException);
    }

    [Fact]
    public async Task CatalogMutationDoesNotRetryOrMapNonRevisionFailures()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeAssetCatalogStore();
        var expected = new InvalidDataException("non-retryable mutation failure");
        var calls = 0;

        var actual = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.MutateAsync(root, _ =>
            {
                Interlocked.Increment(ref calls);
                throw expected;
            }, default).AsTask());

        Assert.Same(expected, actual);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DynamicCommandBoundaryPreservesStableCatalogBusyCodeAndTarget()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = new RekallAgeAssetCatalogStore().GetCatalogPath(root);
        var busy = new RekallAgeAssetCatalogBusyException(
            path,
            RekallAgeAssetCatalogStore.MaximumMutationAttempts,
            new InvalidOperationException("forced contention"));
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CatalogBusyCommand(busy));

        var result = await registry.ExecuteJsonAsync(
            "rekall.test.catalog_busy",
            "{}",
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("catalog busy"), default));

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_ASSET_CATALOG_BUSY", error.Code);
        Assert.Equal(path, error.Target);
    }

    private sealed record CatalogBusyRequest(string? Marker = null);

    private sealed record CatalogBusyResult;

    private sealed class CatalogBusyCommand(RekallAgeAssetCatalogBusyException error)
        : IRekallAgeCommand<CatalogBusyRequest, CatalogBusyResult>
    {
        public string Name => "rekall.test.catalog_busy";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Throws a deterministic catalog contention error.",
            typeof(CatalogBusyRequest).FullName!,
            typeof(CatalogBusyResult).FullName!);

        public ValueTask<RekallAgeCommandResult<CatalogBusyResult>> ExecuteAsync(
            CatalogBusyRequest request,
            RekallAgeCommandContext context) =>
            throw error;
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
