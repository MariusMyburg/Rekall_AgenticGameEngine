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
    IReadOnlyList<string> ExecutedTools);

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
                        new RepairProjectValidationResult(validation, executedTools.Count, passes, executedTools),
                        error.Message,
                        [error]);
                }

                executedTools.Add(suggestion.Tool);
            }

            validation = await ValidateAsync(request.ProjectRoot, context);
        }

        var value = new RepairProjectValidationResult(validation, executedTools.Count, passes, executedTools);
        return RekallAgeCommandResult<RepairProjectValidationResult>.Success(
            value,
            validation.IssueCount == 0
                ? $"Project validation repair executed {executedTools.Count} suggested command(s) and reached zero issues."
                : $"Project validation repair executed {executedTools.Count} suggested command(s); {validation.IssueCount} issue(s) remain.");
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
}
