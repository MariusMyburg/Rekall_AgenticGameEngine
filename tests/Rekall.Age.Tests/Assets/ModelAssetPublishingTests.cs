using Rekall.Age.AssetPipeline;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;

namespace Rekall.Age.Tests.Assets;

public sealed class ModelAssetPublishingTests
{
    [Fact]
    public async Task FirstPublishCreatesCurrentRevisionOneOutputAndCatalogEntry()
    {
        var fixture = await CreateFixtureAsync();
        var transaction = RekallAgeTransaction.Begin("publish hero model");

        var publish = await fixture.Service.PublishAsync(
            fixture.Root,
            new("hero-model", "Hero Model", new(RekallAgeModelSourceKind.Mesh, "hero-mesh"), RekallAgeDocumentRevision.Missing),
            transaction,
            default);

        Assert.Equal(1, publish.Asset.Revision);
        Assert.Equal(RekallAgeModelBuildState.Current, publish.Asset.BuildState);
        Assert.False(publish.Asset.Frozen);
        Assert.Equal(fixture.MeshRevision, publish.Asset.LastSuccessfulBuild!.SourceFileRevision);
        Assert.Equal(1, publish.Asset.LastSuccessfulBuild.SourceLogicalRevision);
        Assert.Equal(publish.CompiledContentHash, publish.Asset.LastSuccessfulBuild.CompiledContentHash);
        Assert.Equal(RekallAgeModelBuildManifest.CurrentCompilerVersion, publish.Asset.LastSuccessfulBuild.CompilerVersion);
        Assert.True(File.Exists(Path.Combine(fixture.Root, publish.Asset.LastSuccessfulBuild.CompiledMeshPath)));
        Assert.Equal(publish.Asset, await fixture.ModelStore.LoadAsync(fixture.Root, "hero-model", default));

        var catalogAsset = Assert.Single((await fixture.CatalogStore.LoadAsync(fixture.Root, default)).Assets);
        Assert.Equal("hero-model", catalogAsset.Id);
        Assert.Equal("model", catalogAsset.Kind);
        Assert.Equal(publish.CompiledContentHash, catalogAsset.ContentHash);
        Assert.Equal(Path.GetFullPath(fixture.MeshStore.GetMeshPath(fixture.Root, "hero-mesh")), catalogAsset.SourcePath);
        Assert.Equal(Path.GetFullPath(publish.CompiledOutputPath), catalogAsset.ImportedPath);

        Assert.Contains(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model"), transaction.ChangedResources);
        Assert.Contains(fixture.OutputStore.GetFinalPath(fixture.Root, "hero-model"), transaction.ChangedResources);
        Assert.Contains(fixture.CatalogStore.GetCatalogPath(fixture.Root), transaction.ChangedResources);
        Assert.DoesNotContain(transaction.ChangedResources, path => path.Contains(".staging", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IdenticalRebuildAdvancesModelRevisionAndPreservesCompiledHash()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);

        var rebuild = await fixture.Service.RebuildAsync(
            fixture.Root,
            "hero-model",
            first.ModelFileRevision,
            RekallAgeTransaction.Begin("rebuild hero model"),
            default);

        Assert.Equal(2, rebuild.Asset.Revision);
        Assert.NotEqual(first.ModelFileRevision, rebuild.ModelFileRevision);
        Assert.Equal(first.CompiledContentHash, rebuild.CompiledContentHash);
        Assert.Equal(first.Asset.LastSuccessfulBuild!.SourceFileRevision, rebuild.Asset.LastSuccessfulBuild!.SourceFileRevision);
        Assert.Equal(first.Asset.LastSuccessfulBuild.SourceLogicalRevision, rebuild.Asset.LastSuccessfulBuild.SourceLogicalRevision);
        Assert.Equal(await File.ReadAllBytesAsync(first.CompiledOutputPath), await File.ReadAllBytesAsync(rebuild.CompiledOutputPath));
    }

    [Fact]
    public async Task RebuildAfterSourceChangeRecordsNewDependencyAndOutput()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var changedMeshRevision = await ReplaceSourceAsync(fixture, "sphere");

        var rebuild = await fixture.Service.RebuildAsync(
            fixture.Root,
            "hero-model",
            first.ModelFileRevision,
            RekallAgeTransaction.Begin("rebuild changed hero model"),
            default);

        Assert.Equal(2, rebuild.Asset.Revision);
        Assert.Equal(changedMeshRevision, rebuild.Asset.LastSuccessfulBuild!.SourceFileRevision);
        Assert.Equal(2, rebuild.Asset.LastSuccessfulBuild.SourceLogicalRevision);
        Assert.NotEqual(first.CompiledContentHash, rebuild.CompiledContentHash);
        var compiled = await fixture.OutputStore.LoadAsync(fixture.Root, "hero-model", default);
        Assert.Equal("hero-mesh", compiled.SourceAssetId);
        Assert.Equal(2, compiled.SourceLogicalRevision);
    }

    [Fact]
    public async Task StaleExpectedModelRevisionFailsAndRestoresPriorPublishedState()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        _ = await ReplaceSourceAsync(fixture, "sphere");
        var modelBytes = await File.ReadAllBytesAsync(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model"));
        var outputBytes = await File.ReadAllBytesAsync(first.CompiledOutputPath);
        var catalogBytes = await File.ReadAllBytesAsync(fixture.CatalogStore.GetCatalogPath(fixture.Root));
        var transaction = RekallAgeTransaction.Begin("stale rebuild");

        var error = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(() =>
            fixture.Service.RebuildAsync(
                fixture.Root,
                "hero-model",
                RekallAgeDocumentRevision.Missing,
                transaction,
                default).AsTask());

        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", error.Code);
        Assert.Equal(modelBytes, await File.ReadAllBytesAsync(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model")));
        Assert.Equal(outputBytes, await File.ReadAllBytesAsync(first.CompiledOutputPath));
        Assert.Equal(catalogBytes, await File.ReadAllBytesAsync(fixture.CatalogStore.GetCatalogPath(fixture.Root)));
        Assert.Empty(transaction.ChangedResources);
        Assert.Empty(EnumerateStagingFiles(fixture.Root));
    }

    [Fact]
    public async Task InvalidSourceRetainsPriorModelRevisionAndCompiledOutput()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var modelBytes = await File.ReadAllBytesAsync(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model"));
        var outputBytes = await File.ReadAllBytesAsync(first.CompiledOutputPath);
        await File.WriteAllTextAsync(fixture.MeshStore.GetMeshPath(fixture.Root, "hero-mesh"), "{\"schemaVersion\":1}\n");
        var transaction = RekallAgeTransaction.Begin("invalid source rebuild");

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.RebuildAsync(
            fixture.Root,
            "hero-model",
            first.ModelFileRevision,
            transaction,
            default).AsTask());

        Assert.Equal(modelBytes, await File.ReadAllBytesAsync(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model")));
        Assert.Equal(outputBytes, await File.ReadAllBytesAsync(first.CompiledOutputPath));
        Assert.Equal(1, (await fixture.ModelStore.LoadAsync(fixture.Root, "hero-model", default)).Revision);
        Assert.Empty(transaction.ChangedResources);
        Assert.Empty(EnumerateStagingFiles(fixture.Root));
    }

    [Fact]
    public async Task FrozenModelRejectsRebuildAndInspectsAsFrozen()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var frozen = first.Asset with { Revision = 2, Frozen = true, BuildState = RekallAgeModelBuildState.Frozen };
        var frozenRevision = await fixture.ModelStore.SaveIfRevisionAsync(
            fixture.Root,
            frozen,
            first.ModelFileRevision,
            default);
        var outputBytes = await File.ReadAllBytesAsync(first.CompiledOutputPath);

        var error = await Assert.ThrowsAsync<RekallAgeModelPublishingException>(() => fixture.Service.RebuildAsync(
            fixture.Root,
            "hero-model",
            frozenRevision,
            RekallAgeTransaction.Begin("frozen rebuild"),
            default).AsTask());

        Assert.Equal("REKALL_MODEL_FROZEN", error.Code);
        Assert.Equal(outputBytes, await File.ReadAllBytesAsync(first.CompiledOutputPath));
        Assert.Equal(2, (await fixture.ModelStore.LoadAsync(fixture.Root, "hero-model", default)).Revision);
        Assert.Equal(RekallAgeModelBuildState.Frozen, (await fixture.Service.InspectAsync(fixture.Root, "hero-model", default)).BuildState);
    }

    [Fact]
    public async Task InspectionDerivesCurrentStaleFailedAndMissingSourceWithoutMutation()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var modelBytes = await File.ReadAllBytesAsync(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model"));
        var catalogBytes = await File.ReadAllBytesAsync(fixture.CatalogStore.GetCatalogPath(fixture.Root));

        var current = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);
        Assert.Equal(RekallAgeModelBuildState.Current, current.BuildState);
        Assert.Empty(current.Diagnostics);

        var changedRevision = await ReplaceSourceAsync(fixture, "sphere");
        var stale = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);
        Assert.Equal(RekallAgeModelBuildState.Stale, stale.BuildState);
        Assert.Equal(changedRevision, stale.CurrentSourceFileRevision);
        Assert.Contains(stale.Diagnostics, item => item.Code == "REKALL_MODEL_SOURCE_STALE");

        File.Delete(fixture.MeshStore.GetMeshPath(fixture.Root, "hero-mesh"));
        var missing = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);
        Assert.Equal(RekallAgeModelBuildState.Failed, missing.BuildState);
        Assert.Contains(missing.Diagnostics, item => item.Code == "REKALL_MODEL_SOURCE_MISSING");
        Assert.True(missing.CompiledOutputExists);
        Assert.Equal(first.CompiledContentHash, missing.ActualCompiledContentHash);

        Assert.Equal(modelBytes, await File.ReadAllBytesAsync(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model")));
        Assert.Equal(catalogBytes, await File.ReadAllBytesAsync(fixture.CatalogStore.GetCatalogPath(fixture.Root)));
        Assert.True(File.Exists(first.CompiledOutputPath));
    }

    [Fact]
    public void CanonicalCommandSchemasExplainLiveLinkingRevisionSafetyAndSourceShape()
    {
        var publish = new PublishModelAssetCommand();
        var rebuild = new RebuildModelAssetCommand();
        var inspect = new InspectModelAssetCommand();

        Assert.Equal("rekall.asset.model.publish", publish.Name);
        Assert.Equal("rekall.asset.model.rebuild", rebuild.Name);
        Assert.Equal("rekall.asset.model.inspect", inspect.Name);
        Assert.Contains("live-linked", publish.Schema.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{ kind: Mesh, assetId, outputName? }", publish.Schema.Description, StringComparison.Ordinal);
        Assert.Contains("expected Model Asset file revision", rebuild.Schema.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("last successful", rebuild.Schema.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without mutating", inspect.Schema.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandMapsKnownFailuresToStableBoundedModelErrors()
    {
        var root = TestPaths.CreateTempDirectory();
        var command = new PublishModelAssetCommand();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("missing model source"), default);

        var result = await command.ExecuteAsync(
            new PublishModelAssetRequest(
                root,
                "hero-model",
                "Hero Model",
                new(RekallAgeModelSourceKind.Mesh, "missing-mesh"),
                RekallAgeDocumentRevision.Missing),
            context);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_MODEL_SOURCE_MISSING", Assert.Single(result.Errors).Code);
        Assert.Equal("missing-mesh", Assert.Single(result.Errors).Target);
        Assert.Null(result.Value.Publication);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task PublishCommandBoundsNullSourceValidationInsteadOfThrowing()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("invalid model request"), default);

        var result = await new PublishModelAssetCommand().ExecuteAsync(
            new PublishModelAssetRequest(
                root,
                "hero-model",
                "Hero Model",
                null!,
                RekallAgeDocumentRevision.Missing),
            context);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_MODEL_REQUEST_INVALID", Assert.Single(result.Errors).Code);
        Assert.Equal("hero-model", Assert.Single(result.Errors).Target);
        Assert.Null(result.Value.Publication);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    private static async ValueTask<Fixture> CreateFixtureAsync()
    {
        var fixture = new Fixture(TestPaths.CreateTempDirectory());
        var mesh = await new RekallAgeMeshPrimitiveFactory().CreateAsync("box", "hero-mesh", "Hero Mesh", default);
        await fixture.MeshStore.SaveAsync(fixture.Root, mesh, default);
        var loaded = await fixture.MeshStore.LoadVersionedAsync(fixture.Root, "hero-mesh", default);
        return fixture with { MeshRevision = loaded.Revision };
    }

    private static async ValueTask<RekallAgePublishModelResult> PublishAsync(Fixture fixture) =>
        await fixture.Service.PublishAsync(
            fixture.Root,
            new("hero-model", "Hero Model", new(RekallAgeModelSourceKind.Mesh, "hero-mesh"), RekallAgeDocumentRevision.Missing),
            RekallAgeTransaction.Begin("publish hero model"),
            default);

    private static async ValueTask<string> ReplaceSourceAsync(Fixture fixture, string primitive)
    {
        var current = await fixture.MeshStore.LoadVersionedAsync(fixture.Root, "hero-mesh", default);
        var changed = await new RekallAgeMeshPrimitiveFactory().CreateAsync(primitive, "hero-mesh", "Hero Mesh", default);
        return await fixture.MeshStore.SaveIfRevisionAsync(
            fixture.Root,
            changed with { Revision = current.Value.Revision + 1 },
            current.Revision,
            default);
    }

    private static IReadOnlyList<string> EnumerateStagingFiles(string root)
    {
        var staging = Path.Combine(root, "Assets", "Models", ".staging");
        return Directory.Exists(staging)
            ? Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).ToArray()
            : [];
    }

    private sealed record Fixture(string Root)
    {
        public RekallAgeMeshAssetStore MeshStore { get; } = new();
        public RekallAgeModelAssetStore ModelStore { get; } = new();
        public RekallAgePublishedModelOutputStore OutputStore { get; } = new();
        public RekallAgeAssetCatalogStore CatalogStore { get; } = new();
        public RekallAgeModelPublishingService Service { get; } = new();
        public string MeshRevision { get; init; } = string.Empty;
    }
}
