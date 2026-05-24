using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record ExecuteRenderPlanRequest(
    string ProjectRoot,
    string OutputDirectory);

public sealed record ExecuteRenderPlanResult(
    string OutputPath,
    bool NonBlank,
    int Width,
    int Height);

public sealed class ExecuteRenderPlanCommand
    : IRekallAgeCommand<ExecuteRenderPlanRequest, ExecuteRenderPlanResult>
{
    private readonly RekallAgeRenderPlanStore _store = new();
    private readonly RekallAgeRenderPlanValidator _validator = new();
    private readonly RekallAgeSoftwareRenderPlanExecutor _softwareExecutor = new();
    private readonly RekallAgeVulkanRenderPlanExecutor _vulkanExecutor;

    public ExecuteRenderPlanCommand()
        : this(new RekallAgeVulkanRenderPlanExecutor())
    {
    }

    public ExecuteRenderPlanCommand(IRekallAgeVulkanRenderPassCapture vulkanCapture)
        : this(new RekallAgeVulkanRenderPlanExecutor(vulkanCapture))
    {
    }

    private ExecuteRenderPlanCommand(RekallAgeVulkanRenderPlanExecutor vulkanExecutor)
    {
        _vulkanExecutor = vulkanExecutor;
    }

    public string Name => "rekall.render.plan.execute";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Executes a render plan through an internal backend and writes a deterministic render artifact.",
        typeof(ExecuteRenderPlanRequest).FullName!,
        typeof(ExecuteRenderPlanResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ExecuteRenderPlanResult>> ExecuteAsync(
        ExecuteRenderPlanRequest request,
        RekallAgeCommandContext context)
    {
        var plan = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var validation = _validator.Validate(plan);
        if (!validation.Valid)
        {
            return Failure(
                "Render plan has validation issues.",
                validation.Issues.Select(issue => new RekallAgeCommandError(
                    issue.Code,
                    issue.Message,
                    issue.Target)).ToArray());
        }

        if (!plan.BackendId.Equals("software", StringComparison.Ordinal)
            && !plan.BackendId.Equals("vulkan", StringComparison.Ordinal))
        {
            return Failure(
                $"Render backend '{plan.BackendId}' is not executable in this build.",
                [
                    new RekallAgeCommandError(
                        "REKALL_RENDER_BACKEND_NOT_EXECUTABLE",
                        $"Render backend '{plan.BackendId}' is registered for agent planning but has no executable native backend in this build.",
                        plan.BackendId)
                ]);
        }

        RekallAgeRenderPlanExecutionResult execution;
        try
        {
            execution = plan.BackendId.Equals("vulkan", StringComparison.Ordinal)
                ? await _vulkanExecutor.ExecuteAsync(plan, request.OutputDirectory, context.CancellationToken)
                : await _softwareExecutor.ExecuteAsync(plan, request.OutputDirectory, context.CancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(
                ex.Message,
                [
                    new RekallAgeCommandError(
                        "REKALL_RENDER_PLAN_EXECUTION_FAILED",
                        ex.Message,
                        plan.BackendId)
                ]);
        }

        context.Transaction.RecordChangedResource(execution.OutputPath);
        return RekallAgeCommandResult<ExecuteRenderPlanResult>.Success(
            new ExecuteRenderPlanResult(
                execution.OutputPath,
                execution.NonBlank,
                execution.Width,
                execution.Height),
            $"Executed render plan '{plan.Name}'.");
    }

    private static RekallAgeCommandResult<ExecuteRenderPlanResult> Failure(
        string summary,
        IReadOnlyList<RekallAgeCommandError> errors)
    {
        return RekallAgeCommandResult<ExecuteRenderPlanResult>.Failure(
            new ExecuteRenderPlanResult(string.Empty, false, 0, 0),
            summary,
            errors);
    }
}
