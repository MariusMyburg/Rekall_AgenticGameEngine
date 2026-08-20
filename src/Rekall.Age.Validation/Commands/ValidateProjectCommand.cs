using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Validation.Commands;

public sealed record ValidateProjectRequest(string ProjectRoot);

public sealed record ValidateProjectSceneResult(
    string SceneName,
    string Status,
    int IssueCount,
    int BlockingCount,
    int WarningCount,
    IReadOnlyList<RekallAgeValidationIssue> Issues);

public sealed record ValidateProjectResult(
    string ProjectRoot,
    string Status,
    int SceneCount,
    int IssueCount,
    int BlockingCount,
    int WarningCount,
    IReadOnlyList<ValidateProjectSceneResult> Scenes,
    IReadOnlyList<RekallAgeSuggestedCommand> SuggestedNextActions);

public sealed class ValidateProjectCommand
    : IRekallAgeCommand<ValidateProjectRequest, ValidateProjectResult>
{
    private readonly RekallAgeSceneStore _sceneStore = new();
    private readonly IRekallAgeShaderPipelineValidationService? _shaderPipelineValidation;

    public ValidateProjectCommand(IRekallAgeShaderPipelineValidationService? shaderPipelineValidation = null)
    {
        _shaderPipelineValidation = shaderPipelineValidation;
    }

    public string Name => "rekall.validation.project";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Validates every authored scene in a Rekall AGE project, including component schemas, and returns aggregated issues and executable repair actions.",
        typeof(ValidateProjectRequest).FullName!,
        typeof(ValidateProjectResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ValidateProjectResult>> ExecuteAsync(
        ValidateProjectRequest request,
        RekallAgeCommandContext context)
    {
        var projectRoot = Path.GetFullPath(request.ProjectRoot);
        var sceneNames = _sceneStore.ListSceneNames(projectRoot);
        if (sceneNames.Count == 0)
        {
            var suggestion = new RekallAgeSuggestedCommand(
                "rekall.scene.create",
                new Dictionary<string, object?>
                {
                    ["projectRoot"] = projectRoot,
                    ["name"] = "Main",
                    ["capabilities"] = new[] { "world" }
                });
            var result = new ValidateProjectResult(
                projectRoot,
                "blocked",
                0,
                1,
                1,
                0,
                [],
                [suggestion]);
            return RekallAgeCommandResult<ValidateProjectResult>.Success(
                result,
                "Project validation found no authored scenes.");
        }

        var validator = new RekallAgeProjectValidator(_sceneStore, _shaderPipelineValidation);
        var scenes = new List<ValidateProjectSceneResult>(sceneNames.Count);
        foreach (var sceneName in sceneNames)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var report = await validator.ValidateSceneAsync(
                projectRoot,
                sceneName,
                context.CancellationToken);
            scenes.Add(new ValidateProjectSceneResult(
                sceneName,
                report.Status,
                report.Issues.Count,
                report.Issues.Count(issue => issue.Severity == "blocking"),
                report.Issues.Count(issue => issue.Severity == "warning"),
                report.Issues));
        }

        var issues = scenes.SelectMany(scene => scene.Issues).ToArray();
        var suggestions = issues
            .SelectMany(issue => issue.SuggestedCommands ?? [])
            .ToArray();
        var blockingCount = issues.Count(issue => issue.Severity == "blocking");
        var resultValue = new ValidateProjectResult(
            projectRoot,
            blockingCount > 0 ? "blocked" : "ok",
            scenes.Count,
            issues.Length,
            blockingCount,
            issues.Count(issue => issue.Severity == "warning"),
            scenes,
            suggestions);
        return RekallAgeCommandResult<ValidateProjectResult>.Success(
            resultValue,
            issues.Length == 0
                ? $"Project passed validation across {scenes.Count} scene(s)."
                : $"Project validation returned {issues.Length} issue(s) across {scenes.Count} scene(s).");
    }
}
