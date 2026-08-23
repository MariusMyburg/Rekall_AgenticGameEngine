using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModifierStackAssetStore
{
    private const string Suffix = ".age.modifier-stack.json";
    private static readonly JsonSerializerOptions JsonOptions = new(RekallAgeModelingJson.Options) { MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth };
    private readonly RekallAgeModifierStackValidator _validator = new();
    public string GetStackPath(string root, string id) { ArgumentException.ThrowIfNullOrWhiteSpace(root); ValidateId(id); return Path.Combine(Path.GetFullPath(root), "Modeling", "ModifierStacks", id + Suffix); }
    public string GetRecoveryPath(string root, string id) => RekallAgeDocumentRecoveryStore.GetPreviousPath(root, GetStackPath(root, id));
    public async ValueTask<string> SaveIfRevisionAsync(string root, RekallAgeModifierStackAsset stack, string expectedRevision, CancellationToken token)
    {
        Validate(stack, stack.AssetId); var path = GetStackPath(root, stack.AssetId);
        if (File.Exists(path))
        {
            var current = await LoadVersionedAsync(root, stack.AssetId, token).ConfigureAwait(false);
            if (current.Revision == expectedRevision && stack.Revision != current.Value.Revision + 1) throw new InvalidDataException($"REKALL_MODIFIER_STACK_LOGICAL_REVISION_INVALID: Revision must advance from {current.Value.Revision} to {current.Value.Revision + 1}.");
        }
        else if (stack.Revision != 1) throw new InvalidDataException("REKALL_MODIFIER_STACK_LOGICAL_REVISION_INVALID: New stacks start at revision 1.");
        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(path, Serialize(stack), RekallAgeDocumentSchemaProbe.MaximumDocumentBytes, expectedRevision, GetRecoveryPath(root, stack.AssetId), token).ConfigureAwait(false);
    }
    public async ValueTask<RekallAgeVersionedDocument<RekallAgeModifierStackAsset>> LoadVersionedAsync(string root, string id, CancellationToken token)
    {
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(GetStackPath(root, id), "modifier stack asset", RekallAgeModifierStackAsset.CurrentSchemaVersion, token).ConfigureAwait(false);
        var stack = snapshot.Deserialize<RekallAgeModifierStackAsset>(JsonOptions); Validate(stack, id); return new(stack, snapshot.File.Revision);
    }
    public string Serialize(RekallAgeModifierStackAsset stack) => JsonSerializer.Serialize(stack with { SchemaVersion = RekallAgeModifierStackAsset.CurrentSchemaVersion }, JsonOptions) + Environment.NewLine;
    private void Validate(RekallAgeModifierStackAsset stack, string id)
    {
        if (stack.AssetId != id) throw new InvalidDataException("REKALL_MODIFIER_STACK_ASSET_ID_MISMATCH: Document and requested IDs differ.");
        var diagnostics = _validator.Validate(stack); if (diagnostics.Count > 0) throw new InvalidDataException("Modifier stack failed strict validation: " + string.Join(", ", diagnostics.Select(item => item.Code).Distinct(StringComparer.Ordinal)));
    }
    private static void ValidateId(string id) { if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !char.IsAsciiLetterOrDigit(id[0]) || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_')) throw new ArgumentException("Modifier stack asset ID is unsafe.", nameof(id)); }
}

public sealed record RekallAgeModifierStackPatchResult(RekallAgeModifierStackAsset Stack, string BeforeFileRevision, string AfterFileRevision, int AppliedOperationCount);
public sealed class RekallAgeModifierStackPatchService
{
    private readonly RekallAgeModifierStackAssetStore _store = new(); private readonly RekallAgeModifierStackValidator _validator = new();
    public async ValueTask<RekallAgeModifierStackPatchResult> ApplyAsync(string root, string id, string expectedRevision,
        RekallAgeModifierStackPatch patch, RekallAgeTransaction transaction, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(patch); ArgumentNullException.ThrowIfNull(transaction);
        if (patch.Operations is null || patch.Operations.Count is < 1 or > 256) throw new InvalidDataException("REKALL_MODIFIER_STACK_PATCH_BOUNDS: A patch requires 1-256 operations.");
        var loaded = await _store.LoadVersionedAsync(root, id, token).ConfigureAwait(false);
        if (loaded.Revision != expectedRevision) throw new RekallAgeDocumentRevisionException("REKALL_DOCUMENT_REVISION_CONFLICT", _store.GetStackPath(root, id), $"Modifier stack '{id}' changed.", expectedRevision, loaded.Revision);
        var modifiers = loaded.Value.Modifiers.Select(Clone).ToList(); var sourceId = loaded.Value.SourceMeshAssetId; var sourceRevision = loaded.Value.SourceMeshFileRevision;
        foreach (var operation in patch.Operations)
        {
            token.ThrowIfCancellationRequested(); var index = operation.TargetId is null ? -1 : modifiers.FindIndex(item => item.ModifierId == operation.TargetId);
            switch (operation.Kind)
            {
                case RekallAgeModifierStackPatchKind.Add:
                    var added = Clone(operation.Modifier ?? throw Malformed(operation.Kind)); var insertion = operation.NewIndex ?? modifiers.Count;
                    if (insertion < 0 || insertion > modifiers.Count) throw Malformed(operation.Kind); modifiers.Insert(insertion, added); break;
                case RekallAgeModifierStackPatchKind.Remove:
                    if (index < 0) throw Malformed(operation.Kind); modifiers.RemoveAt(index); break;
                case RekallAgeModifierStackPatchKind.Move:
                    if (index < 0 || operation.NewIndex is null || operation.NewIndex < 0 || operation.NewIndex >= modifiers.Count) throw Malformed(operation.Kind);
                    var moved = modifiers[index]; modifiers.RemoveAt(index); modifiers.Insert(operation.NewIndex.Value, moved); break;
                case RekallAgeModifierStackPatchKind.Configure:
                    if (index < 0 || operation.Parameters is null) throw Malformed(operation.Kind); modifiers[index] = modifiers[index] with { Parameters = (JsonObject)operation.Parameters.DeepClone() }; break;
                case RekallAgeModifierStackPatchKind.SetEnabled:
                    if (index < 0 || operation.Enabled is null) throw Malformed(operation.Kind); modifiers[index] = modifiers[index] with { Enabled = operation.Enabled.Value }; break;
                case RekallAgeModifierStackPatchKind.SetSource:
                    if (string.IsNullOrWhiteSpace(operation.SourceMeshAssetId) || string.IsNullOrWhiteSpace(operation.SourceMeshFileRevision)) throw Malformed(operation.Kind);
                    sourceId = operation.SourceMeshAssetId; sourceRevision = operation.SourceMeshFileRevision; break;
                default: throw Malformed(operation.Kind);
            }
        }
        var candidate = loaded.Value with { Revision = loaded.Value.Revision + 1, SourceMeshAssetId = sourceId, SourceMeshFileRevision = sourceRevision, Modifiers = modifiers };
        var diagnostics = _validator.Validate(candidate); if (diagnostics.Count > 0) throw new InvalidDataException("REKALL_MODIFIER_STACK_PATCH_INVALID: " + string.Join(", ", diagnostics.Select(item => item.Code)));
        var path = _store.GetStackPath(root, id); transaction.CaptureResourcePreimage(path);
        var after = await _store.SaveIfRevisionAsync(root, candidate, expectedRevision, token).ConfigureAwait(false); transaction.RecordChangedResource(path);
        return new(candidate, loaded.Revision, after, patch.Operations.Count);
    }
    private static RekallAgeModifierInstance Clone(RekallAgeModifierInstance item) => item with { Parameters = (JsonObject)item.Parameters.DeepClone() };
    private static InvalidDataException Malformed(RekallAgeModifierStackPatchKind kind) => new($"REKALL_MODIFIER_STACK_PATCH_OPERATION_INVALID: Modifier patch operation '{kind}' is malformed.");
}

public sealed class RekallAgeModifierStackEvaluationService
{
    private readonly RekallAgeModifierStackAssetStore _stackStore = new(); private readonly RekallAgeMeshAssetStore _meshStore = new(); private readonly RekallAgeModifierStackEvaluator _evaluator;
    public RekallAgeModifierStackEvaluationService(RekallAgeModifierStackEvaluator? evaluator = null) => _evaluator = evaluator ?? new();
    public async ValueTask<RekallAgeModifierStackEvaluationReport> EvaluateAsync(string root, string stackId, RekallAgeModelingEvaluationBudget budget, CancellationToken token)
    {
        var stack = await _stackStore.LoadVersionedAsync(root, stackId, token).ConfigureAwait(false); var source = await _meshStore.LoadVersionedAsync(root, stack.Value.SourceMeshAssetId, token).ConfigureAwait(false);
        if (source.Revision != stack.Value.SourceMeshFileRevision) throw new RekallAgeDocumentRevisionException("REKALL_DOCUMENT_REVISION_CONFLICT", _meshStore.GetMeshPath(root, stack.Value.SourceMeshAssetId), "Modifier stack source mesh revision is stale.", stack.Value.SourceMeshFileRevision, source.Revision);
        return await _evaluator.EvaluateAsync(stack.Value, source.Value, budget, token).ConfigureAwait(false);
    }
}

public sealed record RekallAgeModifierStackBakeResult(string StackAssetId, long StackLogicalRevision, RekallAgeMeshAsset Mesh, string BeforeFileRevision, string AfterFileRevision, RekallAgeModifierStackEvaluationReport Evaluation);
public sealed class RekallAgeModifierStackBakeService
{
    private readonly RekallAgeModifierStackEvaluationService _evaluation; private readonly RekallAgeMeshAssetStore _meshStore = new();
    public RekallAgeModifierStackBakeService(RekallAgeModifierStackEvaluator? evaluator = null) => _evaluation = new(evaluator);
    public async ValueTask<RekallAgeModifierStackBakeResult> BakeAsync(string root, string stackId, string targetId, string expectedTargetRevision,
        RekallAgeModelingEvaluationBudget budget, RekallAgeTransaction transaction, CancellationToken token)
    {
        var report = await _evaluation.EvaluateAsync(root, stackId, budget, token).ConfigureAwait(false);
        if (!report.Succeeded || report.Mesh is null) throw new InvalidOperationException("REKALL_MODIFIER_STACK_BAKE_EVALUATION_FAILED: Modifier stack evaluation failed.");
        var path = _meshStore.GetMeshPath(root, targetId); long logicalRevision;
        if (File.Exists(path)) { var current = await _meshStore.LoadVersionedAsync(root, targetId, token).ConfigureAwait(false); if (current.Revision != expectedTargetRevision) throw new RekallAgeDocumentRevisionException("REKALL_DOCUMENT_REVISION_CONFLICT", path, $"Target mesh '{targetId}' changed.", expectedTargetRevision, current.Revision); logicalRevision = current.Value.Revision + 1; }
        else { if (expectedTargetRevision != RekallAgeDocumentRevision.Missing) throw new RekallAgeDocumentRevisionException("REKALL_DOCUMENT_REVISION_CONFLICT", path, $"Target mesh '{targetId}' does not exist.", expectedTargetRevision, RekallAgeDocumentRevision.Missing); logicalRevision = 1; }
        var mesh = report.Mesh with { AssetId = targetId, Name = targetId, Revision = logicalRevision }; transaction.CaptureResourcePreimage(path);
        var after = await _meshStore.SaveIfRevisionAsync(root, mesh, expectedTargetRevision, token).ConfigureAwait(false); transaction.RecordChangedResource(path);
        return new(stackId, report.StackLogicalRevision, mesh, expectedTargetRevision, after, report);
    }
}
