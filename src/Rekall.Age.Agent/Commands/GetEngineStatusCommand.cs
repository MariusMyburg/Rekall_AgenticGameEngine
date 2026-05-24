using Rekall.Age.Core.Commands;
using Rekall.Age.GameTemplates;

namespace Rekall.Age.Agent.Commands;

public sealed record GetEngineStatusRequest;

public sealed record RekallAgeAgentWorkflowTool(
    string Tool,
    string Purpose,
    bool Recommended);

public sealed record GetEngineStatusResult(
    string EngineName,
    bool AgentFirst,
    string RenderingPosture,
    IReadOnlyList<string> MvpTemplateIds,
    IReadOnlyList<RekallAgeAgentWorkflowTool> WorkflowTools);

public sealed class GetEngineStatusCommand
    : IRekallAgeCommand<GetEngineStatusRequest, GetEngineStatusResult>
{
    private readonly RekallAgeGameTemplateCatalog _templates = RekallAgeGameTemplateCatalog.CreateDefault();

    public string Name => "rekall.context.engine_status";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Returns a compact agent-readable Rekall AGE capability and workflow snapshot.",
        typeof(GetEngineStatusRequest).FullName!,
        typeof(GetEngineStatusResult).FullName!);

    public ValueTask<RekallAgeCommandResult<GetEngineStatusResult>> ExecuteAsync(
        GetEngineStatusRequest request,
        RekallAgeCommandContext context)
    {
        var result = new GetEngineStatusResult(
            EngineName: "Rekall AGE",
            AgentFirst: true,
            RenderingPosture: "Vulkan-first internal renderer with backend-neutral render plans and Direct3D extension point.",
            MvpTemplateIds: _templates.Templates.Select(template => template.Id).ToArray(),
            WorkflowTools:
            [
                new RekallAgeAgentWorkflowTool(
                    "rekall.templates.inspect",
                    "Inspect one MVP template, its draw contract, and suggested creation workflows.",
                    Recommended: true),
                new RekallAgeAgentWorkflowTool(
                    "rekall.workflow.create_playable_package_from_template",
                    "Create, build, package, run, and capture a proof frame for a playable template game in one call.",
                    Recommended: true),
                new RekallAgeAgentWorkflowTool(
                    "rekall.templates.verify_mvp",
                    "Build and playtest every MVP template, returning a readiness matrix.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.module.write_source",
                    "Replace or extend generated C# gameplay module source for custom agent-authored behavior.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.render.plan.execute",
                    "Execute backend-neutral render plans through software or Vulkan targets.",
                    Recommended: false)
            ]);
        return ValueTask.FromResult(RekallAgeCommandResult<GetEngineStatusResult>.Success(
            result,
            "Loaded Rekall AGE engine status."));
    }
}
