using System.Text.Json.Nodes;
using System.Globalization;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Modules.Security;
using Rekall.Age.Project;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
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
        CancellationToken cancellationToken,
        string? selectedEntityId = null)
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
        RekallAgeModuleTrustException? moduleSchemaIssue = null;
        IReadOnlyList<RekallAgeComponentSchema> componentSchemas;
        try
        {
            componentSchemas = RekallAgeModuleIndexer.IndexAssemblies(
                    new[] { typeof(RekallAgeBuiltInModule).Assembly }
                        .Concat(RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(projectRoot)))
                .Components;
        }
        catch (RekallAgeModuleTrustException exception)
        {
            moduleSchemaIssue = exception;
            componentSchemas = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly).Components;
        }
        var validationIssues = validation.Issues
            .Select(issue => new RekallAgeValidationPanelIssue(
                issue.Code,
                issue.Severity,
                issue.Message,
                issue.Target ?? string.Empty,
                (issue.SuggestedCommands ?? Array.Empty<RekallAgeSuggestedCommand>())
                    .Select(command => command.Tool)
                    .ToArray()))
            .ToList();
        if (moduleSchemaIssue is not null)
        {
            validationIssues.Add(new RekallAgeValidationPanelIssue(
                moduleSchemaIssue.Code,
                "blocking",
                moduleSchemaIssue.Message,
                moduleSchemaIssue.Target,
                ["rekall.build.modules", "rekall.module.read_source"]));
        }

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
            BuildInspector(scene, selectedEntityId, componentSchemas),
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
                validationIssues),
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
            BuildActionPalette(manifest.Capabilities))
        {
            Rendering = BuildRenderingPanel(runtimeWorld)
        };
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

    private static RekallAgeInspectorModel BuildInspector(
        RekallAgeSceneDocument scene,
        string? selectedEntityId,
        IReadOnlyList<RekallAgeComponentSchema> schemas)
    {
        var availableComponents = schemas.Select(ToInspectorSchema).ToArray();
        var selected = string.IsNullOrWhiteSpace(selectedEntityId)
            ? null
            : scene.Entities.FirstOrDefault(entity => entity.Id.Equals(selectedEntityId, StringComparison.Ordinal));
        selected ??= scene.Entities.OrderBy(entity => entity.Name, StringComparer.Ordinal).FirstOrDefault();
        if (selected is null)
        {
            return new RekallAgeInspectorModel(null, null, Array.Empty<RekallAgeInspectorComponentModel>())
            {
                AvailableComponents = availableComponents
            };
        }

        return new RekallAgeInspectorModel(
            selected.Id,
            selected.Name,
            selected.Components
                .Select(component => BuildInspectorComponent(
                    component,
                    schemas.FirstOrDefault(schema => schema.TypeName.Equals(component.Type, StringComparison.Ordinal))))
                .ToArray())
        {
            AvailableComponents = availableComponents
        };
    }

    private static RekallAgeInspectorComponentModel BuildInspectorComponent(
        RekallAgeComponentDocument component,
        RekallAgeComponentSchema? schema)
    {
        var properties = component.Properties
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => BuildInspectorProperty(
                property.Key,
                property.Value,
                schema?.Properties.FirstOrDefault(candidate => candidate.Name.Equals(property.Key, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        if (schema is not null)
        {
            properties.AddRange(schema.Properties
                .Where(candidate => !component.Properties.Any(property => property.Key.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(candidate => BuildInspectorProperty(ToEditorPropertyName(candidate.Name), null, candidate, isDefined: false)));
        }

        return new RekallAgeInspectorComponentModel(
            component.Type,
            properties.OrderBy(property => property.Name, StringComparer.Ordinal).ToArray())
        {
            DisplayName = schema?.DisplayName ?? component.Type,
            Description = schema?.Description,
            SchemaKnown = schema is not null
        };
    }

    private static RekallAgeInspectorPropertyModel BuildInspectorProperty(
        string name,
        JsonNode? value,
        RekallAgePropertySchema? schema,
        bool isDefined = true) =>
        new(name, isDefined ? ToDisplayValue(value) : string.Empty, isDefined ? value?.GetValueKind().ToString() ?? "Null" : "Undefined")
        {
            TypeName = schema?.TypeName ?? value?.GetValueKind().ToString() ?? "Object",
            EditorKind = schema?.Kind ?? "json",
            AssetKind = schema?.AssetKind,
            Minimum = schema?.Minimum,
            Maximum = schema?.Maximum,
            Description = schema?.Description,
            AllowedValues = schema?.AllowedValues ?? [],
            IsDefined = isDefined
        };

    private static RekallAgeInspectorComponentSchemaModel ToInspectorSchema(RekallAgeComponentSchema schema) =>
        new(
            schema.TypeName,
            schema.DisplayName,
            schema.Description,
            schema.Properties.Select(property => new RekallAgeInspectorPropertySchemaModel(
                ToEditorPropertyName(property.Name),
                property.TypeName,
                property.Kind,
                property.AssetKind,
                property.Minimum,
                property.Maximum,
                property.Description,
                property.AllowedValues)).ToArray());

    private static string ToEditorPropertyName(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

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
                "compare-quality-presets",
                "Compare Quality Presets",
                "Rendering",
                "rekall.render.compare_quality_presets",
                "Capture aligned deterministic frames for requested render-quality presets.",
                Recommended: true),
            new(
                "inspect-render-budget",
                "Inspect Render Budget",
                "Rendering",
                "rekall.render.performance.inspect_scene_budget",
                "Inspect the resolved feature plan, render workload, resources, and timing diagnostics.",
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

    private static RekallAgeWorkbenchRenderQualityModel BuildRenderingPanel(RekallAgeRuntimeWorld world)
    {
        var profile = world.Subsystems.Rendering.QualityProfiles
            .OrderBy(item => item.EntityName, StringComparer.Ordinal)
            .ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (profile is null)
        {
            return RekallAgeWorkbenchRenderQualityModel.Empty("High");
        }

        var intent = profile.Intent;
        var authoring = new RekallAgeWorkbenchRenderQualityAuthoringModel(
            profile.EntityId,
            profile.EntityName,
            intent.Preset,
            intent.ResolutionScale,
            intent.ShadowCascadeCount,
            intent.ShadowResolution,
            intent.FogMode,
            intent.Bloom,
            intent.Ssao,
            intent.MaximumActiveParticles,
            intent.AutomaticScaling,
            intent.TargetFramesPerSecond,
            intent.EnableGpuTimestamps);
        return new RekallAgeWorkbenchRenderQualityModel(
            authoring,
            RekallAgeWorkbenchRenderQualityRuntimeModel.Unavailable(intent.Preset),
            [],
            []);
    }

    public static RekallAgeWorkbenchRenderQualityRuntimeModel BuildRenderingRuntime(
        RekallAgeResolvedRenderFeaturePlan plan,
        RekallAgeGpuFrameTimingReport timings,
        long resourceBytes,
        int drawCount,
        int dispatchCount,
        IReadOnlyList<string> suggestedActions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(timings);
        ArgumentNullException.ThrowIfNull(suggestedActions);
        var timingAvailable = timings.Available && timings.TotalMilliseconds.HasValue;
        return new RekallAgeWorkbenchRenderQualityRuntimeModel(
            plan.RequestedPreset,
            plan.ResolvedPreset,
            plan.OutputWidth,
            plan.OutputHeight,
            plan.RenderWidth,
            plan.RenderHeight,
            plan.ResolutionScale,
            timingAvailable,
            timingAvailable ? timings.Code : timings.Code ?? "REKALL_GPU_TIMESTAMPS_UNAVAILABLE",
            timingAvailable ? timings.Provenance : "unavailable",
            timingAvailable ? timings.TotalMilliseconds : null,
            timingAvailable
                ? $"{timings.TotalMilliseconds!.Value.ToString("0.000", CultureInfo.InvariantCulture)} ms"
                : "Unavailable",
            drawCount,
            dispatchCount,
            timingAvailable
                ? timings.Passes.Select(pass => new RekallAgeWorkbenchRenderPassTimingModel(
                    pass.Name,
                    pass.Nanoseconds,
                    pass.Milliseconds,
                    $"{pass.Milliseconds.ToString("0.000", CultureInfo.InvariantCulture)} ms")).ToArray()
                : [],
            [
                new RekallAgeWorkbenchRenderResourceModel("Frame resources", resourceBytes),
                new RekallAgeWorkbenchRenderResourceModel("Planned transient", plan.EstimatedTransientBytes),
                new RekallAgeWorkbenchRenderResourceModel("Planned persistent", plan.EstimatedPersistentBytes)
            ],
            ToDegradations(plan.Degradations),
            suggestedActions.ToArray());
    }

    public static RekallAgeWorkbenchModel WithCaptureResult(
        RekallAgeWorkbenchModel model,
        CaptureRuntimeViewportResult capture)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(capture);
        var runtime = capture.QualityPlan is null
            ? model.Rendering.Runtime
            : BuildRenderingRuntime(
                capture.QualityPlan,
                capture.GpuTimings,
                capture.ResourceBytes,
                capture.DrawCount,
                capture.DispatchCount,
                capture.SuggestedCommands);
        return model with
        {
            Rendering = model.Rendering with
            {
                Runtime = runtime,
                Comparisons = [],
                DebugViews = BuildDebugViews(capture)
            }
        };
    }

    public static RekallAgeWorkbenchModel WithQualityComparisonResult(
        RekallAgeWorkbenchModel model,
        CompareQualityPresetsResult comparison)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(comparison);
        var comparisons = comparison.Captures.Select(ToComparison).ToArray();
        var runtime = comparison.Captures.Count == 0
            ? model.Rendering.Runtime
            : ToRuntime(comparison.Captures[0], comparison.NextCommands);
        return model with
        {
            Rendering = model.Rendering with
            {
                Runtime = runtime,
                Comparisons = comparisons,
                DebugViews = comparisons.Select(item => new RekallAgeWorkbenchRenderDebugViewModel(
                    $"{item.RequestedPreset} final",
                    "final",
                    item.ScreenshotPath,
                    item.NonBlank)).ToArray()
            }
        };
    }

    private static RekallAgeWorkbenchRenderQualityRuntimeModel ToRuntime(
        RekallAgeQualityPresetCapture capture,
        IReadOnlyList<string> suggestedActions)
    {
        var timingAvailable = capture.GpuTimings.Available && capture.GpuTimings.TotalMilliseconds.HasValue;
        return new RekallAgeWorkbenchRenderQualityRuntimeModel(
            capture.RequestedPreset,
            capture.ResolvedPreset,
            capture.OutputWidth,
            capture.OutputHeight,
            capture.RenderWidth,
            capture.RenderHeight,
            null,
            timingAvailable,
            timingAvailable ? capture.GpuTimings.Code : capture.GpuTimings.Code ?? "REKALL_GPU_TIMESTAMPS_UNAVAILABLE",
            timingAvailable ? capture.GpuTimings.Provenance : "unavailable",
            timingAvailable ? capture.GpuTimings.TotalMilliseconds : null,
            FormatTiming(capture.GpuTimings),
            capture.DrawCount,
            capture.DispatchCount,
            timingAvailable
                ? capture.GpuTimings.Passes.Select(pass => new RekallAgeWorkbenchRenderPassTimingModel(
                    pass.Name,
                    pass.Nanoseconds,
                    pass.Milliseconds,
                    $"{pass.Milliseconds.ToString("0.000", CultureInfo.InvariantCulture)} ms")).ToArray()
                : [],
            [new RekallAgeWorkbenchRenderResourceModel("Frame resources", capture.ResourceBytes)],
            ToDegradations(capture.Degradations),
            suggestedActions.ToArray());
    }

    private static RekallAgeWorkbenchRenderQualityComparisonModel ToComparison(
        RekallAgeQualityPresetCapture capture) => new(
        capture.RequestedPreset,
        capture.ResolvedPreset,
        capture.ScreenshotPath,
        capture.NonBlank,
        capture.OutputWidth,
        capture.OutputHeight,
        capture.RenderWidth,
        capture.RenderHeight,
        capture.ResourceBytes,
        capture.DrawCount,
        capture.DispatchCount,
        capture.GpuTimings.Available ? capture.GpuTimings.TotalMilliseconds : null,
        FormatTiming(capture.GpuTimings),
        ToDegradations(capture.Degradations));

    private static IReadOnlyList<RekallAgeWorkbenchRenderDebugViewModel> BuildDebugViews(
        CaptureRuntimeViewportResult capture)
    {
        var views = new List<RekallAgeWorkbenchRenderDebugViewModel>();
        if (!capture.Captured)
        {
            return views;
        }

        if (!string.IsNullOrWhiteSpace(capture.ScreenshotPath))
        {
            views.Add(new("Final output", "final", capture.ScreenshotPath, capture.NonBlank));
        }

        if (capture.HighFidelityFrame is { } frame)
        {
            views.AddRange(frame.ShadowDebugCaptures.Select(item => new RekallAgeWorkbenchRenderDebugViewModel(
                $"Shadow cascade {item.CascadeIndex}",
                "shadow-cascade",
                item.OutputPath,
                item.NonBlank)));
            views.AddRange(frame.FogDebugCaptures.Select(item => new RekallAgeWorkbenchRenderDebugViewModel(
                $"Fog {item.Kind} {item.SliceIndex}",
                "fog-slice",
                item.OutputPath,
                item.NonBlank)));
            views.AddRange(frame.ParticleDebugCaptures.Select(item => new RekallAgeWorkbenchRenderDebugViewModel(
                $"Particles {item.Kind}",
                $"particle-{item.Kind}",
                item.OutputPath,
                item.NonBlank)));
        }

        return views;
    }

    private static IReadOnlyList<RekallAgeWorkbenchRenderDegradationModel> ToDegradations(
        IReadOnlyList<RekallAgeRenderFeatureDegradation> degradations) =>
        degradations.Select(item => new RekallAgeWorkbenchRenderDegradationModel(
            item.Code,
            item.Feature,
            item.RequestedValue,
            item.ResolvedValue,
            item.Message)).ToArray();

    private static string FormatTiming(RekallAgeGpuFrameTimingReport timings) =>
        timings.Available && timings.TotalMilliseconds.HasValue
            ? $"{timings.TotalMilliseconds.Value.ToString("0.000", CultureInfo.InvariantCulture)} ms"
            : "Unavailable";
}
