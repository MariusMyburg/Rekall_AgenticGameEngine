using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMaterialInstanceAssetStore
{
    private const string FileSuffix = ".age.material-instance.json";
    private static readonly JsonSerializerOptions JsonOptions = new(RekallAgeModelingJson.Options) { MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth };
    private readonly RekallAgeMaterialGraphAssetStore _graphStore = new();
    private readonly RekallAgeMaterialNodeCatalog _catalog = RekallAgeMaterialNodeCatalog.CreateDefault();

    public string GetInstancePath(string projectRoot, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot); ValidateId(assetId, "Material instance");
        return Path.Combine(projectRoot, "Materials", "Instances", assetId + FileSuffix);
    }
    public string GetRecoveryPath(string projectRoot, string assetId) => RekallAgeDocumentRecoveryStore.GetPreviousPath(projectRoot, GetInstancePath(projectRoot, assetId));

    public async ValueTask<string> SaveIfRevisionAsync(string projectRoot, RekallAgeMaterialInstanceAsset instance, string expectedRevision, CancellationToken cancellationToken)
    {
        await ValidateAsync(projectRoot, instance, instance.AssetId, cancellationToken).ConfigureAwait(false);
        var path = GetInstancePath(projectRoot, instance.AssetId);
        if (File.Exists(path))
        {
            var current = await LoadVersionedAsync(projectRoot, instance.AssetId, cancellationToken).ConfigureAwait(false);
            if (current.Revision.Equals(expectedRevision, StringComparison.Ordinal) && instance.Revision != current.Value.Revision + 1)
                throw new InvalidDataException($"REKALL_MATERIAL_INSTANCE_LOGICAL_REVISION_INVALID: Revision must advance from {current.Value.Revision} to {current.Value.Revision + 1}.");
        }
        else if (instance.Revision != 1) throw new InvalidDataException("REKALL_MATERIAL_INSTANCE_LOGICAL_REVISION_INVALID: A new material instance must start at revision 1.");
        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(path, Serialize(instance), RekallAgeDocumentSchemaProbe.MaximumDocumentBytes, expectedRevision, GetRecoveryPath(projectRoot, instance.AssetId), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeMaterialInstanceAsset>> LoadVersionedAsync(string projectRoot, string assetId, CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(GetInstancePath(projectRoot, assetId), "material instance asset", RekallAgeMaterialInstanceAsset.CurrentSchemaVersion, cancellationToken).ConfigureAwait(false);
        var instance = snapshot.Deserialize<RekallAgeMaterialInstanceAsset>(JsonOptions);
        await ValidateAsync(projectRoot, instance, assetId, cancellationToken).ConfigureAwait(false);
        return new(instance with { SchemaVersion = RekallAgeMaterialInstanceAsset.CurrentSchemaVersion }, snapshot.File.Revision);
    }

    public IReadOnlyList<string> ListAssetIds(string projectRoot)
    {
        var directory = Path.Combine(projectRoot, "Materials", "Instances");
        return !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*" + FileSuffix).Select(Path.GetFileName).Where(name => name is not null).Select(name => name![..^FileSuffix.Length]).Order(StringComparer.Ordinal).ToArray();
    }
    public string Serialize(RekallAgeMaterialInstanceAsset instance) => JsonSerializer.Serialize(instance with { SchemaVersion = RekallAgeMaterialInstanceAsset.CurrentSchemaVersion }, JsonOptions) + Environment.NewLine;

    private async ValueTask ValidateAsync(string root, RekallAgeMaterialInstanceAsset instance, string expectedId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(instance); ValidateId(instance.AssetId, "Material instance"); ValidateId(instance.GraphAssetId, "Material graph");
        if (instance.SchemaVersion != RekallAgeMaterialInstanceAsset.CurrentSchemaVersion) throw new InvalidDataException($"REKALL_MATERIAL_INSTANCE_SCHEMA_UNSUPPORTED: Schema {instance.SchemaVersion} is unsupported.");
        if (instance.AssetId != expectedId) throw new InvalidDataException("REKALL_MATERIAL_INSTANCE_ASSET_ID_MISMATCH: Document and requested asset IDs differ.");
        if (string.IsNullOrWhiteSpace(instance.Name) || instance.Name.Length > 256 || instance.Revision < 1 || instance.Overrides.Count > 512) throw new InvalidDataException("REKALL_MATERIAL_INSTANCE_INVALID: Name, revision, or override bounds are invalid.");
        var graph = await _graphStore.LoadVersionedAsync(root, instance.GraphAssetId, token).ConfigureAwait(false);
        if (!graph.Revision.Equals(instance.GraphFileRevision, StringComparison.Ordinal))
            throw new RekallAgeDocumentRevisionException("REKALL_DOCUMENT_REVISION_CONFLICT", _graphStore.GetGraphPath(root, instance.GraphAssetId),
                $"Material instance '{instance.AssetId}' targets graph revision '{instance.GraphFileRevision}', current revision is '{graph.Revision}'.", instance.GraphFileRevision, graph.Revision);
        var exposed = graph.Value.ExposedParameters.ToDictionary(item => item.Name, StringComparer.Ordinal);
        foreach (var item in instance.Overrides)
        {
            if (!exposed.TryGetValue(item.Key, out var parameter)) throw new InvalidDataException($"REKALL_MATERIAL_INSTANCE_OVERRIDE_UNKNOWN: Override '{item.Key}' is not exposed by graph '{graph.Value.AssetId}'.");
            var node = graph.Value.Nodes.Single(candidate => candidate.NodeId == parameter.NodeId);
            var descriptor = _catalog.Find(node.TypeId, node.TypeVersion)!;
            var contract = descriptor.Parameters.Single(candidate => candidate.ParameterId == parameter.ParameterId);
            if (contract.ValueType != parameter.ValueType || !ValidValue(item.Value, contract)) throw new InvalidDataException($"REKALL_MATERIAL_INSTANCE_OVERRIDE_INVALID: Override '{item.Key}' does not match its declared type or range.");
        }
    }

    private static bool ValidValue(JsonNode? node, RekallAgeMaterialParameterDescriptor contract)
    {
        if (node is null) return false;
        if (contract.ValueType == RekallAgeMaterialValueType.Float) return node is JsonValue numberNode && numberNode.TryGetValue<double>(out var number) && double.IsFinite(number) && number >= (contract.Minimum ?? double.NegativeInfinity) && number <= (contract.Maximum ?? double.PositiveInfinity);
        if (contract.ValueType == RekallAgeMaterialValueType.Vector2) return node is JsonArray { Count: 2 } array && array.All(item => item is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number));
        return node is JsonValue textNode && textNode.TryGetValue<string>(out var text) && text.Length <= 2_048 && (contract.EnumChoices is not { Count: > 0 } || contract.EnumChoices.Contains(text, StringComparer.Ordinal));
    }
    private static void ValidateId(string id, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 128 || !char.IsAsciiLetterOrDigit(id[0]) || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_')) throw new ArgumentException($"{kind} asset ID is unsafe.", nameof(id));
    }
}

public sealed class RekallAgeMaterialInstanceResolver
{
    private readonly RekallAgeMaterialGraphValidator _validator = new(RekallAgeMaterialNodeCatalog.CreateDefault());
    public RekallAgeMaterialGraphAsset Resolve(RekallAgeMaterialGraphAsset graph, string graphFileRevision, RekallAgeMaterialInstanceAsset instance)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(instance);
        if (instance.GraphAssetId != graph.AssetId || instance.GraphFileRevision != graphFileRevision) throw new RekallAgeDocumentRevisionException("REKALL_DOCUMENT_REVISION_CONFLICT", graph.AssetId, "Material instance does not target this exact graph revision.", instance.GraphFileRevision, graphFileRevision);
        var exposed = graph.ExposedParameters.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var nodes = graph.Nodes.Select(node => node with { Parameters = (JsonObject)node.Parameters.DeepClone() }).ToArray();
        foreach (var item in instance.Overrides)
        {
            if (!exposed.TryGetValue(item.Key, out var parameter)) throw new InvalidDataException($"REKALL_MATERIAL_INSTANCE_OVERRIDE_UNKNOWN: Override '{item.Key}' is not exposed.");
            var index = Array.FindIndex(nodes, node => node.NodeId == parameter.NodeId);
            nodes[index].Parameters[parameter.ParameterId] = item.Value?.DeepClone();
        }
        var resolved = graph with { Nodes = nodes };
        var report = _validator.Validate(resolved);
        if (!report.IsValid) throw new InvalidDataException("REKALL_MATERIAL_INSTANCE_RESOLUTION_INVALID: Resolved material graph is invalid.");
        return resolved;
    }
}
