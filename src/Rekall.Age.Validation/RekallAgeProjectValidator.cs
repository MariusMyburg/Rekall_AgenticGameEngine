using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Rendering;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.World;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Rekall.Age.Validation;

public sealed class RekallAgeProjectValidator
{
    private static readonly string[] UiComponentTypes =
        ["Rekall.UiCanvas", "Rekall.UiElement", "Rekall.Panel", "Rekall.Label", "Rekall.Image", "Rekall.Button"];
    private static readonly IReadOnlyDictionary<string, RekallAgeComponentSchema> BuiltInComponentSchemas =
        RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly)
            .Components.ToDictionary(component => component.TypeName, StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string> UnqualifiedBuiltInAliases =
        BuiltInComponentSchemas.Keys.ToDictionary(
            type => type["Rekall.".Length..],
            type => type,
            StringComparer.Ordinal);
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly IRekallAgeShaderPipelineValidationService? _shaderPipelineValidation;

    public RekallAgeProjectValidator(
        RekallAgeSceneStore sceneStore,
        IRekallAgeShaderPipelineValidationService? shaderPipelineValidation = null)
    {
        _sceneStore = sceneStore;
        _shaderPipelineValidation = shaderPipelineValidation;
    }

    public async ValueTask<RekallAgeValidationReport> ValidateSceneAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var issues = new List<RekallAgeValidationIssue>();
        ValidateAuthoringContracts(projectRoot, scene, issues);
        await ValidateShaderPipelinesAsync(projectRoot, scene, issues, cancellationToken);

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
                        "rekall.module.search_component_schemas",
                        new Dictionary<string, object?>
                        {
                            ["query"] = "camera transform",
                            ["limit"] = 20
                        })
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

    private async ValueTask ValidateShaderPipelinesAsync(
        string projectRoot,
        RekallAgeSceneDocument scene,
        List<RekallAgeValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        if (_shaderPipelineValidation is null)
        {
            return;
        }

        foreach (var entity in scene.Entities)
        {
            foreach (var renderer in entity.Components.Where(component =>
                         component.Type is "Rekall.MeshRenderer" or "Rekall.MeshSet"))
            {
                var vertex = ReadString(renderer.Properties, "vertexShader");
                var fragment = ReadString(renderer.Properties, "fragmentShader");
                if (string.IsNullOrWhiteSpace(vertex) && string.IsNullOrWhiteSpace(fragment))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(vertex) || string.IsNullOrWhiteSpace(fragment))
                {
                    issues.Add(CreateShaderIssue(
                        "REKALL_SHADER_STAGE_MISSING",
                        $"Entity '{entity.Name}' must assign both vertexShader and fragmentShader.",
                        projectRoot,
                        scene.Name,
                        entity.Id,
                        vertex ?? string.Empty,
                        fragment ?? string.Empty));
                    continue;
                }

                var validation = await _shaderPipelineValidation.ValidateAsync(
                    projectRoot,
                    vertex,
                    fragment,
                    cancellationToken);
                foreach (var diagnostic in validation.Diagnostics.Take(32))
                {
                    var separator = diagnostic.IndexOf(':', StringComparison.Ordinal);
                    var code = separator > 0 ? diagnostic[..separator] : "REKALL_SHADER_PIPELINE_INVALID";
                    issues.Add(CreateShaderIssue(
                        code.StartsWith("REKALL_SHADER_", StringComparison.Ordinal)
                            ? code
                            : "REKALL_SHADER_PIPELINE_INVALID",
                        $"Entity '{entity.Name}' shader pipeline is invalid: {diagnostic}",
                        projectRoot,
                        scene.Name,
                        entity.Id,
                        vertex,
                        fragment));
                }
            }
        }
    }

    private static RekallAgeValidationIssue CreateShaderIssue(
        string code,
        string message,
        string projectRoot,
        string sceneName,
        string entityId,
        string vertex,
        string fragment) =>
        new(
            code,
            message,
            "blocking",
            entityId,
            [
                new RekallAgeSuggestedCommand(
                    "rekall.shader.inspect_pipeline",
                    new Dictionary<string, object?>
                    {
                        ["projectRoot"] = projectRoot,
                        ["vertexShader"] = vertex,
                        ["fragmentShader"] = fragment
                    }),
                new RekallAgeSuggestedCommand(
                    "rekall.shader.assign_pipeline",
                    new Dictionary<string, object?>
                    {
                        ["projectRoot"] = projectRoot,
                        ["sceneName"] = sceneName,
                        ["entityId"] = entityId,
                        ["vertexShader"] = vertex,
                        ["fragmentShader"] = fragment,
                        ["validateBeforeAssign"] = true
                    })
            ]);

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
                        "rekall.module.search_component_schemas",
                        new Dictionary<string, object?>
                        {
                            ["query"] = "UiCanvas",
                            ["limit"] = 10
                        })
                ]));
        }

        foreach (var entity in scene.Entities)
        {
            ValidateUnqualifiedBuiltInAliases(projectRoot, scene.Name, entity, issues);
            ValidatePhysicsBodyTransform(projectRoot, scene.Name, entity, "Rekall.Rigidbody3D", "Rekall.Transform3D", issues);
            ValidatePhysicsBodyTransform(projectRoot, scene.Name, entity, "Rekall.Rigidbody2D", "Rekall.Transform2D", issues);
            ValidatePhysicsBodyCollider(
                projectRoot,
                scene.Name,
                entity,
                "Rekall.Rigidbody3D",
                ["Rekall.BoxCollider3D", "Rekall.SphereCollider3D", "Rekall.CapsuleCollider3D", "Rekall.MeshCollider"],
                "Rekall.BoxCollider3D",
                issues);
            ValidatePhysicsBodyCollider(
                projectRoot,
                scene.Name,
                entity,
                "Rekall.Rigidbody2D",
                ["Rekall.BoxCollider2D", "Rekall.CircleCollider2D"],
                "Rekall.BoxCollider2D",
                issues);
            ValidatePhysicsColliderDimensions(projectRoot, scene.Name, entity, issues);

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
                            "rekall.component.add",
                            new Dictionary<string, object?>
                            {
                                ["projectRoot"] = projectRoot,
                                ["sceneName"] = scene.Name,
                                ["entityId"] = entity.Id,
                                ["componentType"] = suggestion.Type,
                                ["properties"] = component.Properties.DeepClone().AsObject()
                            }),
                        new RekallAgeSuggestedCommand(
                            "rekall.component.remove",
                            new Dictionary<string, object?>
                            {
                                ["projectRoot"] = projectRoot,
                                ["sceneName"] = scene.Name,
                                ["entityId"] = entity.Id,
                                ["componentType"] = component.Type
                            })
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

    private static void ValidateUnqualifiedBuiltInAliases(
        string projectRoot,
        string sceneName,
        RekallAgeEntityDocument entity,
        List<RekallAgeValidationIssue> issues)
    {
        foreach (var component in entity.Components)
        {
            if (!UnqualifiedBuiltInAliases.TryGetValue(component.Type, out var canonicalType))
            {
                continue;
            }

            issues.Add(new RekallAgeValidationIssue(
                "REKALL_COMPONENT_BUILTIN_PREFIX_REQUIRED",
                $"Entity '{entity.Name}' uses unqualified built-in alias '{component.Type}', which runtime treats as a custom component. Use canonical type '{canonicalType}', or choose a distinct namespace for an agent-authored custom component.",
                "blocking",
                entity.Id,
                [
                    new RekallAgeSuggestedCommand(
                        "rekall.component.remove",
                        new Dictionary<string, object?>
                        {
                            ["projectRoot"] = projectRoot,
                            ["sceneName"] = sceneName,
                            ["entityId"] = entity.Id,
                            ["componentType"] = component.Type
                        }),
                    new RekallAgeSuggestedCommand(
                        "rekall.component.add",
                        new Dictionary<string, object?>
                        {
                            ["projectRoot"] = projectRoot,
                            ["sceneName"] = sceneName,
                            ["entityId"] = entity.Id,
                            ["componentType"] = canonicalType,
                            ["properties"] = component.Properties.DeepClone().AsObject()
                        })
                ]));
        }
    }

    private static void ValidatePhysicsBodyTransform(
        string projectRoot,
        string sceneName,
        RekallAgeEntityDocument entity,
        string bodyType,
        string transformType,
        List<RekallAgeValidationIssue> issues)
    {
        if (!entity.Components.Any(component => component.Type.Equals(bodyType, StringComparison.Ordinal)) ||
            entity.Components.Any(component => component.Type.Equals(transformType, StringComparison.Ordinal)))
        {
            return;
        }

        issues.Add(new RekallAgeValidationIssue(
            "REKALL_PHYSICS_BODY_NO_TRANSFORM",
            $"Entity '{entity.Name}' has {bodyType} but no matching {transformType}. Add the transform so authored position and runtime physics state remain inspectable and composable.",
            "blocking",
            entity.Id,
            [
                new RekallAgeSuggestedCommand(
                    "rekall.component.add",
                    new Dictionary<string, object?>
                    {
                        ["projectRoot"] = projectRoot,
                        ["sceneName"] = sceneName,
                        ["entityId"] = entity.Id,
                        ["componentType"] = transformType,
                        ["properties"] = new JsonObject()
                    })
            ]));
    }

    private static void ValidatePhysicsColliderDimensions(
        string projectRoot,
        string sceneName,
        RekallAgeEntityDocument entity,
        List<RekallAgeValidationIssue> issues)
    {
        var types = entity.Components.Select(component => component.Type).ToHashSet(StringComparer.Ordinal);
        var isOnly2D = (types.Contains("Rekall.Rigidbody2D") || types.Contains("Rekall.Transform2D"))
            && !types.Contains("Rekall.Rigidbody3D")
            && !types.Contains("Rekall.Transform3D");
        var isOnly3D = (types.Contains("Rekall.Rigidbody3D") || types.Contains("Rekall.Transform3D"))
            && !types.Contains("Rekall.Rigidbody2D")
            && !types.Contains("Rekall.Transform2D");
        var replacements = isOnly2D
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Rekall.BoxCollider3D"] = "Rekall.BoxCollider2D",
                ["Rekall.SphereCollider3D"] = "Rekall.CircleCollider2D"
            }
            : isOnly3D
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Rekall.BoxCollider2D"] = "Rekall.BoxCollider3D",
                    ["Rekall.CircleCollider2D"] = "Rekall.SphereCollider3D"
                }
                : [];

        foreach (var replacement in replacements.Where(item => types.Contains(item.Key)))
        {
            issues.Add(new RekallAgeValidationIssue(
                "REKALL_PHYSICS_COLLIDER_DIMENSION_MISMATCH",
                $"Entity '{entity.Name}' uses {replacement.Key} with a {(isOnly2D ? "2D" : "3D")} transform/body contract. Replace it with {replacement.Value} so runtime collision and motion use the authored dimension.",
                "blocking",
                entity.Id,
                [
                    new RekallAgeSuggestedCommand(
                        "rekall.component.remove",
                        new Dictionary<string, object?>
                        {
                            ["projectRoot"] = projectRoot,
                            ["sceneName"] = sceneName,
                            ["entityId"] = entity.Id,
                            ["componentType"] = replacement.Key
                        }),
                    new RekallAgeSuggestedCommand(
                        "rekall.component.add",
                        new Dictionary<string, object?>
                        {
                            ["projectRoot"] = projectRoot,
                            ["sceneName"] = sceneName,
                            ["entityId"] = entity.Id,
                            ["componentType"] = replacement.Value
                        })
                ]));
        }
    }

    private static void ValidatePhysicsBodyCollider(
        string projectRoot,
        string sceneName,
        RekallAgeEntityDocument entity,
        string bodyType,
        IReadOnlyList<string> compatibleColliderTypes,
        string defaultColliderType,
        List<RekallAgeValidationIssue> issues)
    {
        if (!entity.Components.Any(component => component.Type.Equals(bodyType, StringComparison.Ordinal))
            || entity.Components.Any(component => compatibleColliderTypes.Contains(component.Type, StringComparer.Ordinal)))
        {
            return;
        }

        issues.Add(new RekallAgeValidationIssue(
            "REKALL_PHYSICS_BODY_NO_COLLIDER",
            $"Entity '{entity.Name}' has {bodyType} but no compatible collider. Runtime cannot create a simulated body without a shape; add one of: {string.Join(", ", compatibleColliderTypes.OrderBy(type => type, StringComparer.Ordinal))}.",
            "blocking",
            entity.Id,
            [
                new RekallAgeSuggestedCommand(
                    "rekall.component.add",
                    new Dictionary<string, object?>
                    {
                        ["projectRoot"] = projectRoot,
                        ["sceneName"] = sceneName,
                        ["entityId"] = entity.Id,
                        ["componentType"] = defaultColliderType,
                        ["properties"] = new JsonObject()
                    })
            ]));
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

            if (jsonValue.TryGetValue<string>(out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
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
                    $"Camera '{entity.Name}' CullingMask references layer '{layer}', but no active renderable uses that layer. Set CullingMask to '*' to include every named render layer, or list only intended layer names.",
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
                $"Render layer '{layer}' contains active renderables but no active camera CullingMask includes it. Entities: {string.Join(", ", entityNames)}. Add '{layer}' to an intended camera CullingMask or use '*' to include every named render layer.",
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
                        "rekall.module.search_component_schemas",
                        new Dictionary<string, object?>
                        {
                            ["query"] = "XrRig",
                            ["limit"] = 10
                        })
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
