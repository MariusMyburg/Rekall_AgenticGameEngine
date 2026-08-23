using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModelingGraphBakeException : Exception
{
    public RekallAgeModelingGraphBakeException(string code, string message, RekallAgeModelingGraphEvaluationReport? evaluation = null)
        : base(message)
    {
        Code = code;
        Evaluation = evaluation;
    }

    public string Code { get; }
    public RekallAgeModelingGraphEvaluationReport? Evaluation { get; }
}

public sealed record RekallAgeModelingGraphBakeResult(
    string GraphAssetId,
    long GraphLogicalRevision,
    string OutputName,
    RekallAgeMeshAsset Mesh,
    string BeforeFileRevision,
    string AfterFileRevision,
    RekallAgeModelingGraphEvaluationReport Evaluation);

public sealed class RekallAgeModelingGraphBakeService
{
    private readonly RekallAgeModelingGraphEvaluator _evaluator;
    private readonly RekallAgeMeshAssetStore _meshStore;

    public RekallAgeModelingGraphBakeService(
        RekallAgeModelingGraphEvaluator? evaluator = null,
        RekallAgeMeshAssetStore? meshStore = null)
    {
        _evaluator = evaluator ?? new RekallAgeModelingGraphEvaluator();
        _meshStore = meshStore ?? new RekallAgeMeshAssetStore();
    }

    public async ValueTask<RekallAgeModelingGraphBakeResult> BakeAsync(
        string projectRoot,
        RekallAgeModelingGraphAsset graph,
        string outputName,
        string targetMeshAssetId,
        string expectedTargetRevision,
        RekallAgeModelingEvaluationBudget budget,
        RekallAgeModelingEvaluationContext evaluationContext,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTargetRevision);
        var evaluation = await _evaluator.EvaluateAsync(
            graph, [outputName], budget, evaluationContext, cancellationToken).ConfigureAwait(false);
        if (!evaluation.Succeeded || !evaluation.Outputs.TryGetValue(outputName, out var evaluatedMesh))
        {
            throw new RekallAgeModelingGraphBakeException(
                "REKALL_MODELING_GRAPH_BAKE_EVALUATION_FAILED",
                $"Graph output '{outputName}' could not be evaluated; no mesh was published.",
                evaluation);
        }

        var path = _meshStore.GetMeshPath(projectRoot, targetMeshAssetId);
        long logicalRevision;
        if (File.Exists(path))
        {
            var current = await _meshStore.LoadVersionedAsync(projectRoot, targetMeshAssetId, cancellationToken).ConfigureAwait(false);
            if (!current.Revision.Equals(expectedTargetRevision, StringComparison.Ordinal))
            {
                throw new RekallAgeDocumentRevisionException(
                    "REKALL_DOCUMENT_REVISION_CONFLICT",
                    path,
                    $"Target mesh '{targetMeshAssetId}' changed: expected revision '{expectedTargetRevision}', current revision '{current.Revision}'.",
                    expectedTargetRevision,
                    current.Revision);
            }
            logicalRevision = checked(current.Value.Revision + 1);
        }
        else
        {
            if (!expectedTargetRevision.Equals(RekallAgeDocumentRevision.Missing, StringComparison.Ordinal))
            {
                throw new RekallAgeDocumentRevisionException(
                    "REKALL_DOCUMENT_REVISION_CONFLICT",
                    path,
                    $"Target mesh '{targetMeshAssetId}' does not exist; expected revision must identify a missing document.",
                    expectedTargetRevision,
                    RekallAgeDocumentRevision.Missing);
            }
            logicalRevision = 1;
        }

        var mesh = evaluatedMesh with
        {
            AssetId = targetMeshAssetId,
            Name = targetMeshAssetId,
            Revision = logicalRevision
        };
        transaction.CaptureResourcePreimage(path);
        var afterRevision = await _meshStore.SaveIfRevisionAsync(
            projectRoot, mesh, expectedTargetRevision, cancellationToken).ConfigureAwait(false);
        transaction.RecordChangedResource(path);
        return new(
            graph.AssetId,
            graph.Revision,
            outputName,
            mesh,
            expectedTargetRevision,
            afterRevision,
            evaluation);
    }
}
