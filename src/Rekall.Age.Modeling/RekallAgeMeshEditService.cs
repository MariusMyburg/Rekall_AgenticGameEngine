using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed record RekallAgeMeshEditExecution(
    string AssetId,
    bool Persisted,
    string BeforeFileRevision,
    string AfterFileRevision,
    RekallAgeMeshOperationResult Operation);

public sealed record RekallAgeMeshBatchExecution(
    string AssetId,
    bool Persisted,
    string BeforeFileRevision,
    string AfterFileRevision,
    long BeforeLogicalRevision,
    long AfterLogicalRevision,
    RekallAgeMeshAsset Mesh,
    IReadOnlyList<RekallAgeMeshOperationResult> Steps,
    RekallAgeMeshValidationReport Validation);

public sealed class RekallAgeMeshEditService
{
    public const int MaximumBatchOperations = 128;
    private readonly RekallAgeMeshAssetStore _store;
    private readonly RekallAgeMeshOperationExecutor _executor;
    private readonly RekallAgeMeshValidator _validator;

    public RekallAgeMeshEditService(
        RekallAgeMeshAssetStore? store = null,
        RekallAgeMeshOperationExecutor? executor = null,
        RekallAgeMeshValidator? validator = null)
    {
        _store = store ?? new RekallAgeMeshAssetStore();
        _executor = executor ?? new RekallAgeMeshOperationExecutor();
        _validator = validator ?? new RekallAgeMeshValidator();
    }

    public async ValueTask<RekallAgeMeshEditExecution> PreviewAsync(
        string projectRoot,
        string assetId,
        string expectedRevision,
        RekallAgeMeshOperationRequest operation,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var loaded = await LoadExpectedAsync(projectRoot, assetId, expectedRevision, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var result = _executor.Execute(loaded.Value, operation);
        return new RekallAgeMeshEditExecution(
            assetId,
            false,
            loaded.Revision,
            loaded.Revision,
            result);
    }

    public async ValueTask<RekallAgeMeshEditExecution> ApplyAsync(
        string projectRoot,
        string assetId,
        string expectedRevision,
        RekallAgeMeshOperationRequest operation,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var loaded = await LoadExpectedAsync(projectRoot, assetId, expectedRevision, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var result = _executor.Execute(loaded.Value, operation);
        var path = _store.GetMeshPath(projectRoot, assetId);
        transaction.CaptureResourcePreimage(path);
        var afterFileRevision = await _store.SaveIfRevisionAsync(
            projectRoot,
            result.Mesh,
            loaded.Revision,
            cancellationToken).ConfigureAwait(false);
        transaction.RecordChangedResource(path);
        return new RekallAgeMeshEditExecution(
            assetId,
            true,
            loaded.Revision,
            afterFileRevision,
            result);
    }

    public async ValueTask<RekallAgeMeshBatchExecution> PreviewBatchAsync(
        string projectRoot,
        string assetId,
        string expectedRevision,
        IReadOnlyList<RekallAgeMeshOperationRequest> operations,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var loaded = await LoadExpectedAsync(projectRoot, assetId, expectedRevision, cancellationToken).ConfigureAwait(false);
        return ExecuteBatch(assetId, loaded, operations, persisted: false, loaded.Revision, cancellationToken);
    }

    public async ValueTask<RekallAgeMeshBatchExecution> ApplyBatchAsync(
        string projectRoot,
        string assetId,
        string expectedRevision,
        IReadOnlyList<RekallAgeMeshOperationRequest> operations,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var loaded = await LoadExpectedAsync(projectRoot, assetId, expectedRevision, cancellationToken).ConfigureAwait(false);
        var candidate = ExecuteBatch(assetId, loaded, operations, persisted: false, loaded.Revision, cancellationToken);
        var path = _store.GetMeshPath(projectRoot, assetId);
        transaction.CaptureResourcePreimage(path);
        var afterFileRevision = await _store.SaveIfRevisionAsync(
            projectRoot,
            candidate.Mesh,
            loaded.Revision,
            cancellationToken).ConfigureAwait(false);
        transaction.RecordChangedResource(path);
        return candidate with
        {
            Persisted = true,
            AfterFileRevision = afterFileRevision
        };
    }

    private RekallAgeMeshBatchExecution ExecuteBatch(
        string assetId,
        RekallAgeVersionedDocument<RekallAgeMeshAsset> loaded,
        IReadOnlyList<RekallAgeMeshOperationRequest> operations,
        bool persisted,
        string afterFileRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count < 1 || operations.Count > MaximumBatchOperations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operations),
                operations.Count,
                $"Mesh operation batch requires 1-{MaximumBatchOperations} operations.");
        }

        var sourceLogicalRevision = loaded.Value.Revision;
        var current = loaded.Value;
        var steps = new List<RekallAgeMeshOperationResult>(operations.Count);
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = current with { Revision = sourceLogicalRevision };
            var step = _executor.Execute(current, operation);
            steps.Add(step);
            current = step.Mesh;
        }

        current = current with { Revision = checked(sourceLogicalRevision + 1) };
        var validation = _validator.Validate(current);
        if (!validation.IsValid)
        {
            throw new RekallAgeMeshOperationException(
                "REKALL_MESH_BATCH_OUTPUT_INVALID",
                "Mesh operation batch produced an invalid final candidate.");
        }
        return new RekallAgeMeshBatchExecution(
            assetId,
            persisted,
            loaded.Revision,
            afterFileRevision,
            sourceLogicalRevision,
            current.Revision,
            current,
            steps,
            validation);
    }

    private async ValueTask<RekallAgeVersionedDocument<RekallAgeMeshAsset>> LoadExpectedAsync(
        string projectRoot,
        string assetId,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        var loaded = await _store.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(loaded.Revision, expectedRevision, StringComparison.Ordinal))
        {
            var path = _store.GetMeshPath(projectRoot, assetId);
            throw new RekallAgeDocumentRevisionException(
                "REKALL_DOCUMENT_REVISION_CONFLICT",
                path,
                $"Mesh asset '{assetId}' changed: expected revision '{expectedRevision}', current revision '{loaded.Revision}'. Reload, reapply the semantic edit, and retry.",
                expectedRevision,
                loaded.Revision);
        }
        return loaded;
    }
}
