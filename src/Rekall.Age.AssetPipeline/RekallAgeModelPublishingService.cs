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

public sealed class RekallAgeModelPublishingService : IRekallAgeModelAssetHealthInspector
{
    private const int MaximumDiagnostics = 16;
    private readonly RekallAgeMeshAssetStore _meshStore;
    private readonly RekallAgeMeshCompiler _compiler;
    private readonly RekallAgeModelAssetStore _modelStore;
    private readonly RekallAgePublishedModelOutputStore _outputStore;
    private readonly RekallAgeAssetCatalogStore _catalogStore;

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
        RekallAgeAssetCatalogStore catalogStore)
    {
        _meshStore = meshStore ?? throw new ArgumentNullException(nameof(meshStore));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _outputStore = outputStore ?? throw new ArgumentNullException(nameof(outputStore));
        _catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
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
        var catalog = await _catalogStore.LoadVersionedAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        RekallAgeStagedModelOutput? staged = null;
        IReadOnlyList<ModelPublicationMutation>? mutationJournal = null;
        try
        {
            staged = await _outputStore.WriteStagedAsync(
                projectRoot,
                request.AssetId,
                compiled,
                cancellationToken).ConfigureAwait(false);

            var outputPath = _outputStore.GetFinalPath(projectRoot, request.AssetId);
            var catalogPath = _catalogStore.GetCatalogPath(projectRoot);
            mutationJournal = await CaptureMutationJournalAsync(
                transaction,
                [outputPath, modelPath, catalogPath],
                cancellationToken).ConfigureAwait(false);
            var outputMutation = mutationJournal[0];
            var modelMutation = mutationJournal[1];
            var catalogMutation = mutationJournal[2];
            RequireMatchingPreimageRevision(request.ExpectedModelFileRevision, modelMutation);
            RequireMatchingPreimageRevision(loadedModelRevision, modelMutation);
            RequireMatchingPreimageRevision(catalog.Revision, catalogMutation);

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

            outputMutation.RecordWrite(await _outputStore.CommitStagedIfRevisionAsync(
                projectRoot,
                staged,
                outputMutation.BeforeRevision,
                cancellationToken).ConfigureAwait(false));
            var modelRevision = await _modelStore.SaveIfRevisionAsync(
                projectRoot,
                model,
                modelMutation.BeforeRevision,
                cancellationToken).ConfigureAwait(false);
            modelMutation.RecordWrite(modelRevision);
            catalogMutation.RecordWrite(await _catalogStore.SaveIfRevisionAsync(
                projectRoot,
                catalog.Value.AddOrReplace(catalogAsset),
                catalogMutation.BeforeRevision,
                cancellationToken).ConfigureAwait(false));

            transaction.RecordChangedResource(outputPath);
            transaction.RecordChangedResource(modelPath);
            transaction.RecordChangedResource(catalogPath);
            return new(model, modelRevision, outputPath, staged.ContentHash);
        }
        catch (Exception publicationError) when (mutationJournal?.Any(item => item.Written) == true)
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
        var current = await _modelStore.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        RejectFrozen(current.Value);
        return await PublishAsync(
            projectRoot,
            new(current.Value.AssetId, current.Value.DisplayName, current.Value.Source, expectedModelFileRevision),
            transaction,
            cancellationToken).ConfigureAwait(false);
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
            var retainedOutput = await InspectOutputAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
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
            var retainedOutput = await InspectOutputAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
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
            var retainedOutput = await InspectOutputAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
            return Inspection(
                loadedModel,
                RekallAgeModelBuildState.Failed,
                source.Revision,
                source.Value.Revision,
                retainedOutput.Exists,
                retainedOutput.Hash,
                [Diagnostic("REKALL_MODEL_LAST_BUILD_FAILED", "Error", "Model Asset has no usable successful build manifest.", assetId)]);
        }

        var finalPath = _outputStore.GetFinalPath(projectRoot, assetId);
        var canonicalOutput = await InspectOutputAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
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
            actualHash ??= await _outputStore.HashAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
            var actualOutput = await _outputStore.LoadAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
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
        var canonicalOutput = await InspectOutputAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
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

        var finalPath = _outputStore.GetFinalPath(projectRoot, assetId);
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
        RekallAgeTransaction transaction,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var journal = new List<ModelPublicationMutation>(paths.Count);
        foreach (var path in paths)
        {
            transaction.CaptureResourcePreimage(path);
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
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_outputStore.GetFinalPath(projectRoot, assetId)))
        {
            return (false, null);
        }

        try
        {
            return (true, await _outputStore.HashAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false));
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
