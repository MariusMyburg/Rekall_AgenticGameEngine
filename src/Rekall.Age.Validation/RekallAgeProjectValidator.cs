using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Rendering;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.World;
using System.Text.Json.Nodes;

namespace Rekall.Age.Validation;

public sealed class RekallAgeProjectValidator
{
    private static readonly string[] UiComponentTypes =
        ["Rekall.UiCanvas", "Rekall.UiElement", "Rekall.Panel", "Rekall.Label", "Rekall.Image", "Rekall.Button"];
    private static readonly IReadOnlyDictionary<string, RekallAgeComponentSchema> BuiltInComponentSchemas =
        RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly)
            .Components.ToDictionary(component => component.TypeName, StringComparer.Ordinal);
    private readonly RekallAgeSceneStore _sceneStore;

    public RekallAgeProjectValidator(RekallAgeSceneStore sceneStore)
    {
        _sceneStore = sceneStore;
    }

    public async ValueTask<RekallAgeValidationReport> ValidateSceneAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var issues = new List<RekallAgeValidationIssue>();
        ValidateAuthoringContracts(projectRoot, scene, issues);

        var cameras = scene.Entities
            .SelectMany(entity => entity.Components
                .Where(component => IsCamera(component.Type))
                .Select(component => new
                {
                    Entity = entity,
                    Component = component,
                    Active = ReadBoolean(component.Properties, "active", true)
                }))
            .ToArray();

        var hasActiveCameraRenderedContent = scene.Entities.Any(entity =>
            entity.Components.Any(component =>
                IsRenderable(component.Type)
                && ReadBoolean(component.Properties, "active", true)));
        if (cameras.Length == 0 && hasActiveCameraRenderedContent)
        {
            issues.Add(new RekallAgeValidationIssue(
                "REKALL_CAMERA_MISSING",
                $"Scene '{scene.Name}' has no active camera.",
                "blocking",
                scene.Name,
                [
                    new RekallAgeSuggestedCommand(
                        "rekall.workflow.fix_validation_errors",
                        new Dictionary<string, object?> { ["scene"] = scene.Name })
                ]));
        }

        var activeCameras = cameras.Where(camera => camera.Active).ToArray();
        if (activeCameras.Length > 1)
        {
            issues.Add(new RekallAgeValidationIssue(
                "REKALL_CAMERA_MULTIPLE_ACTIVE",
                $"Scene '{scene.Name}' has {activeCameras.Length} active cameras; runtime rendering will compose them by renderOrder and viewport.",
                "warning",
                scene.Name));
        }

        ValidateActiveStereoCameras(scene, activeCameras.Select(camera => (camera.Entity, camera.Component)).ToArray(), issues);
        ValidateXrScene(scene, activeCameras.Select(camera => camera.Entity).ToArray(), issues);
        ValidateRenderLayers(scene, activeCameras.Select(camera => (camera.Entity, camera.Component)).ToArray(), issues);

        return new RekallAgeValidationReport(issues);
    }

    private static void ValidateAuthoringContracts(
        string projectRoot,
        RekallAgeSceneDocument scene,
        List<RekallAgeValidationIssue> issues)
    {
        var uiElements = scene.Entities
            .Where(entity => entity.Components.Any(component =>
                component.Type is "Rekall.UiElement" or "Rekall.Panel" or "Rekall.Label" or "Rekall.Image" or "Rekall.Button"))
            .ToArray();
        var hasUiCanvas = scene.Entities.Any(entity => entity.Components.Any(component =>
            component.Type.Equals("Rekall.UiCanvas", StringComparison.Ordinal)
            && ReadBoolean(component.Properties, "active", true)));
        if (uiElements.Length > 0 && !hasUiCanvas)
        {
            issues.Add(new RekallAgeValidationIssue(
                "REKALL_UI_ELEMENT_NO_CANVAS",
                $"Scene '{scene.Name}' contains UI elements but no active Rekall.UiCanvas. Add Rekall.UiCanvas; similarly named components do not create a runtime canvas.",
                "blocking",
                scene.Name,
                [
                    new RekallAgeSuggestedCommand(
                        "rekall.scene.apply_blueprint",
                        new Dictionary<string, object?> { ["scene"] = scene.Name })
                ]));
        }

        foreach (var entity in scene.Entities)
        {
            foreach (var component in entity.Components.Where(component =>
                         component.Type.StartsWith("Rekall.", StringComparison.Ordinal)
                         && !BuiltInComponentSchemas.ContainsKey(component.Type)))
            {
                var suggestion = BuiltInComponentSchemas.Keys
                    .Select(type => (Type: type, Distance: EditDistance(component.Type, type)))
                    .OrderBy(candidate => candidate.Distance)
                    .ThenBy(candidate => candidate.Type, StringComparer.Ordinal)
                    .First();
                if (suggestion.Distance > 3)
                {
                    continue;
                }

                issues.Add(new RekallAgeValidationIssue(
                    "REKALL_COMPONENT_RESERVED_TYPE_UNKNOWN",
                    $"Entity '{entity.Name}' uses unknown reserved component '{component.Type}'. "
                    + (suggestion.Distance <= 3 ? $"Did you mean '{suggestion.Type}'? " : string.Empty)
                    + "Use the exact type returned by rekall.module.search_component_schemas.",
                    "blocking",
                    entity.Id,
                    [
                        new RekallAgeSuggestedCommand(
                            "rekall.scene.apply_blueprint",
                            new Dictionary<string, object?> { ["query"] = component.Type })
                    ]));
            }

            foreach (var component in entity.Components)
            {
                if (!BuiltInComponentSchemas.TryGetValue(component.Type, out var schema))
                {
                    continue;
                }

                var allowed = schema.Properties
                    .Select(property => property.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unknown = component.Properties
                    .Select(property => property.Key)
                    .Where(property => !allowed.Contains(property))
                    .OrderBy(property => property, StringComparer.Ordinal)
                    .ToArray();
                foreach (var propertyName in unknown)
                {
                    issues.Add(new RekallAgeValidationIssue(
                        "REKALL_COMPONENT_PROPERTY_UNKNOWN",
                        $"Entity '{entity.Name}' component '{component.Type}' contains unknown property '{propertyName}'. Allowed properties: {string.Join(", ", allowed.OrderBy(name => name, StringComparer.Ordinal))}. Unknown properties are ignored at runtime.",
                        "blocking",
                        entity.Id,
                        [
                            new RekallAgeSuggestedCommand(
                                "rekall.component.remove_property",
                                ComponentPropertyArguments(projectRoot, scene.Name, entity.Id, component.Type, propertyName)),
                            new RekallAgeSuggestedCommand(
                                "rekall.module.search_component_schemas",
                                new Dictionary<string, object?> { ["query"] = component.Type })
                        ]));
                }

                foreach (var propertySchema in schema.Properties)
                {
                    if (!TryGetPropertyValue(component.Properties, propertySchema.Name, out var node)
                        || !TryReadNumber(node, out var number)
                        || (propertySchema.Minimum is null || number >= propertySchema.Minimum)
                        && (propertySchema.Maximum is null || number <= propertySchema.Maximum))
                    {
                        continue;
                    }

                    var replacement = propertySchema.Minimum is not null && number < propertySchema.Minimum
                        ? propertySchema.Minimum.Value
                        : propertySchema.Maximum!.Value;
                    issues.Add(new RekallAgeValidationIssue(
                        "REKALL_COMPONENT_PROPERTY_OUT_OF_RANGE",
                        $"Entity '{entity.Name}' component '{component.Type}' property '{propertySchema.Name}' value {number} is outside the allowed range {FormatRange(propertySchema)}.",
                        "blocking",
                        entity.Id,
                        [
                            new RekallAgeSuggestedCommand(
                                "rekall.component.set_property",
                                ComponentPropertyArguments(projectRoot, scene.Name, entity.Id, component.Type, propertySchema.Name, replacement))
                        ]));
                }
            }
        }
    }

    private static Dictionary<string, object?> ComponentPropertyArguments(
        string projectRoot,
        string sceneName,
        string entityId,
        string componentType,
        string propertyName,
        object? value = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["projectRoot"] = projectRoot,
            ["sceneName"] = sceneName,
            ["entityId"] = entityId,
            ["componentType"] = componentType,
            ["propertyName"] = propertyName
        };
        if (value is not null)
        {
            arguments["value"] = value;
        }

        return arguments;
    }

    private static bool TryReadNumber(JsonNode? node, out double value)
    {
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<double>(out value))
            {
                return true;
            }

            if (jsonValue.TryGetValue<decimal>(out var decimalValue))
            {
                value = (double)decimalValue;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string FormatRange(RekallAgePropertySchema schema)
    {
        return schema.Minimum is not null && schema.Maximum is not null
            ? $"[{schema.Minimum}, {schema.Maximum}]"
            : schema.Minimum is not null
                ? $">= {schema.Minimum}"
                : $"<= {schema.Maximum}";
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            var current = new int[right.Length + 1];
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitution);
            }

            previous = current;
        }

        return previous[right.Length];
    }

    private static void ValidateActiveStereoCameras(
        RekallAgeSceneDocument scene,
        IReadOnlyList<(RekallAgeEntityDocument Entity, RekallAgeComponentDocument Camera)> activeCameras,
        List<RekallAgeValidationIssue> issues)
    {
        var stereoCameras = activeCameras
            .Where(camera => camera.Camera.Type.Equals("Rekall.Camera3D", StringComparison.Ordinal)
                && IsStereoMode(ReadString(camera.Camera.Properties, "stereoMode")))
            .OrderBy(camera => camera.Entity.Name, StringComparer.Ordinal)
            .ToArray();
        if (stereoCameras.Length <= 1)
        {
            return;
        }

        issues.Add(new RekallAgeValidationIssue(
            "REKALL_XR_MULTIPLE_ACTIVE_STEREO_CAMERAS",
            $"Scene '{scene.Name}' has {stereoCameras.Length} active stereo cameras ({string.Join(", ", stereoCameras.Select(camera => camera.Entity.Name))}). OpenXR headset output uses one active stereo camera; disable extra stereo cameras or make non-headset cameras mono.",
            "warning",
            scene.Name));
    }

    private static void ValidateRenderLayers(
        RekallAgeSceneDocument scene,
        IReadOnlyList<(RekallAgeEntityDocument Entity, RekallAgeComponentDocument Camera)> activeCameras,
        List<RekallAgeValidationIssue> issues)
    {
        var renderableLayers = scene.Entities
            .Where(entity => entity.Components.Any(component => IsRenderable(component.Type)
                && ReadBoolean(component.Properties, "active", true)))
            .GroupBy(ReadRenderLayer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entity => entity.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var (entity, camera) in activeCameras)
        {
            var mask = ReadString(camera.Properties, "cullingMask") ?? "*";
            foreach (var layer in RekallAgeRenderLayerMask.EnumerateIncludedLayers(mask))
            {
                if (renderableLayers.ContainsKey(layer))
                {
                    continue;
                }

                issues.Add(new RekallAgeValidationIssue(
                    "REKALL_CAMERA_CULLING_MASK_EMPTY_LAYER",
                    $"Camera '{entity.Name}' culling mask references layer '{layer}', but no active renderable uses that layer.",
                    "warning",
                    entity.Name));
            }
        }

        foreach (var (layer, entityNames) in renderableLayers)
        {
            if (activeCameras.Any(camera =>
                    RekallAgeRenderLayerMask.IncludesLayer(layer, ReadString(camera.Camera.Properties, "cullingMask"))))
            {
                continue;
            }

            issues.Add(new RekallAgeValidationIssue(
                "REKALL_RENDER_LAYER_NOT_VISIBLE",
                $"Render layer '{layer}' contains active renderables but no active camera culling mask includes it. Entities: {string.Join(", ", entityNames)}.",
                "warning",
                layer));
        }
    }

    private static void ValidateXrScene(
        RekallAgeSceneDocument scene,
        IReadOnlyList<RekallAgeEntityDocument> activeCameraEntities,
        List<RekallAgeValidationIssue> issues)
    {
        if (!scene.Capabilities.Any(capability =>
                capability.Equals("vr", StringComparison.OrdinalIgnoreCase)
                || capability.Equals("xr", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var activeXrRigs = scene.Entities
            .Where(entity => entity.Components.Any(component =>
                component.Type.Equals("Rekall.XrRig", StringComparison.Ordinal)
                && ReadBoolean(component.Properties, "active", true)))
            .ToArray();
        if (activeXrRigs.Length == 0)
        {
            issues.Add(new RekallAgeValidationIssue(
                "REKALL_XR_RIG_MISSING",
                $"VR scene '{scene.Name}' has no active Rekall.XrRig entity.",
                "warning",
                scene.Name,
                [
                    new RekallAgeSuggestedCommand(
                        "rekall.scene.apply_blueprint",
                        new Dictionary<string, object?> { ["scene"] = scene.Name })
                ]));
        }

        var active3DCameras = activeCameraEntities
            .Select(entity => new
            {
                Entity = entity,
                Camera = entity.Components.FirstOrDefault(component =>
                    component.Type.Equals("Rekall.Camera3D", StringComparison.Ordinal)
                    && ReadBoolean(component.Properties, "active", true))
            })
            .Where(item => item.Camera is not null)
            .ToArray();
        foreach (var camera in active3DCameras)
        {
            var stereoMode = ReadString(camera.Camera!.Properties, "stereoMode") ?? "mono";
            if (!IsStereoMode(stereoMode))
            {
                issues.Add(new RekallAgeValidationIssue(
                    "REKALL_XR_CAMERA_NOT_STEREO",
                    $"VR camera '{camera.Entity.Name}' should set Rekall.Camera3D stereoMode to stereo.",
                    "warning",
                    camera.Entity.Id));
            }

            var stereoRenderMode = ReadString(camera.Camera.Properties, "stereoRenderMode")
                ?? "single-pass-multiview";
            if (!stereoRenderMode.Equals("single-pass-multiview", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new RekallAgeValidationIssue(
                    "REKALL_XR_CAMERA_NOT_MULTIVIEW",
                    $"VR camera '{camera.Entity.Name}' should use single-pass-multiview rendering.",
                    "warning",
                    camera.Entity.Id));
            }

            var hasHeadPoseSource = camera.Entity.Components.Any(component =>
                component.Type.Equals("Rekall.XrPoseSource", StringComparison.Ordinal)
                && ReadBoolean(component.Properties, "active", true)
                && (ReadString(component.Properties, "source") ?? "head")
                    .Equals("head", StringComparison.OrdinalIgnoreCase));
            if (!hasHeadPoseSource)
            {
                issues.Add(new RekallAgeValidationIssue(
                    "REKALL_XR_CAMERA_POSE_SOURCE_MISSING",
                    $"VR camera '{camera.Entity.Name}' should include an active Rekall.XrPoseSource with source=head.",
                    "warning",
                    camera.Entity.Id));
            }
        }

        if (active3DCameras.Length == 0)
        {
            issues.Add(new RekallAgeValidationIssue(
                "REKALL_XR_CAMERA3D_MISSING",
                $"VR scene '{scene.Name}' has no active Rekall.Camera3D.",
                "warning",
                scene.Name));
        }

        var activeControllers = scene.Entities
            .SelectMany(entity => entity.Components
                .Where(component =>
                    component.Type.Equals("Rekall.XrController", StringComparison.Ordinal)
                    && ReadBoolean(component.Properties, "active", true))
                .Select(component => ReadString(component.Properties, "hand") ?? string.Empty))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!activeControllers.Contains("left") || !activeControllers.Contains("right"))
        {
            issues.Add(new RekallAgeValidationIssue(
                "REKALL_XR_CONTROLLERS_INCOMPLETE",
                $"VR scene '{scene.Name}' should include active left and right Rekall.XrController entities.",
                "warning",
                scene.Name));
        }
    }

    private static bool IsCamera(string type)
    {
        return type.Equals("Rekall.Camera2D", StringComparison.Ordinal)
            || type.Equals("Rekall.Camera3D", StringComparison.Ordinal);
    }

    private static bool IsStereoMode(string? stereoMode)
    {
        return stereoMode is not null
            && (stereoMode.Equals("stereo", StringComparison.OrdinalIgnoreCase)
                || stereoMode.Equals("vr", StringComparison.OrdinalIgnoreCase)
                || stereoMode.Equals("xr", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRenderable(string type)
    {
        return type is "Rekall.SpriteRenderer"
            or "Rekall.MeshRenderer"
            or "Rekall.MeshSet"
            or "Rekall.GeometryPrimitive"
            or "Rekall.PlanetRenderer"
            or "Rekall.OrbitPathRenderer"
            or "Rekall.RingRenderer"
            or "Rekall.StarfieldRenderer"
            or "Rekall.MarkerRenderer"
            or "Rekall.HaloRenderer"
            or "Rekall.TextLabelRenderer"
            or "Rekall.RenderLight";
    }

    private static string ReadRenderLayer(RekallAgeEntityDocument entity)
    {
        var component = entity.Components.FirstOrDefault(item =>
            item.Type.Equals("Rekall.RenderLayer", StringComparison.Ordinal));
        return RekallAgeRenderLayerMask.NormalizeLayer(component is null
            ? null
            : ReadString(component.Properties, "layer"));
    }

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return TryGetPropertyValue(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        if (name.Length > 0)
        {
            var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
            if (properties.TryGetPropertyValue(pascalName, out node))
            {
                return true;
            }
        }

        node = null;
        return false;
    }
}
