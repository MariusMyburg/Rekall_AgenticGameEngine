using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record ValidateRenderPlanRequest(string ProjectRoot);

public sealed record ValidateRenderPlanResult(bool Valid, IReadOnlyList<RekallAgeRenderPlanIssue> Issues);

public sealed class ValidateRenderPlanCommand : IRekallAgeCommand<ValidateRenderPlanRequest, ValidateRenderPlanResult>
{
    private readonly RekallAgeRenderPlanStore _store = new();
    private readonly RekallAgeRenderPlanValidator _validator = new();

    public string Name => "rekall.render.plan.validate";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Validates a project render plan before a backend executes it.",
        typeof(ValidateRenderPlanRequest).FullName!,
        typeof(ValidateRenderPlanResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ValidateRenderPlanResult>> ExecuteAsync(
        ValidateRenderPlanRequest request,
        RekallAgeCommandContext context)
    {
        var plan = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var report = _validator.Validate(plan);
        var result = new ValidateRenderPlanResult(report.Valid, report.Issues);
        return report.Valid
            ? RekallAgeCommandResult<ValidateRenderPlanResult>.Success(result, "Render plan is valid.")
            : RekallAgeCommandResult<ValidateRenderPlanResult>.Failure(
                result,
                "Render plan has validation issues.",
                report.Issues
                    .Select(issue => new RekallAgeCommandError(issue.Code, issue.Message, issue.Target))
                    .ToArray());
    }
}
