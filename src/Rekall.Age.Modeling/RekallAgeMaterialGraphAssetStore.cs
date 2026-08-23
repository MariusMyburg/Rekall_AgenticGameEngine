using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMaterialGraphAssetStore
{
    private const string FileSuffix = ".age.material-graph.json";
    private static readonly JsonSerializerOptions JsonOptions = new(RekallAgeModelingJson.Options)
    {
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };
    private readonly RekallAgeMaterialGraphValidator _validator = new(RekallAgeMaterialNodeCatalog.CreateDefault());

    public string GetGraphPath(string projectRoot, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot); ValidateId(assetId);
        return Path.Combine(projectRoot, "Materials", "Graphs", assetId + FileSuffix);
    }

    public string GetRecoveryPath(string projectRoot, string assetId) => RekallAgeDocumentRecoveryStore.GetPreviousPath(projectRoot, GetGraphPath(projectRoot, assetId));

    public async ValueTask<string> SaveIfRevisionAsync(string projectRoot, RekallAgeMaterialGraphAsset graph, string expectedRevision, CancellationToken cancellationToken)
    {
        Validate(graph, graph.AssetId);
        var path = GetGraphPath(projectRoot, graph.AssetId);
        if (File.Exists(path))
        {
            var current = await LoadVersionedAsync(projectRoot, graph.AssetId, cancellationToken).ConfigureAwait(false);
            if (current.Revision.Equals(expectedRevision, StringComparison.Ordinal) && graph.Revision != current.Value.Revision + 1)
                throw new InvalidDataException($"REKALL_MATERIAL_GRAPH_LOGICAL_REVISION_INVALID: Material graph revision must advance from {current.Value.Revision} to {current.Value.Revision + 1}.");
        }
        else if (graph.Revision != 1) throw new InvalidDataException("REKALL_MATERIAL_GRAPH_LOGICAL_REVISION_INVALID: A new material graph must start at revision 1.");
        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(path, Serialize(graph), RekallAgeDocumentSchemaProbe.MaximumDocumentBytes, expectedRevision, GetRecoveryPath(projectRoot, graph.AssetId), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeMaterialGraphAsset>> LoadVersionedAsync(string projectRoot, string assetId, CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(GetGraphPath(projectRoot, assetId), "material graph asset", RekallAgeMaterialGraphAsset.CurrentSchemaVersion, cancellationToken).ConfigureAwait(false);
        var graph = snapshot.Deserialize<RekallAgeMaterialGraphAsset>(JsonOptions); Validate(graph, assetId);
        return new(graph with { SchemaVersion = RekallAgeMaterialGraphAsset.CurrentSchemaVersion }, snapshot.File.Revision);
    }

    public ValueTask<RekallAgeMaterialGraphAsset> LoadAsync(string projectRoot, string assetId, CancellationToken cancellationToken) => LoadValueAsync(projectRoot, assetId, cancellationToken);
    private async ValueTask<RekallAgeMaterialGraphAsset> LoadValueAsync(string root, string id, CancellationToken token) => (await LoadVersionedAsync(root, id, token).ConfigureAwait(false)).Value;

    public IReadOnlyList<string> ListAssetIds(string projectRoot)
    {
        var directory = Path.Combine(projectRoot, "Materials", "Graphs");
        return !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*" + FileSuffix).Select(Path.GetFileName).Where(name => name is not null).Select(name => name![..^FileSuffix.Length]).Order(StringComparer.Ordinal).ToArray();
    }

    public string Serialize(RekallAgeMaterialGraphAsset graph) => JsonSerializer.Serialize(graph with { SchemaVersion = RekallAgeMaterialGraphAsset.CurrentSchemaVersion }, JsonOptions) + Environment.NewLine;

    private void Validate(RekallAgeMaterialGraphAsset graph, string expectedId)
    {
        ArgumentNullException.ThrowIfNull(graph); ValidateId(graph.AssetId);
        if (graph.AssetId != expectedId) throw new InvalidDataException($"REKALL_MATERIAL_GRAPH_ASSET_ID_MISMATCH: Document asset ID '{graph.AssetId}' does not match requested ID '{expectedId}'.");
        var report = _validator.Validate(graph);
        if (!report.IsValid) throw new InvalidDataException("Material graph failed strict validation: " + string.Join(", ", report.Diagnostics.Where(item => item.Severity == RekallAgeModelingDiagnosticSeverity.Error).Select(item => item.Code).Distinct(StringComparer.Ordinal)));
    }

    private static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 128 || !char.IsAsciiLetterOrDigit(id[0]) || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            throw new ArgumentException("Material graph asset ID must be a safe 1-128 character logical identifier using ASCII letters, digits, '.', '-', or '_'.", nameof(id));
    }
}
