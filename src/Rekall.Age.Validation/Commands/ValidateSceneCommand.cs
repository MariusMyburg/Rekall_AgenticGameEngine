using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Validation.Commands;

public sealed record ValidateSceneRequest(
    string ProjectRoot,
    string SceneName);

public sealed record ValidateSceneResult(
    string SceneName,
    string Status,
    int IssueCount,
    int BlockingCount,
    int WarningCount,
    IReadOnlyList<RekallAgeValidationIssue> Issues,
    IReadOnlyList<ValidateSceneSuggestedAction> SuggestedNextActions);

public sealed record ValidateSceneSuggestedAction(
    string Tool,
    IReadOnlyDictionary<string, object?> Arguments);

public sealed class ValidateSceneCommand : IRekallAgeCommand<ValidateSceneRequest, ValidateSceneResult>
{
    public string Name => "rekall.validation.scene";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Validates a scene and returns blocking issues, warnings, and agent-readable suggested next actions.",
        typeof(ValidateSceneRequest).FullName!,
        typeof(ValidateSceneResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ValidateSceneResult>> ExecuteAsync(
        ValidateSceneRequest request,
        RekallAgeCommandContext context)
    {
        var validator = new RekallAgeProjectValidator(new RekallAgeSceneStore());
        var report = await validator.ValidateSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            context.CancellationToken);
        var result = new ValidateSceneResult(
            request.SceneName,
            report.Status,
            report.Issues.Count,
            report.Issues.Count(issue => issue.Severity == "blocking"),
            report.Issues.Count(issue => issue.Severity == "warning"),
            report.Issues,
            report.Issues
                .SelectMany(issue => issue.SuggestedCommands ?? Array.Empty<RekallAgeSuggestedCommand>())
                .GroupBy(command => command.Tool, StringComparer.Ordinal)
                .Select(group => new ValidateSceneSuggestedAction(
                    group.Key,
                    group.First().Arguments))
                .OrderBy(action => action.Tool, StringComparer.Ordinal)
                .ToArray());

        return RekallAgeCommandResult<ValidateSceneResult>.Success(
            result,
            result.IssueCount == 0
                ? $"Scene '{request.SceneName}' passed validation."
                : $"Scene '{request.SceneName}' validation returned {result.IssueCount} issue(s).");
    }
}
