using Rekall.Age.Core.Commands;
using Rekall.Age.World;

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

        var hasCamera = scene.Entities.Any(entity =>
            entity.Components.Any(component =>
                component.Type.Equals("Rekall.Camera2D", StringComparison.Ordinal) ||
                component.Type.Equals("Rekall.Camera3D", StringComparison.Ordinal)));

        if (!hasCamera)
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

        var hasPlayableLoop = scene.Entities.Any(entity =>
            entity.Components.Any(component =>
                component.Type.Equals("Rekall.PlayableLoop", StringComparison.Ordinal)));

        if (!hasPlayableLoop)
        {
            issues.Add(new RekallAgeValidationIssue(
                "REKALL_PLAYABLE_LOOP_MISSING",
                $"Scene '{scene.Name}' has no playable loop marker.",
                "advisory",
                scene.Name,
                [
                    new RekallAgeSuggestedCommand(
                        "rekall.workflow.add_playable_loop",
                        new Dictionary<string, object?> { ["scene"] = scene.Name })
                ]));
        }

        return new RekallAgeValidationReport(issues);
    }
}
