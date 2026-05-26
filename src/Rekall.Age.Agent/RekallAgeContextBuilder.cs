using Rekall.Age.Core.Rendering;
using Rekall.Age.Project;
using Rekall.Age.Validation;
using Rekall.Age.World;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Rekall.Age.Agent;

public sealed class RekallAgeContextBuilder
{
    private readonly RekallAgeProjectStore _projectStore;
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeProjectValidator _validator;

    public RekallAgeContextBuilder(
        RekallAgeProjectStore projectStore,
        RekallAgeSceneStore sceneStore,
        RekallAgeProjectValidator validator)
    {
        _projectStore = projectStore;
        _sceneStore = sceneStore;
        _validator = validator;
    }

    public async ValueTask<RekallAgeProjectSummary> BuildProjectSummaryAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var manifest = await _projectStore.LoadAsync(projectRoot, cancellationToken);
        var sceneNames = _sceneStore.ListSceneNames(projectRoot);
        var blocking = new List<string>();

        foreach (var sceneName in sceneNames)
        {
            var report = await _validator.ValidateSceneAsync(projectRoot, sceneName, cancellationToken);
            blocking.AddRange(report.BlockingMessages);
        }

        var artifacts = FindArtifacts(projectRoot);
        var status = blocking.Count == 0 ? "ok" : "blocked";
        IReadOnlyList<string> nextActions = blocking.Count != 0
            ? ["rekall.workflow.fix_validation_errors"]
            : artifacts.Any(artifact => artifact.Kind == "playable-package-archive")
                ? ["rekall.workflow.audit_playable_package", "rekall.workflow.run_playable_package"]
                : ["rekall.workflow.package_playable_game", "rekall.capture.screenshot"];

        return new RekallAgeProjectSummary(
            manifest.Name,
            manifest.SourceTemplateId,
            manifest.Capabilities,
            sceneNames,
            artifacts,
            new RekallAgeProjectHealth(status, blocking),
            nextActions);
    }

    public async ValueTask<RekallAgeSceneSummary> BuildSceneSummaryAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var entities = scene.Entities
            .Select(entity => new RekallAgeEntitySummary(
                entity.Id,
                entity.Name,
                entity.Tags,
                entity.Components.Select(component => SimplifyComponentType(component.Type)).ToArray()))
            .ToArray();
        var componentTypes = entities
            .SelectMany(entity => entity.Components)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(component => component, StringComparer.Ordinal)
            .ToArray();
        var cameras = BuildCameraSummaries(scene);

        return new RekallAgeSceneSummary(
            scene.Name,
            scene.Capabilities,
            entities,
            componentTypes,
            cameras,
            BuildRenderLayerSummaries(scene),
            cameras.FirstOrDefault(camera => camera.DrivesHeadsetOutput)?.EntityName);
    }

    private static IReadOnlyList<RekallAgeSceneCameraSummary> BuildCameraSummaries(
        RekallAgeSceneDocument scene)
    {
        var cameras = scene.Entities
            .SelectMany(entity => entity.Components
                .Where(component => component.Type is "Rekall.Camera2D" or "Rekall.Camera3D")
                .Select(component => new RekallAgeSceneCameraSummary(
                    entity.Id,
                    entity.Name,
                    SimplifyComponentType(component.Type),
                    ReadBoolean(component.Properties, "active", true),
                    RekallAgeRenderLayerMask.NormalizeCullingMask(ReadString(component.Properties, "cullingMask")),
                    ReadNumber(component.Properties, "renderOrder", 0),
                    Math.Clamp(ReadNumber(component.Properties, "viewportX", 0), 0, 1),
                    Math.Clamp(ReadNumber(component.Properties, "viewportY", 0), 0, 1),
                    Math.Clamp(ReadNumber(component.Properties, "viewportWidth", 1), 0.001, 1),
                    Math.Clamp(ReadNumber(component.Properties, "viewportHeight", 1), 0.001, 1),
                    NormalizeStereoMode(ReadString(component.Properties, "stereoMode")),
                    NormalizeStereoRenderMode(ReadString(component.Properties, "stereoRenderMode")),
                    NormalizeXrViewConfiguration(ReadString(component.Properties, "xrViewConfiguration")))))
            .OrderByDescending(camera => camera.Active)
            .ThenBy(camera => camera.RenderOrder)
            .ThenBy(camera => camera.EntityName, StringComparer.Ordinal)
            .ThenBy(camera => camera.EntityId, StringComparer.Ordinal)
            .ToArray();
        var headsetCamera = cameras.FirstOrDefault(IsHeadsetCamera);
        return cameras
            .Select(camera => camera with
            {
                DrivesHeadsetOutput = headsetCamera?.EntityId.Equals(camera.EntityId, StringComparison.Ordinal) == true
            })
            .ToArray();
    }

    private static bool IsHeadsetCamera(RekallAgeSceneCameraSummary camera)
    {
        return camera.Active
            && camera.Kind.Equals("Camera3D", StringComparison.Ordinal)
            && camera.StereoMode.Equals("stereo", StringComparison.Ordinal);
    }

    private static string NormalizeStereoMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "stereo" or "vr" or "xr" => "stereo",
            _ => "mono"
        };
    }

    private static string NormalizeStereoRenderMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "side-by-side" or "side_by_side" or "sbs" => "side-by-side",
            "dual-pass" or "dual_pass" => "dual-pass",
            _ => "single-pass-multiview"
        };
    }

    private static string NormalizeXrViewConfiguration(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "primary-mono" or "mono" => "primary-mono",
            "primary-stereo-with-foveated-inset" or "foveated-stereo" => "primary-stereo-with-foveated-inset",
            _ => "primary-stereo"
        };
    }

    private static IReadOnlyList<RekallAgeSceneRenderLayerSummary> BuildRenderLayerSummaries(
        RekallAgeSceneDocument scene)
    {
        return scene.Entities
            .Where(entity => entity.Components.Any(component => IsRenderable(component.Type)))
            .GroupBy(ReadRenderLayer, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new RekallAgeSceneRenderLayerSummary(
                group.Key,
                group.Count(),
                group.Select(entity => entity.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static string ReadRenderLayer(RekallAgeEntityDocument entity)
    {
        var component = entity.Components.FirstOrDefault(item =>
            item.Type.Equals("Rekall.RenderLayer", StringComparison.Ordinal));
        return RekallAgeRenderLayerMask.NormalizeLayer(component is null
            ? null
            : ReadString(component.Properties, "layer"));
    }

    private static bool IsRenderable(string componentType)
    {
        return componentType is "Rekall.SpriteRenderer"
            or "Rekall.MeshRenderer"
            or "Rekall.MeshSet"
            or "Rekall.GeometryPrimitive"
            or "Rekall.GeometryMesh"
            or "Rekall.LineSegments"
            or "Rekall.PlanetRenderer"
            or "Rekall.OrbitPathRenderer"
            or "Rekall.RenderLight"
            or "Rekall.DirectionalLight"
            or "Rekall.PointLight";
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

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
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

    private static string SimplifyComponentType(string componentType)
    {
        const string prefix = "Rekall.";
        return componentType.StartsWith(prefix, StringComparison.Ordinal)
            ? componentType[prefix.Length..]
            : componentType;
    }

    private static IReadOnlyList<RekallAgeProjectArtifact> FindArtifacts(string projectRoot)
    {
        var fullRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(fullRoot))
        {
            return [];
        }

        var artifacts = new List<RekallAgeProjectArtifact>();
        AddArtifacts(
            artifacts,
            fullRoot,
            Directory.EnumerateFiles(fullRoot, "*.zip", SearchOption.AllDirectories),
            "playable-package-archive");
        AddArtifacts(
            artifacts,
            fullRoot,
            Directory.EnumerateFiles(fullRoot, "rekall.package.json", SearchOption.AllDirectories),
            "playable-package-manifest");

        return artifacts
            .OrderBy(artifact => artifact.Kind, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddArtifacts(
        List<RekallAgeProjectArtifact> artifacts,
        string fullRoot,
        IEnumerable<string> files,
        string kind)
    {
        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(file);
            artifacts.Add(new RekallAgeProjectArtifact(
                kind,
                NormalizePath(Path.GetRelativePath(fullRoot, fullPath)),
                new FileInfo(fullPath).Length));
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
