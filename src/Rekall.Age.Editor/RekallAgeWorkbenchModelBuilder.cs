using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Project;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Editor;

public sealed class RekallAgeWorkbenchModelBuilder
{
    private readonly RekallAgeProjectStore _projectStore;
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeAssetCatalogStore _assetStore;

    public RekallAgeWorkbenchModelBuilder()
        : this(new RekallAgeProjectStore(), new RekallAgeSceneStore(), new RekallAgeAssetCatalogStore())
    {
    }

    public RekallAgeWorkbenchModelBuilder(
        RekallAgeProjectStore projectStore,
        RekallAgeSceneStore sceneStore,
        RekallAgeAssetCatalogStore assetStore)
    {
        _projectStore = projectStore;
        _sceneStore = sceneStore;
        _assetStore = assetStore;
    }

    public async ValueTask<RekallAgeWorkbenchModel> BuildAsync(
        string projectRoot,
        string activeSceneName,
        CancellationToken cancellationToken)
    {
        var manifest = await _projectStore.LoadAsync(projectRoot, cancellationToken);
        var scene = await _sceneStore.LoadAsync(projectRoot, activeSceneName, cancellationToken);
        var assets = await _assetStore.LoadAsync(projectRoot, cancellationToken);
        var validation = await new RekallAgeProjectValidator(_sceneStore)
            .ValidateSceneAsync(projectRoot, activeSceneName, cancellationToken);

        return new RekallAgeWorkbenchModel(
            new RekallAgeProjectTreeModel(
                manifest.Name,
                projectRoot,
                manifest.Capabilities,
                _sceneStore.ListSceneNames(projectRoot)
                    .Select(name => new RekallAgeProjectSceneItem(
                        name,
                        _sceneStore.GetScenePath(projectRoot, name),
                        name.Equals(activeSceneName, StringComparison.Ordinal)))
                    .ToArray()),
            BuildSceneGraph(scene),
            BuildInspector(scene),
            new RekallAgeAssetBrowserModel(
                assets.Assets
                    .OrderBy(asset => asset.Kind, StringComparer.Ordinal)
                    .ThenBy(asset => asset.DisplayName, StringComparer.Ordinal)
                    .Select(asset => new RekallAgeAssetBrowserItem(
                        asset.Id,
                        asset.DisplayName,
                        asset.Kind,
                        asset.ImportedPath,
                        asset.ContentHash))
                    .ToArray()),
            new RekallAgeValidationPanelModel(
                validation.Issues
                    .Select(issue => new RekallAgeValidationPanelIssue(
                        issue.Code,
                        issue.Severity,
                        issue.Message,
                        issue.Target ?? string.Empty,
                        (issue.SuggestedCommands ?? Array.Empty<RekallAgeSuggestedCommand>())
                            .Select(command => command.Tool)
                            .ToArray()))
                    .ToArray()),
            new RekallAgeTransactionPanelModel(Array.Empty<RekallAgeTransactionPanelItem>()),
            new RekallAgeImportQueueModel(Array.Empty<RekallAgeImportQueueItem>()));
    }

    private static RekallAgeSceneGraphModel BuildSceneGraph(RekallAgeSceneDocument scene)
    {
        var childrenByParent = scene.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.ParentId))
            .GroupBy(entity => entity.ParentId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(entity => entity.Name, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var roots = scene.Entities
            .Where(entity => string.IsNullOrWhiteSpace(entity.ParentId))
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .Select(entity => ToNode(entity, childrenByParent))
            .ToArray();
        return new RekallAgeSceneGraphModel(scene.Id, scene.Name, roots);
    }

    private static RekallAgeSceneEntityNode ToNode(
        RekallAgeEntityDocument entity,
        IReadOnlyDictionary<string, RekallAgeEntityDocument[]> childrenByParent)
    {
        var children = childrenByParent.TryGetValue(entity.Id, out var items)
            ? items.Select(child => ToNode(child, childrenByParent)).ToArray()
            : Array.Empty<RekallAgeSceneEntityNode>();
        return new RekallAgeSceneEntityNode(
            entity.Id,
            entity.Name,
            entity.Tags,
            entity.ParentId,
            entity.Visible,
            entity.Locked,
            children);
    }

    private static RekallAgeInspectorModel BuildInspector(RekallAgeSceneDocument scene)
    {
        var selected = scene.Entities.OrderBy(entity => entity.Name, StringComparer.Ordinal).FirstOrDefault();
        if (selected is null)
        {
            return new RekallAgeInspectorModel(null, null, Array.Empty<RekallAgeInspectorComponentModel>());
        }

        return new RekallAgeInspectorModel(
            selected.Id,
            selected.Name,
            selected.Components
                .Select(component => new RekallAgeInspectorComponentModel(
                    component.Type,
                    component.Properties
                        .OrderBy(property => property.Key, StringComparer.Ordinal)
                        .Select(property => new RekallAgeInspectorPropertyModel(
                            property.Key,
                            ToDisplayValue(property.Value),
                            property.Value?.GetValueKind().ToString() ?? "Null"))
                        .ToArray()))
                .ToArray());
    }

    private static string ToDisplayValue(JsonNode? value)
    {
        if (value is null)
        {
            return "null";
        }

        return value is JsonValue jsonValue
            ? jsonValue.ToJsonString().Trim('"')
            : value.ToJsonString();
    }
}
