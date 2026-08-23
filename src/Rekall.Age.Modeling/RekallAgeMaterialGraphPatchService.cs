using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed record RekallAgeMaterialGraphPatchResult(RekallAgeMaterialGraphAsset Graph, string BeforeFileRevision,
    string AfterFileRevision, RekallAgeMaterialGraphValidationReport Validation, int AppliedOperationCount);

public sealed class RekallAgeMaterialGraphPatchService
{
    private readonly RekallAgeMaterialGraphAssetStore _store = new();
    private readonly RekallAgeMaterialGraphValidator _validator = new(RekallAgeMaterialNodeCatalog.CreateDefault());
    public async ValueTask<RekallAgeMaterialGraphPatchResult> ApplyAsync(string root, string id, string expectedRevision,
        RekallAgeMaterialGraphPatch patch, RekallAgeTransaction transaction, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(patch); ArgumentNullException.ThrowIfNull(transaction);
        if (patch.Operations is null || patch.Operations.Count is < 1 or > 256) throw new InvalidDataException("REKALL_MATERIAL_GRAPH_PATCH_BOUNDS: A patch requires 1-256 operations.");
        var loaded = await _store.LoadVersionedAsync(root, id, token).ConfigureAwait(false);
        if (loaded.Revision != expectedRevision) throw new RekallAgeDocumentRevisionException("REKALL_DOCUMENT_REVISION_CONFLICT", _store.GetGraphPath(root, id), $"Material graph '{id}' changed.", expectedRevision, loaded.Revision);
        var nodes = loaded.Value.Nodes.Select(Clone).ToList(); var links = loaded.Value.Links.ToList(); var output = loaded.Value.Output; var exposed = loaded.Value.ExposedParameters.Select(Clone).ToList();
        foreach (var operation in patch.Operations)
        {
            token.ThrowIfCancellationRequested();
            switch (operation.Kind)
            {
                case RekallAgeMaterialGraphPatchKind.AddNode: nodes.Add(Clone(operation.Node ?? throw Malformed(operation.Kind))); break;
                case RekallAgeMaterialGraphPatchKind.RemoveNode:
                    RequireTarget(operation); nodes.RemoveAll(item => item.NodeId == operation.TargetId); links.RemoveAll(item => item.FromNodeId == operation.TargetId || item.ToNodeId == operation.TargetId); exposed.RemoveAll(item => item.NodeId == operation.TargetId); break;
                case RekallAgeMaterialGraphPatchKind.SetParameter:
                    RequireTarget(operation); if (string.IsNullOrWhiteSpace(operation.ParameterId)) throw Malformed(operation.Kind);
                    var index = nodes.FindIndex(item => item.NodeId == operation.TargetId); if (index < 0) throw Malformed(operation.Kind);
                    var parameters = (JsonObject)nodes[index].Parameters.DeepClone(); parameters[operation.ParameterId] = operation.Value?.DeepClone(); nodes[index] = nodes[index] with { Parameters = parameters }; break;
                case RekallAgeMaterialGraphPatchKind.AddLink: links.Add(operation.Link ?? throw Malformed(operation.Kind)); break;
                case RekallAgeMaterialGraphPatchKind.RemoveLink: RequireTarget(operation); links.RemoveAll(item => item.LinkId == operation.TargetId); break;
                case RekallAgeMaterialGraphPatchKind.SetOutput: output = operation.Output ?? throw Malformed(operation.Kind); break;
                case RekallAgeMaterialGraphPatchKind.ExposeParameter:
                    var parameter = Clone(operation.ExposedParameter ?? throw Malformed(operation.Kind)); exposed.RemoveAll(item => item.Name == parameter.Name); exposed.Add(parameter); break;
                case RekallAgeMaterialGraphPatchKind.RemoveExposedParameter: RequireTarget(operation); exposed.RemoveAll(item => item.Name == operation.TargetId); break;
                default: throw Malformed(operation.Kind);
            }
        }
        var candidate = loaded.Value with { Revision = loaded.Value.Revision + 1, Nodes = nodes, Links = links, Output = output, ExposedParameters = exposed };
        var validation = _validator.Validate(candidate); if (!validation.IsValid) throw new InvalidDataException("REKALL_MATERIAL_GRAPH_PATCH_INVALID: " + string.Join(", ", validation.Diagnostics.Where(item => item.Severity == RekallAgeModelingDiagnosticSeverity.Error).Select(item => item.Code).Distinct(StringComparer.Ordinal)));
        var path = _store.GetGraphPath(root, id); transaction.CaptureResourcePreimage(path);
        var after = await _store.SaveIfRevisionAsync(root, candidate, expectedRevision, token).ConfigureAwait(false); transaction.RecordChangedResource(path);
        return new(candidate, loaded.Revision, after, validation, patch.Operations.Count);
    }
    private static RekallAgeMaterialGraphNode Clone(RekallAgeMaterialGraphNode item) => item with { Parameters = (JsonObject)item.Parameters.DeepClone() };
    private static RekallAgeMaterialGraphExposedParameter Clone(RekallAgeMaterialGraphExposedParameter item) => item with { DefaultValue = item.DefaultValue?.DeepClone() };
    private static void RequireTarget(RekallAgeMaterialGraphPatchOperation item) { if (string.IsNullOrWhiteSpace(item.TargetId)) throw Malformed(item.Kind); }
    private static InvalidDataException Malformed(RekallAgeMaterialGraphPatchKind kind) => new($"REKALL_MATERIAL_GRAPH_PATCH_OPERATION_INVALID: Material graph operation '{kind}' is malformed.");
}
