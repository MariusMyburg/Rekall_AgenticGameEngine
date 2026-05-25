using Rekall.Age.Core.Commands;
using Rekall.Age.GameTemplates;

namespace Rekall.Age.Agent.Commands;

public sealed record GetEngineStatusRequest;

public sealed record RekallAgeAgentWorkflowTool(
    string Tool,
    string Purpose,
    bool Recommended);

public sealed record RekallAgeAgentAuthoringContract(
    string Name,
    string PrimaryType,
    string Purpose,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> RelatedTools);

public sealed record GetEngineStatusResult(
    string EngineName,
    bool AgentFirst,
    string RenderingPosture,
    IReadOnlyList<string> MvpTemplateIds,
    IReadOnlyList<RekallAgeAgentWorkflowTool> WorkflowTools,
    IReadOnlyList<RekallAgeAgentAuthoringContract> AuthoringContracts);

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
                    "rekall.workflow.audit_playable_package",
                    "Inspect a package, run deterministic frames, and capture a proof PNG for deliverable validation.",
                    Recommended: true),
                new RekallAgeAgentWorkflowTool(
                    "rekall.geometry.create_primitive",
                    "Author renderable 3D geometry primitives for dynamic blockouts, props, and scene composition.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.scene.apply_blueprint",
                    "Apply many generic entities and components to a scene in one transaction for high-throughput agent world authoring.",
                    Recommended: true),
                new RekallAgeAgentWorkflowTool(
                    "rekall.entity.delete",
                    "Remove one authored entity when iterating on generated scenes or cleaning duplicate attempts.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.module.scaffold_runtime_system",
                    "Scaffold an editable C# module containing a custom component and runtime system.",
                    Recommended: true),
                new RekallAgeAgentWorkflowTool(
                    "rekall.module.list_sources",
                    "List project module C# source files available for agent inspection or editing.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.module.read_source",
                    "Read an existing project module source file before editing it.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.templates.verify_mvp",
                    "Build and playtest every MVP template, returning a readiness matrix.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.module.write_source",
                    "Replace or extend generated C# gameplay module source for custom agent-authored behavior.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.build.modules",
                    "Compile project C# modules so their components and runtime systems can run in scene snapshots and player builds.",
                    Recommended: true),
                new RekallAgeAgentWorkflowTool(
                    "rekall.shader.write",
                    "Author project GLSL shaders with Vulkan compile validation before runtime use.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.shader.assign_pipeline",
                    "Attach validated project vertex and fragment shaders to a mesh renderer entity.",
                    Recommended: false),
                new RekallAgeAgentWorkflowTool(
                    "rekall.render.plan.execute",
                    "Execute backend-neutral render plans through software or Vulkan targets.",
                    Recommended: false)
            ],
            AuthoringContracts:
            [
                new RekallAgeAgentAuthoringContract(
                    "runtime-module-system",
                    "IRekallAgeRuntimeModuleSystem",
                    "Agent-authored C# modules implement this interface to update a runtime world each frame; game rules belong here, not in engine built-ins.",
                    ["mutate-world", "author-components", "project-subsystems", "own-game-rules"],
                    [
                        "rekall.module.scaffold_runtime_system",
                        "rekall.module.write_source",
                        "rekall.build.modules",
                        "rekall.run.scene",
                        "rekall.render.capture_runtime_viewport"
                    ]),
                new RekallAgeAgentAuthoringContract(
                    "runtime-module-sdk",
                    "RekallAgeRuntimeModuleSdk",
                    "Convenience helpers for agent-authored systems: component lookup, JSON property reads, transform edits, component upserts, and generic 3D ray queries over physics colliders.",
                    ["find-components", "read-properties", "write-components", "write-transforms", "raycast3d"],
                    [
                        "rekall.module.scaffold_runtime_system",
                        "rekall.module.write_source",
                        "rekall.build.modules",
                        "rekall.runtime.inspect_scene"
                    ]),
                new RekallAgeAgentAuthoringContract(
                    "runtime-render-mesh",
                    "RekallAgeRuntimeRenderMesh",
                    "Runtime systems append these records to project arbitrary authored renderables into viewport and player rendering.",
                    ["custom-kind", "custom-variant", "asset-id", "texture-asset", "material-color", "sort-key", "shader-pipeline"],
                    [
                        "rekall.module.write_source",
                        "rekall.shader.write",
                        "rekall.shader.validate",
                        "rekall.build.modules",
                        "rekall.render.capture_runtime_viewport"
                    ]),
                new RekallAgeAgentAuthoringContract(
                    "runtime-shader-pipeline",
                    "RekallAgeRuntimeRenderShaderPipeline",
                    "Runtime render meshes can reference authored vertex and fragment shader identifiers without hard-coded engine-specific component types.",
                    ["vertex-shader", "fragment-shader", "vulkan-validation"],
                    [
                        "rekall.shader.write",
                        "rekall.shader.validate",
                        "rekall.module.write_source",
                        "rekall.build.modules"
                    ])
            ]);
        return ValueTask.FromResult(RekallAgeCommandResult<GetEngineStatusResult>.Success(
            result,
            "Loaded Rekall AGE engine status."));
    }
}
