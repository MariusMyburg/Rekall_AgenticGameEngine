using System.Text.Json;
using System.IO;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Modeling;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Studio;

internal interface IRekallAgeStudioContentSource
{
    string Family { get; }
    ValueTask<IReadOnlyList<RekallAgeContentBrowserItem>> LoadAsync(
        string projectRoot, CancellationToken cancellationToken);
}

internal interface IRekallAgeStudioContentIndex
{
    ValueTask<RekallAgeContentBrowserModel> RefreshAsync(
        string projectRoot, CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioContentIndex(
    IReadOnlyList<IRekallAgeStudioContentSource> sources) : IRekallAgeStudioContentIndex
{
    private readonly IReadOnlyList<IRekallAgeStudioContentSource> _sources =
        sources ?? throw new ArgumentNullException(nameof(sources));

    public static RekallAgeStudioContentIndex CreateDefault() => new([
        new ImportedContentSource(),
        new MeshContentSource(),
        new ModelingGraphContentSource(),
        new MaterialGraphContentSource(),
        new ModuleSourceContentSource()
    ]);

    public async ValueTask<RekallAgeContentBrowserModel> RefreshAsync(
        string projectRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var items = new Dictionary<string, RekallAgeContentBrowserItem>(StringComparer.Ordinal);
        var warnings = new List<RekallAgeContentBrowserWarning>();
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var item in await source.LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false))
                {
                    items.TryAdd(item.Id, item);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsBoundedSourceFailure(exception))
            {
                warnings.Add(new(
                    "REKALL_CONTENT_SOURCE_FAILED",
                    source.Family,
                    $"The {source.Family} content source could not be read."));
            }
        }

        return new(
            items.Values
                .OrderBy(item => item.Family, StringComparer.Ordinal)
                .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray(),
            warnings.OrderBy(warning => warning.Family, StringComparer.Ordinal).ToArray());
    }

    private static bool IsBoundedSourceFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException;
}

internal static class RekallAgeStudioContentProjection
{
    public static IReadOnlyList<string> Categories(IEnumerable<RekallAgeContentBrowserItem> items) =>
        new[] { "All" }.Concat(items.Select(item => item.Family)
            .Where(family => !string.IsNullOrWhiteSpace(family))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)).ToArray();

    public static IReadOnlyList<RekallAgeContentBrowserItem> Filter(
        IEnumerable<RekallAgeContentBrowserItem> items, string? category, string? search)
    {
        var query = items;
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(item => item.Family.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item => new[] { item.DisplayName, item.Family, item.Kind, item.Path, item.SourcePath }
                .Any(value => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));
        }
        return query.ToArray();
    }
}

internal sealed class ImportedContentSource : IRekallAgeStudioContentSource
{
    private readonly RekallAgeAssetCatalogStore _store = new();
    public string Family => "imported";

    public async ValueTask<IReadOnlyList<RekallAgeContentBrowserItem>> LoadAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var catalog = await _store.LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        return catalog.Assets.Select(asset =>
        {
            var normalizedKind = asset.Kind.Trim().ToLowerInvariant();
            var family = normalizedKind switch
            {
                "model" or "mesh" or "gltf" or "glb" => "model",
                "texture" or "sprite" or "image" => "texture",
                "audio" or "sound" => "audio",
                "shader" => "shader",
                _ => normalizedKind
            };
            var route = family switch { "model" => "mesh-edit", "texture" => "texture-preview", "audio" => "audio-preview", "shader" => "shader-edit", _ => "external" };
            var capabilities = new List<string> { RekallAgeContentCapability.Open };
            if (family != "model") capabilities.Add(RekallAgeContentCapability.OpenExternal);
            capabilities.Add(RekallAgeContentCapability.Reveal);
            capabilities.Add(RekallAgeContentCapability.Reimport);
            if (family == "model") capabilities.Add(RekallAgeContentCapability.Place);
            if (family is "texture" or "audio" or "shader") capabilities.Add(RekallAgeContentCapability.Assign);
            return new RekallAgeContentBrowserItem(asset.Id, asset.DisplayName, family, asset.Kind, "Imported",
                asset.ImportedPath, asset.SourcePath, asset.ContentHash, route, capabilities, "Healthy", null,
                new(asset.TextureMetadata?.Width, asset.TextureMetadata?.Height, asset.GlbMetadata?.MeshCount,
                    asset.GlbMetadata?.MaterialCount, asset.GlbMetadata?.AnimationCount));
        }).ToArray();
    }
}

internal abstract class StoredIdContentSource(string family, string kind, string route) : IRekallAgeStudioContentSource
{
    public string Family => family;
    protected abstract IReadOnlyList<string> List(string projectRoot);
    protected abstract string PathFor(string projectRoot, string id);
    public ValueTask<IReadOnlyList<RekallAgeContentBrowserItem>> LoadAsync(string projectRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<RekallAgeContentBrowserItem>>(List(projectRoot).Select(id =>
            new RekallAgeContentBrowserItem($"{family}:{id}", id, family, kind, "Authored", PathFor(projectRoot, id), null,
                File.Exists(PathFor(projectRoot, id)) ? File.GetLastWriteTimeUtc(PathFor(projectRoot, id)).Ticks.ToString() : null,
                route, [RekallAgeContentCapability.Open, RekallAgeContentCapability.Reveal], "Healthy", null, new())).ToArray());
    }
}

internal sealed class MeshContentSource() : StoredIdContentSource("model", "mesh", "mesh-edit")
{
    private readonly RekallAgeMeshAssetStore _store = new();
    protected override IReadOnlyList<string> List(string projectRoot) => _store.ListAssetIds(projectRoot);
    protected override string PathFor(string projectRoot, string id) => _store.GetMeshPath(projectRoot, id);
}

internal sealed class ModelingGraphContentSource() : StoredIdContentSource("modeling-graph", "modeling-graph", "modeling-graph")
{
    private readonly RekallAgeModelingGraphAssetStore _store = new();
    protected override IReadOnlyList<string> List(string projectRoot) => _store.ListAssetIds(projectRoot);
    protected override string PathFor(string projectRoot, string id) => _store.GetGraphPath(projectRoot, id);
}

internal sealed class MaterialGraphContentSource() : StoredIdContentSource("material", "material-graph", "material-graph")
{
    private readonly RekallAgeMaterialGraphAssetStore _store = new();
    protected override IReadOnlyList<string> List(string projectRoot) => _store.ListAssetIds(projectRoot);
    protected override string PathFor(string projectRoot, string id) => _store.GetGraphPath(projectRoot, id);
}

internal sealed class ModuleSourceContentSource : IRekallAgeStudioContentSource
{
    public string Family => "module";
    public async ValueTask<IReadOnlyList<RekallAgeContentBrowserItem>> LoadAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var result = await new ListModuleSourcesCommand().ExecuteAsync(new(projectRoot),
            new RekallAgeCommandContext("studio-content", RekallAgeTransaction.Begin("Index module sources"), cancellationToken));
        if (!result.Ok) throw new InvalidDataException("Module source listing failed.");
        return result.Value.Sources.Select(source => new RekallAgeContentBrowserItem(
            $"module:{source.ModuleName}:{source.FileName.Replace('\\', '/')}", source.FileName, "module", "module-source", "Authored",
            source.SourcePath, null, source.Bytes.ToString(), "module-source",
            [RekallAgeContentCapability.Open, RekallAgeContentCapability.OpenExternal, RekallAgeContentCapability.Reveal], "Healthy", null, new())).ToArray();
    }
}
