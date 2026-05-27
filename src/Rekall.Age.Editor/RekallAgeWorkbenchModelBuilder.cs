using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Project;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Editor;

public sealed class RekallAgeWorkbenchModelBuilder
{
    private readonly RekallAgeProjectStore _projectStore;
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeAssetCatalogStore _assetStore;
    private readonly RekallAgeTransactionLogStore _transactionLogStore;

    public RekallAgeWorkbenchModelBuilder()
        : this(
            new RekallAgeProjectStore(),
            new RekallAgeSceneStore(),
            new RekallAgeAssetCatalogStore(),
            new RekallAgeTransactionLogStore())
    {
    }

    public RekallAgeWorkbenchModelBuilder(
        RekallAgeProjectStore projectStore,
        RekallAgeSceneStore sceneStore,
        RekallAgeAssetCatalogStore assetStore,
        RekallAgeTransactionLogStore transactionLogStore)
    {
        _projectStore = projectStore;
        _sceneStore = sceneStore;
        _assetStore = assetStore;
        _transactionLogStore = transactionLogStore;
    }

    public async ValueTask<RekallAgeWorkbenchModel> BuildAsync(
        string projectRoot,
        string activeSceneName,
        CancellationToken cancellationToken)
    {
        var manifest = await _projectStore.LoadAsync(projectRoot, cancellationToken);
        var scene = await _sceneStore.LoadAsync(projectRoot, activeSceneName, cancellationToken);
        var assets = await _assetStore.LoadAsync(projectRoot, cancellationToken);
        var transactions = await _transactionLogStore.LoadAsync(projectRoot, cancellationToken);
        var validation = await new RekallAgeProjectValidator(_sceneStore)
            .ValidateSceneAsync(projectRoot, activeSceneName, cancellationToken);
        var runtimeWorld = await new RekallAgeRuntimeSnapshotService(
                _sceneStore,
                new RekallAgeRuntimeWorldBuilder(),
                RekallAgeRuntimeExecutionLoop.CreateDefault())
            .InspectSceneAsync(projectRoot, activeSceneName, 0, cancellationToken);

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
            new RekallAgeTransactionPanelModel(
                transactions.Transactions
                    .Select(transaction => new RekallAgeTransactionPanelItem(
                        transaction.Id,
                        transaction.Name,
                        transaction.ChangedResources))
                    .ToArray()),
            new RekallAgeImportQueueModel(Array.Empty<RekallAgeImportQueueItem>()),
            BuildRuntimePanel(runtimeWorld),
            BuildSceneSummary(scene),
            BuildActionPalette(manifest.Capabilities));
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

    private static RekallAgeWorkbenchSceneSummaryModel BuildSceneSummary(RekallAgeSceneDocument scene)
    {
        var componentTypes = scene.Entities
            .SelectMany(entity => entity.Components)
            .GroupBy(component => component.Type, StringComparer.Ordinal)
            .Select(group => new RekallAgeWorkbenchComponentTypeSummary(group.Key, group.Count()))
            .OrderBy(summary => summary.Type, StringComparer.Ordinal)
            .ToArray();
        var tags = scene.Entities
            .SelectMany(entity => entity.Tags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeWorkbenchSceneSummaryModel(
            scene.Entities.Count,
            scene.Entities.Count(entity => string.IsNullOrWhiteSpace(entity.ParentId)),
            componentTypes.Sum(component => component.Count),
            tags,
            componentTypes);
    }

    private static RekallAgeWorkbenchActionPaletteModel BuildActionPalette(IReadOnlyList<string> capabilities)
    {
        var hasModules = capabilities.Any(capability => capability.Equals("modules", StringComparison.OrdinalIgnoreCase));
        var actions = new List<RekallAgeWorkbenchActionItem>
        {
            new(
                "validate-scene",
                "Validate Scene",
                "Diagnostics",
                "rekall.validation.scene",
                "Run generic scene validation and surface suggested engine tools.",
                Recommended: true),
            new(
                "inspect-runtime",
                "Inspect Runtime",
                "Runtime",
                "rekall.runtime.inspect_scene",
                "Build an inspectable runtime snapshot for the active scene.",
                Recommended: true),
            new(
                "capture-viewport",
                "Capture Viewport",
                "Rendering",
                "rekall.render.capture_runtime_viewport",
                "Capture a generic runtime viewport frame for visual diagnostics.",
                Recommended: true),
            new(
                "import-asset-report",
                "Import Asset Report",
                "Assets",
                "rekall.asset.import_report",
                "Preview asset import results before committing imported content.",
                Recommended: true),
            new(
                "tripo-generate-model",
                "Generate Tripo Model",
                "Assets",
                "rekall.asset.tripo.generate_model",
                "Generate a Tripo3D text-to-model task and import the completed model as a generic asset.",
                Recommended: true),
            new(
                "agent-authoring-gauntlet",
                "Agent Authoring Gauntlet",
                "Workflow",
                "rekall.workflow.agent_authoring_gauntlet",
                "Run the generic create, verify, package, audit, and proof-frame workflow.",
                Recommended: true)
        };

        if (hasModules)
        {
            actions.Add(new RekallAgeWorkbenchActionItem(
                "build-modules",
                "Build Modules",
                "Modules",
                "rekall.build.modules",
                "Build agent-authored project modules before runtime inspection or playtesting.",
                Recommended: true));
        }

        return new RekallAgeWorkbenchActionPaletteModel(
            actions
                .OrderByDescending(action => action.Recommended)
                .ThenBy(action => action.Category, StringComparer.Ordinal)
                .ThenBy(action => action.Label, StringComparer.Ordinal)
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

    private static RekallAgeRuntimePanelModel BuildRuntimePanel(RekallAgeRuntimeWorld world)
    {
        var rendering = world.Subsystems.Rendering;
        var physics = world.Subsystems.Physics;
        var audio = world.Subsystems.Audio;
        var animation = world.Subsystems.Animation;
        var ui = world.Subsystems.Ui;
        var activeCamera = rendering.Cameras.FirstOrDefault(camera => camera.Active)
            ?? rendering.Cameras.FirstOrDefault();

        return new RekallAgeRuntimePanelModel(
            world.SceneName,
            world.FrameIndex,
            activeCamera?.EntityName,
            "rekall.render.capture_runtime_viewport",
            world.Entities.Count,
            rendering.Cameras.Count + rendering.Sprites.Count + rendering.Meshes.Count + rendering.Lights.Count + rendering.UiLayers.Count,
            physics.RigidBodies.Count,
            audio.Emitters.Count,
            animation.Players.Count,
            ui.Elements.Count,
            world.Observations
                .Select(observation => new RekallAgeRuntimePanelObservation(
                    observation.Code,
                    observation.Severity,
                    observation.Subsystem,
                    observation.TargetName.Length > 0 ? observation.TargetName : observation.TargetId,
                    observation.Message))
                .ToArray());
    }
}
