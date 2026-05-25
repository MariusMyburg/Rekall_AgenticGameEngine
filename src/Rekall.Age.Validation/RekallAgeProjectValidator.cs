using Rekall.Age.Core.Commands;
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
                $"Scene '{scene.Name}' has {activeCameras.Length} active cameras; the runtime will choose one deterministically.",
                "warning",
                scene.Name));
        }

        return new RekallAgeValidationReport(issues);
    }

    private static bool IsCamera(string type)
    {
        return type.Equals("Rekall.Camera2D", StringComparison.Ordinal)
            || type.Equals("Rekall.Camera3D", StringComparison.Ordinal);
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
