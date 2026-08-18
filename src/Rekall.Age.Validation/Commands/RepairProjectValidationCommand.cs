using System.Text.Json;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Validation.Commands;

public sealed record RepairProjectValidationRequest(
    string ProjectRoot,
    int MaxRepairPasses = 4,
    int MaxRepairs = 256);

public sealed record RepairProjectValidationResult(
    ValidateProjectResult Validation,
    int ExecutedRepairCount,
    int RepairPassCount,
    IReadOnlyList<string> ExecutedTools,
    string TerminationReason,
    int RemainingAutomaticRepairCount);

public sealed class RepairProjectValidationCommand(RekallAgeCommandRegistry registry)
    : IRekallAgeCommand<RepairProjectValidationRequest, RepairProjectValidationResult>
{
    public string Name => "rekall.validation.repair_project";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Executes the engine-generated suggested repair commands for every project validation issue in bounded passes, then returns fresh project validation. Use after deliberate fault injection or broad schema diagnostics instead of executing many independent repair calls.",
        typeof(RepairProjectValidationRequest).FullName!,
        typeof(RepairProjectValidationResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RepairProjectValidationResult>> ExecuteAsync(
        RepairProjectValidationRequest request,
        RekallAgeCommandContext context)
    {
        var maxPasses = Math.Clamp(request.MaxRepairPasses, 1, 16);
        var maxRepairs = Math.Clamp(request.MaxRepairs, 1, 1_024);
        var executedTools = new List<string>();
        var passes = 0;
        var validation = await ValidateAsync(request.ProjectRoot, context);

        while (validation.SuggestedNextActions.Count > 0
               && validation.IssueCount > 0
               && passes < maxPasses
               && executedTools.Count < maxRepairs)
        {
            passes++;
            var suggestions = validation.SuggestedNextActions
                .Where(suggestion => IsRepairMutation(suggestion.Tool))
                .Take(maxRepairs - executedTools.Count)
                .ToArray();
            if (suggestions.Length == 0)
            {
                break;
            }
            foreach (var suggestion in suggestions)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var result = await registry.ExecuteJsonAsync(
                    suggestion.Tool,
                    JsonSerializer.Serialize(suggestion.Arguments),
                    context);
                if (!result.Ok)
                {
                    var error = new RekallAgeCommandError(
                        "REKALL_VALIDATION_REPAIR_FAILED",
                        $"Suggested repair '{suggestion.Tool}' failed: {result.Summary}",
                        suggestion.Tool);
                    return RekallAgeCommandResult<RepairProjectValidationResult>.Failure(
                        new RepairProjectValidationResult(
                            validation,
                            executedTools.Count,
                            passes,
                            executedTools,
                            "repair-failed",
                            CountAutomaticRepairs(validation)),
                        error.Message,
                        [error]);
                }

                executedTools.Add(suggestion.Tool);
            }

            validation = await ValidateAsync(request.ProjectRoot, context);
        }

        var remainingAutomaticRepairs = CountAutomaticRepairs(validation);
        var terminationReason = validation.IssueCount == 0
            ? "clean"
            : remainingAutomaticRepairs == 0
                ? "no-progress"
                : executedTools.Count >= maxRepairs
                    ? "repair-limit"
                    : "pass-limit";
        var value = new RepairProjectValidationResult(
            validation,
            executedTools.Count,
            passes,
            executedTools,
            terminationReason,
            remainingAutomaticRepairs);
        return RekallAgeCommandResult<RepairProjectValidationResult>.Success(
            value,
            terminationReason switch
            {
                "clean" => $"Project validation repair executed {executedTools.Count} suggested command(s) and reached zero issues.",
                "no-progress" => $"Project validation repair executed {executedTools.Count} suggested command(s); {validation.IssueCount} issue(s) remain and no automatic repair mutations are available. Do not retry this command unchanged; inspect the remaining diagnostics and author the missing content with exact registered schemas.",
                "repair-limit" => $"Project validation repair reached the {maxRepairs}-repair limit with {validation.IssueCount} issue(s) and {remainingAutomaticRepairs} automatic repair(s) remaining.",
                _ => $"Project validation repair reached the {maxPasses}-pass limit with {validation.IssueCount} issue(s) and {remainingAutomaticRepairs} automatic repair(s) remaining."
            });
    }

    private static async ValueTask<ValidateProjectResult> ValidateAsync(
        string projectRoot,
        RekallAgeCommandContext context)
    {
        var result = await new ValidateProjectCommand().ExecuteAsync(
            new ValidateProjectRequest(projectRoot),
            context);
        return result.Value;
    }

    private static bool IsRepairMutation(string tool) => tool is
        "rekall.component.add"
        or "rekall.component.remove"
        or "rekall.component.remove_property"
        or "rekall.component.set_property";

    private static int CountAutomaticRepairs(ValidateProjectResult validation) =>
        validation.SuggestedNextActions.Count(suggestion => IsRepairMutation(suggestion.Tool));
}
