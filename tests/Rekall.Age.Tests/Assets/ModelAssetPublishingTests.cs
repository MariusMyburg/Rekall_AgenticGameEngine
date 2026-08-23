using Rekall.Age.AssetPipeline;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using System.Text.Json;

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
        Assert.Contains(
            fixture.OutputStore.GetFinalPath(fixture.Root, "hero-model", publish.CompiledContentHash),
            transaction.ChangedResources);
        Assert.Contains(fixture.CatalogStore.GetCatalogPath(fixture.Root), transaction.ChangedResources);
        Assert.DoesNotContain(transaction.ChangedResources, path => path.Contains(".staging", StringComparison.Ordinal));
        Assert.Empty(EnumerateStagingFiles(fixture.Root));
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
        var compiled = await fixture.OutputStore.LoadAsync(
            fixture.Root, "hero-model", rebuild.CompiledContentHash, default);
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
    public async Task MeshCompilerFailureRetainsPriorModelCatalogAndCompiledOutput()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var priorModel = await File.ReadAllBytesAsync(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model"));
        var priorOutput = await File.ReadAllBytesAsync(first.CompiledOutputPath);
        var priorCatalog = await File.ReadAllBytesAsync(fixture.CatalogStore.GetCatalogPath(fixture.Root));
        var currentSource = await fixture.MeshStore.LoadVersionedAsync(fixture.Root, "hero-mesh", default);
        RekallAgeGeometryVector3[] points =
        [
            new(0, 0, 0), new(3, 0, 0), new(4, 2, 0),
            new(2, 4, 0), new(0, 3, 0), new(1, 1, 0)
        ];
        var rejected = PolygonInOrder(points, [0, 1, 3, 5, 4, 2]) with { Revision = 2 };
        Assert.True(new RekallAgeMeshValidator().Validate(rejected).IsValid);
        var compilerError = Assert.Throws<RekallAgeMeshCompileException>(() => new RekallAgeMeshCompiler().Compile(rejected));
        Assert.Equal("REKALL_MESH_COMPILE_TRIANGULATION_FAILED", compilerError.Code);
        await fixture.MeshStore.SaveIfRevisionAsync(fixture.Root, rejected, currentSource.Revision, default);
        var transaction = RekallAgeTransaction.Begin("compiler failure rebuild");

        var error = await Assert.ThrowsAsync<RekallAgeMeshCompileException>(() => fixture.Service.RebuildAsync(
            fixture.Root,
            "hero-model",
            first.ModelFileRevision,
            transaction,
            default).AsTask());

        Assert.Equal("REKALL_MESH_COMPILE_TRIANGULATION_FAILED", error.Code);
        Assert.Equal(priorModel, await File.ReadAllBytesAsync(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model")));
        Assert.Equal(priorOutput, await File.ReadAllBytesAsync(first.CompiledOutputPath));
        Assert.Equal(priorCatalog, await File.ReadAllBytesAsync(fixture.CatalogStore.GetCatalogPath(fixture.Root)));
        Assert.Empty(transaction.ChangedResources);
        Assert.Empty(EnumerateStagingFiles(fixture.Root));
    }

    [Theory]
    [InlineData("immutable-output-committed", RekallAgeModelBuildState.Stale)]
    [InlineData("model-pointer-committed", RekallAgeModelBuildState.Current)]
    [InlineData("catalog-committed", RekallAgeModelBuildState.Current)]
    public async Task ProcessInterruptionAtDurableWriteBoundariesRetainsAValidManifestOutputPair(
        string boundary,
        RekallAgeModelBuildState expectedState)
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        await ReplaceSourceAsync(fixture, "sphere");
        var interrupting = new RekallAgeModelPublishingService(
            fixture.MeshStore,
            new RekallAgeMeshCompiler(),
            fixture.ModelStore,
            fixture.OutputStore,
            fixture.CatalogStore,
            reached =>
            {
                if (reached == boundary)
                {
                    throw new RekallAgeModelPublicationInterruptionException(reached);
                }
            });

        await Assert.ThrowsAsync<RekallAgeModelPublicationInterruptionException>(() =>
            interrupting.RebuildAsync(
                fixture.Root,
                "hero-model",
                first.ModelFileRevision,
                RekallAgeTransaction.Begin("interrupt publication"),
                default).AsTask());

        var inspection = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);
        Assert.Equal(expectedState, inspection.BuildState);
        Assert.True(inspection.CompiledOutputExists);
        var retained = Assert.IsType<RekallAgeModelAssetDocument>(inspection.Asset);
        Assert.True(File.Exists(Path.Combine(fixture.Root, retained.LastSuccessfulBuild!.CompiledMeshPath)));
        Assert.True(File.Exists(first.CompiledOutputPath));

        var loaded = await fixture.ModelStore.LoadVersionedAsync(fixture.Root, "hero-model", default);
        var recovered = await fixture.Service.RebuildAsync(
            fixture.Root,
            "hero-model",
            loaded.Revision,
            RekallAgeTransaction.Begin("recover publication"),
            default);
        Assert.Equal(RekallAgeModelBuildState.Current, recovered.Asset.BuildState);
    }

    [Fact]
    public async Task OversizedPreexistingImmutableOutputFailsBeforePublishingAnyMetadata()
    {
        var fixture = await CreateFixtureAsync();
        var source = await fixture.MeshStore.LoadAsync(fixture.Root, "hero-mesh", default);
        var staged = await fixture.OutputStore.WriteStagedAsync(
            fixture.Root, "hero-model", new RekallAgeMeshCompiler().Compile(source), default);
        await fixture.OutputStore.DeleteStagedAsync(fixture.Root, staged, default);
        var outputPath = fixture.OutputStore.GetFinalPath(fixture.Root, "hero-model", staged.ContentHash);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using (var oversized = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            oversized.SetLength(RekallAgePersistedJson.MaximumDocumentBytes + 1L);
        }
        var transaction = RekallAgeTransaction.Begin("reject oversized immutable output");

        await Assert.ThrowsAsync<RekallAgeBoundedFileSnapshotException>(() =>
            fixture.Service.PublishAsync(
                fixture.Root,
                new("hero-model", "Hero Model", new(RekallAgeModelSourceKind.Mesh, "hero-mesh"), RekallAgeDocumentRevision.Missing),
                transaction,
                default).AsTask());

        Assert.False(File.Exists(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model")));
        Assert.False(File.Exists(fixture.CatalogStore.GetCatalogPath(fixture.Root)));
        Assert.Empty(transaction.ChangedResources);
        Assert.Empty(transaction.ResourcePreimages);
    }

    [Fact]
    public async Task PublicationMergesAConcurrentIndependentCatalogMutationWithoutLoss()
    {
        var fixture = await CreateFixtureAsync();
        var service = new RekallAgeModelPublishingService(
            fixture.MeshStore,
            new RekallAgeMeshCompiler(),
            fixture.ModelStore,
            fixture.OutputStore,
            fixture.CatalogStore,
            boundary =>
            {
                if (boundary == "model-pointer-committed")
                {
                    fixture.CatalogStore.AddOrReplaceAsync(
                        fixture.Root, ConcurrentCatalogAsset(), default).AsTask().GetAwaiter().GetResult();
                }
            });

        var result = await service.PublishAsync(
            fixture.Root,
            new("hero-model", "Hero Model", new(RekallAgeModelSourceKind.Mesh, "hero-mesh"), RekallAgeDocumentRevision.Missing),
            RekallAgeTransaction.Begin("merge concurrent catalog writer"),
            default);

        Assert.Equal("hero-model", result.Asset.AssetId);
        var catalog = await fixture.CatalogStore.LoadAsync(fixture.Root, default);
        Assert.Equal(["concurrent-audio", "hero-model"], catalog.Assets.Select(asset => asset.Id).Order().ToArray());
        Assert.NotNull(catalog.Assets.Single(asset => asset.Id == "hero-model").ModelAssetMetadata);
    }

    [Fact]
    public async Task OversizedExistingModelDocumentFailsBeforeRebuildMutation()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var modelPath = fixture.ModelStore.GetModelPath(fixture.Root, "hero-model");
        await using (var oversized = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            oversized.SetLength(RekallAgePersistedJson.MaximumDocumentBytes + 1L);
        }
        var outputBytes = await File.ReadAllBytesAsync(first.CompiledOutputPath);
        var transaction = RekallAgeTransaction.Begin("reject oversized model document");

        var error = await Assert.ThrowsAsync<Rekall.Age.Core.Compatibility.RekallAgeDocumentCompatibilityException>(() =>
            fixture.Service.RebuildAsync(
                fixture.Root, "hero-model", first.ModelFileRevision, transaction, default).AsTask());
        Assert.IsType<RekallAgeBoundedFileSnapshotException>(error.InnerException);

        Assert.Equal(RekallAgePersistedJson.MaximumDocumentBytes + 1L, new FileInfo(modelPath).Length);
        Assert.Equal(outputBytes, await File.ReadAllBytesAsync(first.CompiledOutputPath));
        Assert.Empty(transaction.ChangedResources);
        Assert.Empty(transaction.ResourcePreimages);
    }

    [Fact]
    public async Task OversizedExistingCatalogRollsBackNewPointerAndImmutableOutput()
    {
        var fixture = await CreateFixtureAsync();
        var catalogPath = fixture.CatalogStore.GetCatalogPath(fixture.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        await using (var oversized = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            oversized.SetLength(RekallAgePersistedJson.MaximumDocumentBytes + 1L);
        }
        var transaction = RekallAgeTransaction.Begin("reject oversized catalog");

        await Assert.ThrowsAsync<RekallAgeBoundedFileSnapshotException>(() =>
            fixture.Service.PublishAsync(
                fixture.Root,
                new("hero-model", "Hero Model", new(RekallAgeModelSourceKind.Mesh, "hero-mesh"), RekallAgeDocumentRevision.Missing),
                transaction,
                default).AsTask());

        Assert.Equal(RekallAgePersistedJson.MaximumDocumentBytes + 1L, new FileInfo(catalogPath).Length);
        Assert.False(File.Exists(fixture.ModelStore.GetModelPath(fixture.Root, "hero-model")));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(fixture.Root, "Assets", "Models", "Compiled"),
            "*.age.compiled-mesh.json",
            SearchOption.AllDirectories));
        Assert.Empty(transaction.ChangedResources);
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
        var inspection = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);
        Assert.Equal(RekallAgeModelBuildState.Frozen, inspection.BuildState);
        Assert.True(inspection.CompiledOutputExists);
        Assert.Equal(first.CompiledContentHash, inspection.ActualCompiledContentHash);
    }

    [Fact]
    public async Task FrozenInspectionRejectsCorrelatedHashAndManifestTamperingWithWrongProvenance()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var frozen = await fixture.Service.FreezeAsync(
            fixture.Root,
            "hero-model",
            first.ModelFileRevision,
            RekallAgeTransaction.Begin("freeze for tamper proof"),
            default);
        var snapshot = await fixture.OutputStore.LoadAsync(
            fixture.Root, "hero-model", first.CompiledContentHash, default);
        var tampered = snapshot with { SourceAssetId = "attacker-mesh" };
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tampered, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = RekallAgePersistedJson.MaximumDocumentDepth
        }) + Environment.NewLine);
        var tamperedHash = RekallAgeDocumentRevision.Compute(bytes);
        var tamperedPath = fixture.OutputStore.GetFinalPath(fixture.Root, "hero-model", tamperedHash);
        Directory.CreateDirectory(Path.GetDirectoryName(tamperedPath)!);
        await File.WriteAllBytesAsync(tamperedPath, bytes);
        var relativePath = Path.GetRelativePath(fixture.Root, tamperedPath).Replace('\\', '/');
        var corrupted = frozen.Value with
        {
            Revision = frozen.Value.Revision + 1,
            LastSuccessfulBuild = frozen.Value.LastSuccessfulBuild! with
            {
                CompiledMeshPath = relativePath,
                CompiledContentHash = tamperedHash
            }
        };
        await fixture.ModelStore.SaveIfRevisionAsync(
            fixture.Root, corrupted, frozen.Revision, default);

        var inspection = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);

        Assert.Equal(RekallAgeModelBuildState.Failed, inspection.BuildState);
        Assert.Contains(inspection.Diagnostics, item => item.Code == "REKALL_MODEL_OUTPUT_PROVENANCE_INVALID");
    }

    [Fact]
    public async Task PersistedFailedModelReportsActualRetainedOutputEvidence()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var failed = first.Asset with { Revision = 2, BuildState = RekallAgeModelBuildState.Failed };
        await fixture.ModelStore.SaveIfRevisionAsync(fixture.Root, failed, first.ModelFileRevision, default);

        var inspection = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);

        Assert.Equal(RekallAgeModelBuildState.Failed, inspection.BuildState);
        Assert.True(inspection.CompiledOutputExists);
        Assert.Equal(first.CompiledContentHash, inspection.ActualCompiledContentHash);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODEL_LAST_BUILD_FAILED");
    }

    [Fact]
    public async Task PersistedFailedModelWithoutManifestStillReportsCanonicalOutputEvidence()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var failed = first.Asset with
        {
            Revision = first.Asset.Revision + 1,
            BuildState = RekallAgeModelBuildState.Failed,
            LastSuccessfulBuild = null
        };
        await fixture.ModelStore.SaveIfRevisionAsync(fixture.Root, failed, first.ModelFileRevision, default);

        var inspection = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);

        Assert.Equal(RekallAgeModelBuildState.Failed, inspection.BuildState);
        Assert.False(inspection.CompiledOutputExists);
        Assert.Null(inspection.ActualCompiledContentHash);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODEL_LAST_BUILD_FAILED");
    }

    [Fact]
    public async Task ManifestPathMismatchReportsMetadataErrorAndActualCanonicalOutputEvidence()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var mismatchedManifest = first.Asset.LastSuccessfulBuild! with
        {
            CompiledMeshPath = "Assets/Models/Compiled/not-hero-model.age.compiled-mesh.json"
        };
        var mismatched = first.Asset with
        {
            Revision = first.Asset.Revision + 1,
            LastSuccessfulBuild = mismatchedManifest
        };
        await fixture.ModelStore.SaveIfRevisionAsync(fixture.Root, mismatched, first.ModelFileRevision, default);

        var inspection = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);

        Assert.Equal(RekallAgeModelBuildState.Failed, inspection.BuildState);
        Assert.True(inspection.CompiledOutputExists);
        Assert.Equal(first.CompiledContentHash, inspection.ActualCompiledContentHash);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODEL_OUTPUT_PATH_MISMATCH");
        Assert.DoesNotContain(inspection.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODEL_OUTPUT_MISSING");
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
    public async Task InspectionReportsMissingCompiledOutputAsFailed()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        File.Delete(first.CompiledOutputPath);

        var inspection = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);

        Assert.Equal(RekallAgeModelBuildState.Failed, inspection.BuildState);
        Assert.False(inspection.CompiledOutputExists);
        Assert.Null(inspection.ActualCompiledContentHash);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODEL_OUTPUT_MISSING");
    }

    [Fact]
    public async Task InspectionReportsCorruptOutputWithItsActualHash()
    {
        var fixture = await CreateFixtureAsync();
        var first = await PublishAsync(fixture);
        var corruptBytes = System.Text.Encoding.UTF8.GetBytes("{ not-valid-json");
        await File.WriteAllBytesAsync(first.CompiledOutputPath, corruptBytes);

        var inspection = await fixture.Service.InspectAsync(fixture.Root, "hero-model", default);

        Assert.Equal(RekallAgeModelBuildState.Failed, inspection.BuildState);
        Assert.True(inspection.CompiledOutputExists);
        Assert.Equal(RekallAgeDocumentRevision.Compute(corruptBytes), inspection.ActualCompiledContentHash);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODEL_OUTPUT_INVALID");
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

    [Fact]
    public async Task PublishCommandBoundsNullRequest()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("null publish"), default);

        var result = await new PublishModelAssetCommand().ExecuteAsync(null!, context);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_MODEL_REQUEST_INVALID", Assert.Single(result.Errors).Code);
        Assert.Null(result.Value.Publication);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task RebuildCommandBoundsNullRequest()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("null rebuild"), default);

        var result = await new RebuildModelAssetCommand().ExecuteAsync(null!, context);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_MODEL_REQUEST_INVALID", Assert.Single(result.Errors).Code);
        Assert.Null(result.Value.Publication);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task RebuildCommandMapsMissingModelAssetPrecisely()
    {
        var root = TestPaths.CreateTempDirectory();
        var result = await new RebuildModelAssetCommand().ExecuteAsync(
            new(root, "missing-model", RekallAgeDocumentRevision.Missing),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("missing rebuild"), default));

        Assert.False(result.Ok);
        Assert.Equal("REKALL_MODEL_ASSET_MISSING", Assert.Single(result.Errors).Code);
        Assert.Equal("missing-model", Assert.Single(result.Errors).Target);
    }

    [Fact]
    public async Task InspectCommandBoundsNullRequest()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("null inspect"), default);

        var result = await new InspectModelAssetCommand().ExecuteAsync(null!, context);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_MODEL_REQUEST_INVALID", Assert.Single(result.Errors).Code);
        Assert.Null(result.Value.Inspection);
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

    private static FileStream HoldDocumentLock(string documentPath) =>
        new(
            RekallAgeAtomicFile.GetLockPath(documentPath),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.Asynchronous);

    private static async ValueTask WaitForBytesToChangeAsync(string path, byte[] priorBytes)
    {
        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            try
            {
                if (File.Exists(path) && !(await File.ReadAllBytesAsync(path)).SequenceEqual(priorBytes))
                {
                    return;
                }
            }
            catch (IOException)
            {
                // The atomic publisher may be between replacement steps; retry the bounded observation.
            }
            await Task.Delay(10);
        }

        throw new TimeoutException($"Timed out waiting for '{path}' to change.");
    }

    private static async ValueTask WaitForCapturedPreimagesAsync(
        RekallAgeTransaction transaction,
        int expectedCount)
    {
        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            if (transaction.ResourcePreimages.Count >= expectedCount)
            {
                return;
            }
            await Task.Delay(10);
        }

        throw new TimeoutException($"Timed out waiting for {expectedCount} transaction preimages.");
    }

    private static async ValueTask<FileStream> WaitForStagedFileAsync(string stagingRoot)
    {
        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            foreach (var path in Directory.EnumerateFiles(
                stagingRoot,
                "*.age.compiled-mesh.json",
                SearchOption.AllDirectories))
            {
                try
                {
                    return new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 1,
                        FileOptions.Asynchronous);
                }
                catch (IOException)
                {
                    // The staged writer may not have closed its atomic replacement yet.
                }
            }
            await Task.Delay(10);
        }

        throw new TimeoutException($"Timed out waiting for staged output beneath '{stagingRoot}'.");
    }

    private static RekallAgeAssetDocument ConcurrentCatalogAsset() =>
        new(
            "concurrent-audio",
            "concurrent-audio",
            "Concurrent Audio",
            "audio",
            "C:\\concurrent\\source.wav",
            "C:\\concurrent\\imported.wav",
            new string('c', 64));

    private static byte[] CatalogBytes(RekallAgeAssetCatalogDocument catalog) =>
        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalog, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = RekallAgePersistedJson.MaximumDocumentDepth
        }) + Environment.NewLine);

    private static Rekall.Age.Modeling.Contracts.RekallAgeMeshAsset PolygonInOrder(
        IReadOnlyList<Rekall.Age.Modeling.Contracts.RekallAgeGeometryVector3> points,
        IReadOnlyList<int> order)
    {
        var count = order.Count;
        return Rekall.Age.Modeling.Contracts.RekallAgeMeshAsset.Create(
            "hero-mesh",
            "Compiler failure",
            new(
                Enumerable.Range(1, count).Select(value => (ulong)value).ToArray(),
                points,
                Enumerable.Range(1, count).Select(value => (ulong)(100 + value)).ToArray(),
                Enumerable.Range(0, count).Select(index =>
                    new Rekall.Age.Modeling.Contracts.RekallAgeMeshEdgePointIndices(order[index], order[(index + 1) % count])).ToArray(),
                [201],
                [0, count],
                Enumerable.Range(1, count).Select(value => (ulong)(300 + value)).ToArray(),
                order,
                Enumerable.Range(0, count).ToArray()));
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
