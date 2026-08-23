using Rekall.Age.AssetPipeline;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;

namespace Rekall.Age.Tests.Assets;

public sealed class ModelAssetListingTests
{
    [Fact]
    public async Task ListModelAssetsReturnsAssetIdOrderedCurrentAndStaleDependencyHealth()
    {
        var fixture = await CreateFixtureAsync();
        await PublishAsync(fixture, "zebra-model", "zebra-mesh");
        await PublishAsync(fixture, "alpha-model", "alpha-mesh");
        await ReplaceMeshAsync(fixture, "zebra-mesh", "sphere");

        var result = await new ListModelAssetsCommand().ExecuteAsync(
            new ListModelAssetsRequest(fixture.Root),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("list model assets"), default));

        Assert.True(result.Ok, result.Summary);
        Assert.False(result.Value.Truncated);
        Assert.Collection(
            result.Value.Assets,
            asset =>
            {
                Assert.Equal("alpha-model", asset.AssetId);
                Assert.Equal(RekallAgeModelBuildState.Current, asset.BuildState);
                Assert.Empty(asset.Diagnostics);
            },
            asset =>
            {
                Assert.Equal("zebra-model", asset.AssetId);
                Assert.Equal(RekallAgeModelBuildState.Stale, asset.BuildState);
                Assert.Contains(asset.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODEL_SOURCE_STALE");
            });
    }

    [Fact]
    public async Task PublishingModelPersistsCompactProjectRelativeCatalogMetadata()
    {
        var fixture = await CreateFixtureAsync();
        var publication = await PublishAsync(fixture, "hero-model", "hero-mesh");

        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(fixture.Root, default);
        var asset = Assert.Single(catalog.Assets);
        var metadata = Assert.IsType<RekallAgeModelAssetCatalogMetadata>(asset.ModelAssetMetadata);

        Assert.Equal("hero-model", asset.Id);
        Assert.Equal("model", asset.Kind);
        Assert.Equal("Assets/Models/hero-model.age.model.json", metadata.ModelDocumentPath);
        Assert.Equal("Mesh", metadata.SourceKind);
        Assert.Equal("hero-mesh", metadata.SourceAssetId);
        Assert.Equal(publication.Asset.LastSuccessfulBuild!.CompiledMeshPath, metadata.CompiledOutputPath);
        Assert.Equal(publication.CompiledContentHash, metadata.CompiledContentHash);
        Assert.False(Path.IsPathFullyQualified(metadata.ModelDocumentPath));
        Assert.False(Path.IsPathFullyQualified(metadata.CompiledOutputPath));
    }

    [Fact]
    public async Task ListModelAssetsBoundsResultsAtTheEngineOutputBudget()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelAssetStore();
        for (var index = 0; index < 9; index++)
        {
            var assetId = $"model-{index:D3}";
            var document = RekallAgeModelAssetDocument.Create(
                assetId,
                assetId,
                new(RekallAgeModelSourceKind.Mesh, $"mesh-{index:D3}"),
                RekallAgeModelBuildManifest.Success(
                    "source-revision",
                    1,
                    $"Assets/Models/Compiled/{assetId}.age.compiled-mesh.json",
                    new string('a', 64),
                    RekallAgeModelBuildManifest.CurrentCompilerVersion));
            var path = store.GetModelPath(root, assetId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, store.Serialize(document));
        }

        var result = await new ListModelAssetsCommand().ExecuteAsync(
            new ListModelAssetsRequest(root),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("bounded model list"), default));

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Truncated);
        Assert.Equal(8, result.Value.Assets.Count);
        Assert.Equal("model-000", result.Value.Assets[0].AssetId);
        Assert.Equal("model-007", result.Value.Assets[^1].AssetId);
    }

    private static async ValueTask<Fixture> CreateFixtureAsync()
    {
        var fixture = new Fixture(TestPaths.CreateTempDirectory());
        await SaveMeshAsync(fixture, "alpha-mesh", "box");
        await SaveMeshAsync(fixture, "zebra-mesh", "box");
        await SaveMeshAsync(fixture, "hero-mesh", "box");
        return fixture;
    }

    private static async ValueTask SaveMeshAsync(Fixture fixture, string assetId, string primitive)
    {
        var mesh = await new RekallAgeMeshPrimitiveFactory().CreateAsync(primitive, assetId, assetId, default);
        await fixture.MeshStore.SaveAsync(fixture.Root, mesh, default);
    }

    private static ValueTask<RekallAgePublishModelResult> PublishAsync(Fixture fixture, string modelAssetId, string meshAssetId) =>
        fixture.Service.PublishAsync(
            fixture.Root,
            new(modelAssetId, modelAssetId, new(RekallAgeModelSourceKind.Mesh, meshAssetId), RekallAgeDocumentRevision.Missing),
            RekallAgeTransaction.Begin($"publish {modelAssetId}"),
            default);

    private static async ValueTask ReplaceMeshAsync(Fixture fixture, string assetId, string primitive)
    {
        var current = await fixture.MeshStore.LoadVersionedAsync(fixture.Root, assetId, default);
        var replacement = await new RekallAgeMeshPrimitiveFactory().CreateAsync(primitive, assetId, assetId, default);
        await fixture.MeshStore.SaveIfRevisionAsync(
            fixture.Root,
            replacement with { Revision = current.Value.Revision + 1 },
            current.Revision,
            default);
    }

    private sealed record Fixture(string Root)
    {
        public RekallAgeMeshAssetStore MeshStore { get; } = new();

        public RekallAgeModelPublishingService Service { get; } = new();
    }
}
