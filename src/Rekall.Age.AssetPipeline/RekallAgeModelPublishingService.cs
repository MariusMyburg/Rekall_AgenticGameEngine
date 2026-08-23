using Rekall.Age.Assets;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using System.Text.Json;

namespace Rekall.Age.AssetPipeline;

public sealed record RekallAgePublishModelRequest(
    string AssetId,
    string DisplayName,
    RekallAgeModelSourceReference Source,
    string ExpectedModelFileRevision);

public sealed record RekallAgePublishModelResult(
    RekallAgeModelAssetDocument Asset,
    string ModelFileRevision,
    string CompiledOutputPath,
    string CompiledContentHash);

public sealed class RekallAgeModelPublishingException : InvalidOperationException
{
    public RekallAgeModelPublishingException(string code, string message, string? target = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Target = target;
    }

    public string Code { get; }

    public string? Target { get; }
}

public sealed class RekallAgeModelPublicationInterruptionException(string boundary)
    : Exception($"Simulated process interruption at Model Asset publication boundary '{boundary}'.")
{
    public string Boundary { get; } = boundary;
}

public sealed class RekallAgeModelPublishingService : IRekallAgeModelAssetHealthInspector
{
    private const int MaximumDiagnostics = 16;
    private readonly RekallAgeMeshAssetStore _meshStore;
    private readonly RekallAgeMeshCompiler _compiler;
    private readonly RekallAgeModelAssetStore _modelStore;
    private readonly RekallAgePublishedModelOutputStore _outputStore;
    private readonly RekallAgeAssetCatalogStore _catalogStore;
    private readonly Action<string>? _publicationBoundary;

    public RekallAgeModelPublishingService()
        : this(
            new RekallAgeMeshAssetStore(),
            new RekallAgeMeshCompiler(),
            new RekallAgeModelAssetStore(),
            new RekallAgePublishedModelOutputStore(),
            new RekallAgeAssetCatalogStore())
    {
    }

    public RekallAgeModelPublishingService(
        RekallAgeMeshAssetStore meshStore,
        RekallAgeMeshCompiler compiler,
        RekallAgeModelAssetStore modelStore,
        RekallAgePublishedModelOutputStore outputStore,
        RekallAgeAssetCatalogStore catalogStore,
        Action<string>? publicationBoundary = null)
    {
        _meshStore = meshStore ?? throw new ArgumentNullException(nameof(meshStore));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _outputStore = outputStore ?? throw new ArgumentNullException(nameof(outputStore));
        _catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
        _publicationBoundary = publicationBoundary;
    }

    public async ValueTask<RekallAgePublishModelResult> PublishAsync(
        string projectRoot,
        RekallAgePublishModelRequest request,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transaction);
        ValidateRequest(request);

        var modelPath = _modelStore.GetModelPath(projectRoot, request.AssetId);
        RekallAgeModelAssetDocument? existing = null;
        var loadedModelRevision = RekallAgeDocumentRevision.Missing;
        if (File.Exists(modelPath))
        {
            var loadedModel = await _modelStore.LoadVersionedAsync(projectRoot, request.AssetId, cancellationToken)
                .ConfigureAwait(false);
            existing = loadedModel.Value;
            loadedModelRevision = loadedModel.Revision;
            RejectFrozen(existing);
        }

        var source = await LoadSourceAsync(projectRoot, request.Source, cancellationToken).ConfigureAwait(false);
        var compiled = _compiler.Compile(source.Value);
        RekallAgeStagedModelOutput? staged = null;
        IReadOnlyList<ModelPublicationMutation>? mutationJournal = null;
        try
        {
            staged = await _outputStore.WriteStagedAsync(
                projectRoot,
                request.AssetId,
                compiled,
                cancellationToken).ConfigureAwait(false);

            var outputPath = Path.GetFullPath(Path.Combine(projectRoot, staged.RelativeFinalPath));
            var catalogPath = _catalogStore.GetCatalogPath(projectRoot);
            var manifest = RekallAgeModelBuildManifest.Success(
                source.Revision,
                source.Value.Revision,
                staged.RelativeFinalPath,
                staged.ContentHash,
                RekallAgeModelBuildManifest.CurrentCompilerVersion);
            var model = existing is null
                ? RekallAgeModelAssetDocument.Create(request.AssetId, request.DisplayName.Trim(), request.Source, manifest)
                : existing with
                {
                    DisplayName = request.DisplayName.Trim(),
                    Revision = existing.Revision + 1,
                    Source = request.Source,
                    BuildState = RekallAgeModelBuildState.Current,
                    LastSuccessfulBuild = manifest,
                    Frozen = false
                };
            var catalogAsset = new RekallAgeAssetDocument(
                request.AssetId,
                request.AssetId,
                request.DisplayName.Trim(),
                "model",
                Path.GetFullPath(_meshStore.GetMeshPath(projectRoot, request.Source.AssetId)),
                Path.GetFullPath(outputPath),
                staged.ContentHash)
            {
                ModelAssetMetadata = new RekallAgeModelAssetCatalogMetadata(
                    ToProjectRelativePath(projectRoot, modelPath),
                    request.Source.Kind.ToString(),
                    request.Source.AssetId,
                    staged.RelativeFinalPath,
                    staged.ContentHash)
            };

            _ = await _outputStore.CommitStagedImmutableAsync(
                projectRoot,
                staged,
                cancellationToken).ConfigureAwait(false);
            _publicationBoundary?.Invoke("immutable-output-committed");

            // Immutable outputs are append-only shared resources once visible. They are never
            // rollback-owned or recorded as deletable transaction preimages. A failed pointer
            // publication may intentionally leave an unreachable blob for a future guarded GC.
            mutationJournal = await CaptureMutationJournalAsync(
                [modelPath],
                cancellationToken).ConfigureAwait(false);
            var modelMutation = mutationJournal[0];
            RequireMatchingPreimageRevision(request.ExpectedModelFileRevision, modelMutation);
            RequireMatchingPreimageRevision(loadedModelRevision, modelMutation);
            transaction.RecordResourcePreimage(
                modelMutation.Path,
                modelMutation.BeforeBytes is not null,
                modelMutation.BeforeBytes ?? []);
            var modelRevision = await _modelStore.SaveIfRevisionAsync(
                projectRoot,
                model,
                modelMutation.BeforeRevision,
                cancellationToken).ConfigureAwait(false);
            modelMutation.RecordWrite(modelRevision);
            _publicationBoundary?.Invoke("model-pointer-committed");
            await _catalogStore.AddOrReplaceAsync(projectRoot, catalogAsset, cancellationToken).ConfigureAwait(false);
            _publicationBoundary?.Invoke("catalog-committed");

            transaction.RecordChangedResource(modelPath);
            transaction.RecordChangedResource(catalogPath);
            return new(model, modelRevision, outputPath, staged.ContentHash);
        }
        catch (Exception publicationError) when (
            publicationError is not RekallAgeModelPublicationInterruptionException
            && mutationJournal?.Any(item => item.Written) == true)
        {
            var rollbackErrors = await RestoreMutationsAsync(mutationJournal).ConfigureAwait(false);
            if (rollbackErrors.Count > 0)
            {
                throw new RekallAgeModelPublishingException(
                    "REKALL_MODEL_ROLLBACK_FAILED",
                    $"Model Asset publication failed and {rollbackErrors.Count} prior resource(s) could not be restored without overwriting newer content.",
                    request.AssetId,
                    new AggregateException([publicationError, .. rollbackErrors]));
            }

            throw;
        }
        finally
        {
            if (staged is not null)
            {
                try
                {
                    await _outputStore.DeleteStagedAsync(projectRoot, staged, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Staging cleanup is best effort. Published state is canonical and must not
                    // be reported as failed merely because an unreferenced staging file is busy.
                }
            }
        }
    }

    public async ValueTask<RekallAgePublishModelResult> RebuildAsync(
        string projectRoot,
        string assetId,
        string expectedModelFileRevision,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        RekallAgeVersionedDocument<RekallAgeModelAssetDocument> current;
        try
        {
            current = await _modelStore.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new RekallAgeModelPublishingException(
                "REKALL_MODEL_ASSET_MISSING",
                $"Model Asset '{assetId}' was not found.",
                assetId,
                error);
        }
        RejectFrozen(current.Value);
        return await PublishAsync(
            projectRoot,
            new(current.Value.AssetId, current.Value.DisplayName, current.Value.Source, expectedModelFileRevision),
            transaction,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<RekallAgeVersionedDocument<RekallAgeModelAssetDocument>> FreezeAsync(
        string projectRoot,
        string assetId,
        string expectedModelFileRevision,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken) =>
        SetFrozenAsync(projectRoot, assetId, expectedModelFileRevision, frozen: true, transaction, cancellationToken);

    public ValueTask<RekallAgeVersionedDocument<RekallAgeModelAssetDocument>> UnfreezeAsync(
        string projectRoot,
        string assetId,
        string expectedModelFileRevision,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken) =>
        SetFrozenAsync(projectRoot, assetId, expectedModelFileRevision, frozen: false, transaction, cancellationToken);

    private async ValueTask<RekallAgeVersionedDocument<RekallAgeModelAssetDocument>> SetFrozenAsync(
        string projectRoot,
        string assetId,
        string expectedModelFileRevision,
        bool frozen,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var loaded = await _modelStore.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(loaded.Revision, expectedModelFileRevision, StringComparison.Ordinal))
        {
            throw new RekallAgeDocumentRevisionException(
                "REKALL_DOCUMENT_REVISION_CONFLICT",
                _modelStore.GetModelPath(projectRoot, assetId),
                $"Model Asset '{assetId}' changed after revision '{expectedModelFileRevision}'.",
                expectedModelFileRevision,
                loaded.Revision);
        }

        var manifest = loaded.Value.LastSuccessfulBuild
            ?? throw new RekallAgeModelPublishingException(
                "REKALL_MODEL_NOT_PLACEABLE",
                $"Model Asset '{assetId}' has no successful output to freeze.",
                assetId);
        var state = RekallAgeModelBuildState.Frozen;
        if (frozen)
        {
            var inspection = await InspectAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
            if (!inspection.CompiledOutputExists
                || inspection.BuildState is not RekallAgeModelBuildState.Current and not RekallAgeModelBuildState.Stale)
            {
                var diagnostic = inspection.Diagnostics.FirstOrDefault();
                throw new RekallAgeModelPublishingException(
                    diagnostic?.Code ?? "REKALL_MODEL_NOT_PLACEABLE",
                    diagnostic?.Message ?? $"Model Asset '{assetId}' has no validated output to freeze.",
                    diagnostic?.Target ?? assetId);
            }
        }
        else
        {
            RekallAgeVersionedDocument<Rekall.Age.Modeling.Contracts.RekallAgeMeshAsset> source;
            try
            {
                source = await LoadSourceAsync(projectRoot, loaded.Value.Source, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                throw new RekallAgeModelPublishingException(
                    "REKALL_MODEL_SOURCE_MISSING",
                    $"Model Asset '{assetId}' cannot be unfrozen because editable source '{loaded.Value.Source.AssetId}' is missing.",
                    loaded.Value.Source.AssetId,
                    error);
            }

            state = string.Equals(source.Revision, manifest.SourceFileRevision, StringComparison.Ordinal)
                && source.Value.Revision == manifest.SourceLogicalRevision
                && string.Equals(manifest.CompilerVersion, RekallAgeModelBuildManifest.CurrentCompilerVersion, StringComparison.Ordinal)
                    ? RekallAgeModelBuildState.Current
                    : RekallAgeModelBuildState.Stale;
        }

        var path = _modelStore.GetModelPath(projectRoot, assetId);
        var before = await RekallAgeBoundedFileSnapshot.ReadAsync(
            path, RekallAgePersistedJson.MaximumDocumentBytes, cancellationToken).ConfigureAwait(false);
        var updated = loaded.Value with
        {
            Revision = loaded.Value.Revision + 1,
            BuildState = state,
            Frozen = frozen
        };
        var revision = await _modelStore.SaveIfRevisionAsync(
            projectRoot, updated, loaded.Revision, cancellationToken).ConfigureAwait(false);
        transaction.RecordResourcePreimage(path, existedBefore: true, before.Bytes);
        transaction.RecordChangedResource(path);
        return new(updated, revision);
    }

    public async ValueTask<RekallAgeModelAssetInspection> InspectAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        RekallAgeVersionedDocument<RekallAgeModelAssetDocument> loadedModel;
        try
        {
            loadedModel = await _modelStore.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(
                null,
                RekallAgeDocumentRevision.Missing,
                RekallAgeModelBuildState.Failed,
                null,
                null,
                false,
                null,
                [Diagnostic("REKALL_MODEL_ASSET_MISSING", "Error", $"Model Asset '{assetId}' was not found.", assetId)]);
        }

        var model = loadedModel.Value;
        if (model.Frozen || model.BuildState == RekallAgeModelBuildState.Frozen)
        {
            return await InspectFrozenAsync(
                projectRoot,
                assetId,
                loadedModel,
                cancellationToken).ConfigureAwait(false);
        }

        RekallAgeVersionedDocument<Rekall.Age.Modeling.Contracts.RekallAgeMeshAsset> source;
        try
        {
            source = await LoadSourceAsync(projectRoot, model.Source, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            var retainedOutput = await InspectOutputAsync(projectRoot, assetId, model.LastSuccessfulBuild, cancellationToken).ConfigureAwait(false);
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                null,
                null,
                retainedOutput.Exists,
                retainedOutput.Hash,
                [Diagnostic("REKALL_MODEL_SOURCE_MISSING", "Error", $"Linked mesh source '{model.Source.AssetId}' was not found; the last successful output was retained.", model.Source.AssetId)]);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            var retainedOutput = await InspectOutputAsync(projectRoot, assetId, model.LastSuccessfulBuild, cancellationToken).ConfigureAwait(false);
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                null,
                null,
                retainedOutput.Exists,
                retainedOutput.Hash,
                [Diagnostic("REKALL_MODEL_SOURCE_INVALID", "Error", $"Linked mesh source '{model.Source.AssetId}' is invalid: {error.Message}", model.Source.AssetId)]);
        }

        var manifest = model.LastSuccessfulBuild;
        if (model.BuildState == RekallAgeModelBuildState.Failed || manifest is null)
        {
            var retainedOutput = await InspectOutputAsync(projectRoot, assetId, manifest, cancellationToken).ConfigureAwait(false);
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                source.Revision,
                source.Value.Revision,
                retainedOutput.Exists,
                retainedOutput.Hash,
                [Diagnostic("REKALL_MODEL_LAST_BUILD_FAILED", "Error", "Model Asset has no usable successful build manifest.", assetId)]);
        }

        var finalPath = _outputStore.GetFinalPath(projectRoot, assetId, manifest.CompiledContentHash);
        var canonicalOutput = await InspectOutputAsync(projectRoot, assetId, manifest, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                Path.GetFullPath(Path.Combine(projectRoot, manifest.CompiledMeshPath)),
                Path.GetFullPath(finalPath),
                PathComparison))
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                source.Revision,
                source.Value.Revision,
                canonicalOutput.Exists,
                canonicalOutput.Hash,
                [Diagnostic("REKALL_MODEL_OUTPUT_PATH_MISMATCH", "Error", "The build manifest does not reference this Model Asset's canonical compiled output path.", manifest.CompiledMeshPath)]);
        }

        if (!canonicalOutput.Exists)
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                source.Revision,
                source.Value.Revision,
                false,
                null,
                [Diagnostic("REKALL_MODEL_OUTPUT_MISSING", "Error", "The last successful compiled Model Asset output is missing.", manifest.CompiledMeshPath)]);
        }

        var actualHash = canonicalOutput.Hash;
        try
        {
            actualHash ??= await _outputStore.HashAsync(projectRoot, assetId, manifest.CompiledContentHash, cancellationToken).ConfigureAwait(false);
            var actualOutput = await _outputStore.LoadAsync(projectRoot, assetId, manifest.CompiledContentHash, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualOutput.SourceAssetId, model.Source.AssetId, StringComparison.Ordinal)
                || actualOutput.SourceLogicalRevision != manifest.SourceLogicalRevision)
            {
                return Inspection(
                    loadedModel,
                    RekallAgeModelBuildState.Failed,
                    source.Revision,
                    source.Value.Revision,
                    true,
                    actualHash,
                    [Diagnostic("REKALL_MODEL_OUTPUT_PROVENANCE_INVALID", "Error", "Compiled output provenance does not match the Model Asset manifest.", manifest.CompiledMeshPath)]);
            }
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException)
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                source.Revision,
                source.Value.Revision,
                true,
                actualHash,
                [Diagnostic("REKALL_MODEL_OUTPUT_INVALID", "Error", $"Compiled output is invalid: {error.Message}", manifest.CompiledMeshPath)]);
        }

        if (!string.Equals(actualHash, manifest.CompiledContentHash, StringComparison.Ordinal))
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                source.Revision,
                source.Value.Revision,
                true,
                actualHash,
                [Diagnostic("REKALL_MODEL_OUTPUT_HASH_MISMATCH", "Error", "Compiled output hash does not match the Model Asset manifest.", manifest.CompiledMeshPath)]);
        }

        if (!string.Equals(source.Revision, manifest.SourceFileRevision, StringComparison.Ordinal)
            || source.Value.Revision != manifest.SourceLogicalRevision
            || !string.Equals(manifest.CompilerVersion, RekallAgeModelBuildManifest.CurrentCompilerVersion, StringComparison.Ordinal))
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Stale,
                source.Revision,
                source.Value.Revision,
                true,
                actualHash,
                [Diagnostic("REKALL_MODEL_SOURCE_STALE", "Warning", "Linked mesh source or compiler revision differs from the last successful build manifest; rebuild with the expected Model Asset file revision.", model.Source.AssetId)]);
        }

        return Inspection(
            loadedModel,
            RekallAgeModelBuildState.Current,
            source.Revision,
            source.Value.Revision,
            true,
            actualHash,
            []);
    }

    private async ValueTask<RekallAgeModelAssetInspection> InspectFrozenAsync(
        string projectRoot,
        string assetId,
        RekallAgeVersionedDocument<RekallAgeModelAssetDocument> loadedModel,
        CancellationToken cancellationToken)
    {
        var manifest = loadedModel.Value.LastSuccessfulBuild;
        var canonicalOutput = await InspectOutputAsync(projectRoot, assetId, manifest, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                null,
                null,
                canonicalOutput.Exists,
                canonicalOutput.Hash,
                [Diagnostic("REKALL_MODEL_LAST_BUILD_FAILED", "Error", "Frozen Model Asset has no usable successful build manifest.", assetId)]);
        }

        var finalPath = _outputStore.GetFinalPath(projectRoot, assetId, manifest.CompiledContentHash);
        if (!string.Equals(
                Path.GetFullPath(Path.Combine(projectRoot, manifest.CompiledMeshPath)),
                Path.GetFullPath(finalPath),
                PathComparison))
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                null,
                null,
                canonicalOutput.Exists,
                canonicalOutput.Hash,
                [Diagnostic("REKALL_MODEL_OUTPUT_PATH_MISMATCH", "Error", "The frozen build manifest does not reference this Model Asset's canonical compiled output path.", manifest.CompiledMeshPath)]);
        }

        if (!canonicalOutput.Exists)
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                null,
                null,
                false,
                null,
                [Diagnostic("REKALL_MODEL_OUTPUT_MISSING", "Error", "The frozen Model Asset's last successful compiled output is missing.", manifest.CompiledMeshPath)]);
        }

        if (canonicalOutput.Hash is null)
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                null,
                null,
                true,
                null,
                [Diagnostic("REKALL_MODEL_OUTPUT_INVALID", "Error", "The frozen Model Asset's compiled output could not be read and hashed.", manifest.CompiledMeshPath)]);
        }

        if (!string.Equals(canonicalOutput.Hash, manifest.CompiledContentHash, StringComparison.Ordinal))
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                null,
                null,
                true,
                canonicalOutput.Hash,
                [Diagnostic("REKALL_MODEL_OUTPUT_HASH_MISMATCH", "Error", "The frozen compiled output hash does not match its successful build manifest.", manifest.CompiledMeshPath)]);
        }

        try
        {
            var snapshot = await _outputStore.LoadAsync(
                projectRoot,
                assetId,
                manifest.CompiledContentHash,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(snapshot.SourceAssetId, loadedModel.Value.Source.AssetId, StringComparison.Ordinal)
                || snapshot.SourceLogicalRevision != manifest.SourceLogicalRevision)
            {
                return Inspection(
                    loadedModel,
                    RekallAgeModelBuildState.Failed,
                    null,
                    null,
                    true,
                    canonicalOutput.Hash,
                    [Diagnostic("REKALL_MODEL_OUTPUT_PROVENANCE_INVALID", "Error", "The frozen compiled output provenance does not agree with its source reference and successful manifest.", manifest.CompiledMeshPath)]);
            }
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException)
        {
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                null,
                null,
                true,
                canonicalOutput.Hash,
                [Diagnostic("REKALL_MODEL_OUTPUT_INVALID", "Error", $"The frozen compiled output is invalid: {error.Message}", manifest.CompiledMeshPath)]);
        }

        return Inspection(
            loadedModel,
            RekallAgeModelBuildState.Frozen,
            null,
            null,
            true,
            canonicalOutput.Hash,
            []);
    }

    private async ValueTask<RekallAgeVersionedDocument<Rekall.Age.Modeling.Contracts.RekallAgeMeshAsset>> LoadSourceAsync(
        string projectRoot,
        RekallAgeModelSourceReference source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != RekallAgeModelSourceKind.Mesh)
        {
            throw new RekallAgeModelPublishingException(
                "REKALL_MODEL_SOURCE_KIND_UNSUPPORTED",
                $"Model source kind '{source.Kind}' is not supported by the editable-mesh publisher.",
                source.AssetId);
        }

        return await _meshStore.LoadVersionedAsync(projectRoot, source.AssetId, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateRequest(RekallAgePublishModelRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AssetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source.AssetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedModelFileRevision);
        if (!RekallAgeDocumentRevision.IsValid(request.ExpectedModelFileRevision))
        {
            throw new ArgumentException(
                "Expected Model Asset file revision must be 'missing' or a lowercase SHA-256 token.",
                nameof(request));
        }
    }

    private static void RejectFrozen(RekallAgeModelAssetDocument model)
    {
        if (model.Frozen || model.BuildState == RekallAgeModelBuildState.Frozen)
        {
            throw new RekallAgeModelPublishingException(
                "REKALL_MODEL_FROZEN",
                $"Model Asset '{model.AssetId}' is frozen and cannot be rebuilt until it is explicitly unfrozen.",
                model.AssetId);
        }
    }

    private static async ValueTask<IReadOnlyList<ModelPublicationMutation>> CaptureMutationJournalAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var journal = new List<ModelPublicationMutation>(paths.Count);
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                journal.Add(new(Path.GetFullPath(path), null, RekallAgeDocumentRevision.Missing));
                continue;
            }

            try
            {
                var snapshot = await RekallAgeBoundedFileSnapshot.ReadAsync(
                    path,
                    RekallAgePersistedJson.MaximumDocumentBytes,
                    cancellationToken).ConfigureAwait(false);
                journal.Add(new(snapshot.Path, snapshot.Bytes, snapshot.Revision));
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                journal.Add(new(Path.GetFullPath(path), null, RekallAgeDocumentRevision.Missing));
            }
        }

        return journal;
    }

    private static void RequireMatchingPreimageRevision(
        string expectedRevision,
        ModelPublicationMutation mutation)
    {
        if (string.Equals(expectedRevision, mutation.BeforeRevision, StringComparison.Ordinal))
        {
            return;
        }

        throw new RekallAgeDocumentRevisionException(
            "REKALL_DOCUMENT_REVISION_CONFLICT",
            mutation.Path,
            $"Document '{mutation.Path}' changed before Model Asset publication: expected revision '{expectedRevision}', current revision '{mutation.BeforeRevision}'. Reload the document, reapply the semantic change, and retry.",
            expectedRevision,
            mutation.BeforeRevision);
    }

    private static async ValueTask<IReadOnlyList<Exception>> RestoreMutationsAsync(
        IReadOnlyList<ModelPublicationMutation> journal)
    {
        var rollbackErrors = new List<Exception>();
        foreach (var mutation in journal.Reverse().Where(item => item.Written))
        {
            try
            {
                if (mutation.BeforeBytes is null)
                {
                    await RekallAgeAtomicFile.DeleteIfRevisionAsync(
                        mutation.Path,
                        RekallAgePersistedJson.MaximumDocumentBytes,
                        mutation.AfterRevision!,
                        CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await RekallAgeAtomicFile.WriteAllBytesIfRevisionAsync(
                        mutation.Path,
                        mutation.BeforeBytes,
                        RekallAgePersistedJson.MaximumDocumentBytes,
                        mutation.AfterRevision!,
                        previousVersionPath: null,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception rollbackError)
            {
                rollbackErrors.Add(rollbackError);
            }
        }

        return rollbackErrors;
    }

    private static RekallAgeModelAssetInspection Inspection(
        RekallAgeVersionedDocument<RekallAgeModelAssetDocument> model,
        RekallAgeModelBuildState state,
        string? sourceRevision,
        long? sourceLogicalRevision,
        bool outputExists,
        string? actualOutputHash,
        IReadOnlyList<RekallAgeModelBuildDiagnostic> diagnostics) =>
        new(
            model.Value,
            model.Revision,
            state,
            sourceRevision,
            sourceLogicalRevision,
            outputExists,
            actualOutputHash,
            diagnostics.Take(MaximumDiagnostics).ToArray());

    private static RekallAgeModelBuildDiagnostic Diagnostic(string code, string severity, string message, string target) =>
        new(code, severity, message, target);

    private async ValueTask<(bool Exists, string? Hash)> InspectOutputAsync(
        string projectRoot,
        string assetId,
        RekallAgeModelBuildManifest? manifest,
        CancellationToken cancellationToken)
    {
        if (manifest is null || !File.Exists(_outputStore.GetFinalPath(projectRoot, assetId, manifest.CompiledContentHash)))
        {
            return (false, null);
        }

        try
        {
            return (true, await _outputStore.HashAsync(projectRoot, assetId, manifest.CompiledContentHash, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return (true, null);
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string ToProjectRelativePath(string projectRoot, string path) =>
        Path.GetRelativePath(Path.GetFullPath(projectRoot), Path.GetFullPath(path)).Replace('\\', '/');

    private sealed class ModelPublicationMutation(
        string path,
        byte[]? beforeBytes,
        string beforeRevision)
    {
        public string Path { get; } = path;

        public byte[]? BeforeBytes { get; } = beforeBytes;

        public string BeforeRevision { get; } = beforeRevision;

        public string? AfterRevision { get; private set; }

        public bool Written => AfterRevision is not null;

        public void RecordWrite(string afterRevision)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(afterRevision);
            AfterRevision = afterRevision;
        }
    }
}
