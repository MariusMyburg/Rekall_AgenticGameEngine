using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModelingGraphPatchException : Exception
{
    public RekallAgeModelingGraphPatchException(
        string code,
        string message,
        IReadOnlyList<RekallAgeModelingGraphDiagnostic>? diagnostics = null)
        : base(message)
    {
        Code = code;
        Diagnostics = diagnostics ?? [];
    }

    public string Code { get; }

    public IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics { get; }
}

public sealed record RekallAgeModelingGraphPatchExecution(
    RekallAgeModelingGraphAsset Graph,
    string BeforeFileRevision,
    string AfterFileRevision,
    RekallAgeModelingGraphValidationReport Validation,
    int AppliedOperationCount);

public sealed class RekallAgeModelingGraphPatchService
{
    public const int MaximumPatchOperations = 256;
    private readonly RekallAgeModelingGraphAssetStore _store = new();
    private readonly RekallAgeModelingGraphValidator _validator =
        new(RekallAgeModelingNodeCatalog.CreateDefault());

    public async ValueTask<RekallAgeModelingGraphPatchExecution> ApplyAsync(
        string projectRoot,
        string assetId,
        string expectedRevision,
        RekallAgeModelingGraphPatch patch,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(transaction);
        if (patch.Operations is null || patch.Operations.Count is < 1 or > MaximumPatchOperations)
        {
            throw new RekallAgeModelingGraphPatchException(
                "REKALL_MODELING_GRAPH_PATCH_BOUNDS",
                $"A graph patch requires 1-{MaximumPatchOperations} operations.");
        }
        var loaded = await _store.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        if (!loaded.Revision.Equals(expectedRevision, StringComparison.Ordinal))
        {
            throw new RekallAgeDocumentRevisionException(
                "REKALL_DOCUMENT_REVISION_CONFLICT",
                _store.GetGraphPath(projectRoot, assetId),
                $"Modelling graph '{assetId}' changed: expected revision '{expectedRevision}', current revision '{loaded.Revision}'.",
                expectedRevision,
                loaded.Revision);
        }

        var nodes = loaded.Value.Nodes.Select(Clone).ToList();
        var links = loaded.Value.Links.ToList();
        var outputs = loaded.Value.Outputs.ToList();
        var exposed = loaded.Value.ExposedParameters.Select(Clone).ToList();
        foreach (var operation in patch.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(operation, nodes, links, outputs, exposed);
        }
        var candidate = loaded.Value with
        {
            Revision = checked(loaded.Value.Revision + 1),
            Nodes = nodes,
            Links = links,
            Outputs = outputs,
            ExposedParameters = exposed
        };
        var validation = _validator.Validate(candidate);
        if (!validation.IsValid)
        {
            throw new RekallAgeModelingGraphPatchException(
                "REKALL_MODELING_GRAPH_PATCH_INVALID",
                "Graph patch produced an invalid candidate; nothing was published.",
                validation.Diagnostics);
        }

        var path = _store.GetGraphPath(projectRoot, assetId);
        transaction.CaptureResourcePreimage(path);
        var afterRevision = await _store.SaveIfRevisionAsync(
            projectRoot, candidate, expectedRevision, cancellationToken).ConfigureAwait(false);
        transaction.RecordChangedResource(path);
        return new(candidate, loaded.Revision, afterRevision, validation, patch.Operations.Count);
    }

    private static void Apply(
        RekallAgeModelingGraphPatchOperation operation,
        List<RekallAgeModelingGraphNode> nodes,
        List<RekallAgeModelingGraphLink> links,
        List<RekallAgeModelingGraphOutput> outputs,
        List<RekallAgeModelingGraphExposedParameter> exposed)
    {
        switch (operation.Kind)
        {
            case RekallAgeModelingGraphPatchKind.AddNode:
                nodes.Add(Clone(operation.Node ?? throw Malformed(operation.Kind, "node")));
                break;
            case RekallAgeModelingGraphPatchKind.RemoveNode:
                RequireTarget(operation);
                nodes.RemoveAll(node => node.NodeId == operation.TargetId);
                links.RemoveAll(link => link.FromNodeId == operation.TargetId || link.ToNodeId == operation.TargetId);
                outputs.RemoveAll(output => output.NodeId == operation.TargetId);
                exposed.RemoveAll(parameter => parameter.NodeId == operation.TargetId);
                break;
            case RekallAgeModelingGraphPatchKind.SetParameter:
                RequireTarget(operation);
                if (string.IsNullOrWhiteSpace(operation.ParameterId)) throw Malformed(operation.Kind, "parameterId");
                var index = nodes.FindIndex(node => node.NodeId == operation.TargetId);
                if (index < 0) throw Malformed(operation.Kind, "existing target node");
                var parameters = (JsonObject)nodes[index].Parameters.DeepClone();
                parameters[operation.ParameterId] = operation.Value?.DeepClone();
                nodes[index] = nodes[index] with { Parameters = parameters };
                break;
            case RekallAgeModelingGraphPatchKind.AddLink:
                links.Add(operation.Link ?? throw Malformed(operation.Kind, "link"));
                break;
            case RekallAgeModelingGraphPatchKind.RemoveLink:
                RequireTarget(operation);
                links.RemoveAll(link => link.LinkId == operation.TargetId);
                break;
            case RekallAgeModelingGraphPatchKind.SetOutput:
                var output = operation.Output ?? throw Malformed(operation.Kind, "output");
                outputs.RemoveAll(item => item.Name == output.Name);
                outputs.Add(output);
                break;
            case RekallAgeModelingGraphPatchKind.RemoveOutput:
                RequireTarget(operation);
                outputs.RemoveAll(outputItem => outputItem.Name == operation.TargetId);
                break;
            case RekallAgeModelingGraphPatchKind.ExposeParameter:
                var parameter = Clone(operation.ExposedParameter ?? throw Malformed(operation.Kind, "exposedParameter"));
                exposed.RemoveAll(item => item.Name == parameter.Name);
                exposed.Add(parameter);
                break;
            case RekallAgeModelingGraphPatchKind.RemoveExposedParameter:
                RequireTarget(operation);
                exposed.RemoveAll(item => item.Name == operation.TargetId);
                break;
            default:
                throw Malformed(operation.Kind, "supported kind");
        }
    }

    private static RekallAgeModelingGraphNode Clone(RekallAgeModelingGraphNode node) =>
        node with { Parameters = (JsonObject)node.Parameters.DeepClone() };

    private static RekallAgeModelingGraphExposedParameter Clone(RekallAgeModelingGraphExposedParameter parameter) =>
        parameter with { DefaultValue = parameter.DefaultValue?.DeepClone() };

    private static void RequireTarget(RekallAgeModelingGraphPatchOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.TargetId)) throw Malformed(operation.Kind, "targetId");
    }

    private static RekallAgeModelingGraphPatchException Malformed(RekallAgeModelingGraphPatchKind kind, string field) =>
        new("REKALL_MODELING_GRAPH_PATCH_OPERATION_INVALID", $"Patch operation '{kind}' requires {field}.");
}
