using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Rendering;
using Rekall.Age.World;
using System.Text.Json.Nodes;

namespace Rekall.Age.Validation;

public sealed class RekallAgeProjectValidator
{
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

        if (cameras.Length == 0)
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
