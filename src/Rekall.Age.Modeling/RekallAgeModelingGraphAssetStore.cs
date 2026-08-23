using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModelingGraphAssetStore
{
    private const string FileSuffix = ".age.modeling-graph.json";
    private static readonly JsonSerializerOptions JsonOptions = new(RekallAgeModelingJson.Options)
    {
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };
    private readonly RekallAgeModelingGraphValidator _validator =
        new(RekallAgeModelingNodeCatalog.CreateDefault());

    public string GetGraphPath(string projectRoot, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateAssetId(assetId);
        return Path.Combine(projectRoot, "Modeling", "Graphs", assetId + FileSuffix);
    }

    public string GetRecoveryPath(string projectRoot, string assetId) =>
        RekallAgeDocumentRecoveryStore.GetPreviousPath(projectRoot, GetGraphPath(projectRoot, assetId));

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeModelingGraphAsset graph,
        CancellationToken cancellationToken)
    {
        ValidateForPersistence(graph, graph.AssetId);
        await RekallAgeAtomicFile.WriteAllTextAsync(
            GetGraphPath(projectRoot, graph.AssetId),
            Serialize(graph),
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> SaveIfRevisionAsync(
        string projectRoot,
        RekallAgeModelingGraphAsset graph,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        ValidateForPersistence(graph, graph.AssetId);
        var path = GetGraphPath(projectRoot, graph.AssetId);
        if (File.Exists(path))
        {
            var current = await LoadVersionedAsync(projectRoot, graph.AssetId, cancellationToken).ConfigureAwait(false);
            if (current.Revision.Equals(expectedRevision, StringComparison.Ordinal)
                && graph.Revision != current.Value.Revision + 1)
            {
                throw new InvalidDataException(
                    $"REKALL_MODELING_GRAPH_LOGICAL_REVISION_INVALID: Graph revision must advance from {current.Value.Revision} to {current.Value.Revision + 1}.");
            }
        }
        else if (graph.Revision != 1)
        {
            throw new InvalidDataException("REKALL_MODELING_GRAPH_LOGICAL_REVISION_INVALID: A new graph must start at revision 1.");
        }
        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            path,
            Serialize(graph),
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            expectedRevision,
            GetRecoveryPath(projectRoot, graph.AssetId),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeModelingGraphAsset> LoadAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken) =>
        (await LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false)).Value;

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeModelingGraphAsset>> LoadVersionedAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(
            GetGraphPath(projectRoot, assetId),
            "modelling graph asset",
            RekallAgeModelingGraphAsset.CurrentSchemaVersion,
            cancellationToken).ConfigureAwait(false);
        var graph = snapshot.Deserialize<RekallAgeModelingGraphAsset>(JsonOptions);
        ValidateForPersistence(graph, assetId);
        return new(graph with { SchemaVersion = RekallAgeModelingGraphAsset.CurrentSchemaVersion }, snapshot.File.Revision);
    }

    public IReadOnlyList<string> ListAssetIds(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var directory = Path.Combine(projectRoot, "Modeling", "Graphs");
        return !Directory.Exists(directory)
            ? []
            : Directory.EnumerateFiles(directory, "*" + FileSuffix)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name![..^FileSuffix.Length])
                .Order(StringComparer.Ordinal)
                .ToArray();
    }

    public string Serialize(RekallAgeModelingGraphAsset graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return JsonSerializer.Serialize(
            graph with { SchemaVersion = RekallAgeModelingGraphAsset.CurrentSchemaVersion },
            JsonOptions) + Environment.NewLine;
    }

    private void ValidateForPersistence(RekallAgeModelingGraphAsset graph, string expectedAssetId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ValidateAssetId(graph.AssetId);
        if (!graph.AssetId.Equals(expectedAssetId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"REKALL_MODELING_GRAPH_ASSET_ID_MISMATCH: Document asset ID '{graph.AssetId}' does not match requested ID '{expectedAssetId}'.");
        }
        var report = _validator.Validate(graph);
        if (!report.IsValid)
        {
            throw new InvalidDataException(
                "Modelling graph failed strict validation: "
                + string.Join(", ", report.Diagnostics
                    .Where(item => item.Severity == RekallAgeModelingDiagnosticSeverity.Error)
                    .Select(item => item.Code)
                    .Distinct(StringComparer.Ordinal)));
        }
    }

    private static void ValidateAssetId(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (assetId.Length > 128
            || !char.IsAsciiLetterOrDigit(assetId[0])
            || assetId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "Modelling graph asset ID must be a safe 1-128 character logical identifier using ASCII letters, digits, '.', '-', or '_'.",
                nameof(assetId));
        }
    }
}
