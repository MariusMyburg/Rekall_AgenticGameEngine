using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Workflows.Commands;

public sealed record RunAgentAuthoringGauntletRequest(
    string ProjectRoot,
    string ProjectName,
    string SceneName = "Main",
    string? OutputDirectory = null);

public sealed record RekallAgeAgentAuthoringGauntletCheck(
    string Name,
    bool Passed,
    string Summary);

public sealed record RunAgentAuthoringGauntletResult(
    bool Ready,
    string AuthoringMode,
    string ProjectRoot,
    string SceneName,
    PackagePlayableGameResult? Package,
    AuditPlayablePackageResult? Audit,
    IReadOnlyList<RekallAgeAgentAuthoringGauntletCheck> Checks,
    IReadOnlyList<string> NextActions);

public sealed class RunAgentAuthoringGauntletCommand
    : IRekallAgeCommand<RunAgentAuthoringGauntletRequest, RunAgentAuthoringGauntletResult>
{
    private const string AuthoringMode = "generic-primitives-and-agent-authored-module";

    private readonly CreateProjectCommand _createProject = new();
    private readonly CreateSceneCommand _createScene = new();
    private readonly ApplySceneBlueprintCommand _applyBlueprint = new();
    private readonly ScaffoldPlayableModuleCommand _scaffoldPlayableModule = new();
    private readonly WriteModuleSourceCommand _writeModuleSource = new();
    private readonly PackagePlayableGameCommand _packagePlayableGame = new();
    private readonly AuditPlayablePackageCommand _auditPlayablePackage = new();

    public string Name => "rekall.workflow.agent_authoring_gauntlet";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Authors a generic playable project, writes agent-owned module code, verifies, packages, audits, and captures a proof frame.",
        typeof(RunAgentAuthoringGauntletRequest).FullName!,
        typeof(RunAgentAuthoringGauntletResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RunAgentAuthoringGauntletResult>> ExecuteAsync(
        RunAgentAuthoringGauntletRequest request,
        RekallAgeCommandContext context)
    {
        var checks = new List<RekallAgeAgentAuthoringGauntletCheck>();
        if (string.IsNullOrWhiteSpace(request.ProjectRoot))
        {
            checks.Add(new RekallAgeAgentAuthoringGauntletCheck(
                "project-root",
                false,
                "Project root must be a non-empty path."));
            var error = new RekallAgeCommandError(
                "REKALL_AGENT_GAUNTLET_PROJECT_ROOT_INVALID",
                "Agent authoring gauntlet requires a non-empty project root.",
                request.ProjectRoot,
                [new RekallAgeSuggestedCommand("rekall.project.create", new Dictionary<string, object?>())]);
            return Failure(request, checks, null, null, "Agent authoring gauntlet could not start.", [error]);
        }

        var sceneName = string.IsNullOrWhiteSpace(request.SceneName) ? "Main" : request.SceneName.Trim();
        var projectName = string.IsNullOrWhiteSpace(request.ProjectName) ? "Agent Authored Game" : request.ProjectName.Trim();
        var projectRoot = Path.GetFullPath(request.ProjectRoot);
        var outputDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? Path.Combine(projectRoot, "Builds", "AgentAuthoringGauntlet")
                : request.OutputDirectory);

        var project = await _createProject.ExecuteAsync(
            new CreateProjectRequest(projectRoot, projectName, ["world", "rendering2d"]),
            context);
        checks.Add(ToCheck("project-created", project));
        if (!project.Ok)
        {
            return Failure(request with { ProjectRoot = projectRoot, SceneName = sceneName }, checks, null, null, project.Summary, project.Errors);
        }

        var scene = await _createScene.ExecuteAsync(
            new CreateSceneRequest(projectRoot, sceneName, ["world", "rendering2d"]),
            context);
        checks.Add(ToCheck("scene-created", scene));
        if (!scene.Ok)
        {
            return Failure(request with { ProjectRoot = projectRoot, SceneName = sceneName }, checks, null, null, scene.Summary, scene.Errors);
        }

        var blueprint = await _applyBlueprint.ExecuteAsync(
            new ApplySceneBlueprintRequest(projectRoot, sceneName, CreateGauntletBlueprint(), ClearExisting: true),
            context);
        checks.Add(ToCheck("scene-blueprint-authored", blueprint));
        if (!blueprint.Ok)
        {
            return Failure(request with { ProjectRoot = projectRoot, SceneName = sceneName }, checks, null, null, blueprint.Summary, blueprint.Errors);
        }

        var scaffold = await _scaffoldPlayableModule.ExecuteAsync(
            new ScaffoldPlayableModuleRequest(projectRoot, "agent.gauntlet", "Agent Gauntlet", "AgentGauntlet"),
            context);
        checks.Add(ToCheck("module-scaffolded", scaffold));
        if (!scaffold.Ok)
        {
            return Failure(request with { ProjectRoot = projectRoot, SceneName = sceneName }, checks, null, null, scaffold.Summary, scaffold.Errors);
        }

        var source = await _writeModuleSource.ExecuteAsync(
            new WriteModuleSourceRequest(projectRoot, "AgentGauntlet", "AgentGauntletModule.cs", CreatePlayableModuleSource()),
            context);
        checks.Add(ToCheck("module-source-authored", source));
        if (!source.Ok)
        {
            return Failure(request with { ProjectRoot = projectRoot, SceneName = sceneName }, checks, null, null, source.Summary, source.Errors);
        }

        var package = await _packagePlayableGame.ExecuteAsync(
            new PackagePlayableGameRequest(
                projectRoot,
                sceneName,
                outputDirectory,
                Frames: 2,
                Inputs: [new RekallAgePlaybackInput(1, PrimaryAction: true)],
                Assertions:
                [
                    new RekallAgeFrameAssertion(0, "AGENT GAUNTLET"),
                    new RekallAgeFrameAssertion(0, "Score 10")
                ]),
            context);
        checks.Add(ToCheck("package-created", package));
        if (!package.Ok)
        {
            return Failure(request with { ProjectRoot = projectRoot, SceneName = sceneName }, checks, package.Value, null, package.Summary, package.Errors);
        }

        var audit = await _auditPlayablePackage.ExecuteAsync(
            new AuditPlayablePackageRequest(
                package.Value.ArchivePath,
                Path.Combine(projectRoot, "Builds", "AgentAuthoringGauntletAudit"),
                Frames: 1,
                FrameIndex: 1,
                Inputs: [new RekallAgePlaybackInput(1, PrimaryAction: true)]),
            context);
        checks.Add(ToCheck("package-audited", audit));
        if (!audit.Ok)
        {
            return Failure(request with { ProjectRoot = projectRoot, SceneName = sceneName }, checks, package.Value, audit.Value, audit.Summary, audit.Errors);
        }

        var result = new RunAgentAuthoringGauntletResult(
            Ready: true,
            AuthoringMode,
            projectRoot,
            sceneName,
            package.Value,
            audit.Value,
            checks,
            SuccessNextActions);
        return RekallAgeCommandResult<RunAgentAuthoringGauntletResult>.Success(
            result,
            $"Agent authoring gauntlet proved generic playable project '{projectName}'.");
    }

    private static IReadOnlyList<RekallAgeSceneBlueprintEntity> CreateGauntletBlueprint()
    {
        return
        [
            new RekallAgeSceneBlueprintEntity(
                "Camera",
                ["camera"],
                [
                    new RekallAgeSceneBlueprintComponent(
                        "Rekall.Camera2D",
                        new JsonObject
                        {
                            ["active"] = true,
                            ["renderOrder"] = 0
                        })
                ]),
            new RekallAgeSceneBlueprintEntity(
                "Agent Authored Marker",
                ["agent-authored", "proof"],
                [
                    new RekallAgeSceneBlueprintComponent(
                        "Rekall.Transform2D",
                        new JsonObject
                        {
                            ["x"] = 160,
                            ["y"] = 90
                        }),
                    new RekallAgeSceneBlueprintComponent(
                        "Rekall.RenderLayer",
                        new JsonObject
                        {
                            ["layer"] = "world"
                        }),
                    new RekallAgeSceneBlueprintComponent(
                        "Rekall.SpriteRenderer",
                        new JsonObject
                        {
                            ["sprite"] = "agent-authored-marker",
                            ["color"] = "#4bd4a1"
                        })
                ])
        ];
    }

    private static string CreatePlayableModuleSource()
    {
        return """
using Rekall.Age.Modules;

namespace Game.Modules.AgentGauntlet;

[RekallAgeModule("agent.gauntlet", "Agent Gauntlet")]
[RekallAgeRequiresCapability("world")]
public sealed class AgentGauntletModule : RekallAgeModule, IRekallAgePlayableModule
{
    public string Kind => "agent-authored-gauntlet";

    public override void Configure(RekallAgeModuleBuilder builder)
    {
    }

    public RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context)
    {
        var state = new RekallAgePlayableModuleState();
        state.Numbers["score"] = 0;
        state.Numbers["lane"] = 0;
        return state;
    }

    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)
    {
        state.Numbers["lane"] += input.VerticalAxis;
        if (input.PrimaryAction)
        {
            state.Numbers["score"] += 10;
        }
    }

    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)
    {
        var score = (int)state.Numbers["score"];
        var lane = (int)state.Numbers["lane"];
        var drawCommands = new[]
        {
            new RekallAgePlayableDrawCommand("clear", "background", 0, 0, 320, 180, "#0d1420"),
            new RekallAgePlayableDrawCommand("rect", "agent-authored-marker", 130 + lane * 8, 70, 60, 38, "#4bd4a1"),
            new RekallAgePlayableDrawCommand("circle", "agent-proof-orbit", 198, 88, 20, 20, "#f8d66d"),
            new RekallAgePlayableDrawCommand("text", "agent-proof-hud", 10, 10, 0, 0, "#ffffff", $"Score {score}")
        };
        return new RekallAgePlayableModuleFrame($"AGENT GAUNTLET\nScore {score}\nLane {lane}", drawCommands);
    }
}
""";
    }

    private static RekallAgeAgentAuthoringGauntletCheck ToCheck<T>(
        string name,
        RekallAgeCommandResult<T> result)
    {
        return new RekallAgeAgentAuthoringGauntletCheck(name, result.Ok, result.Summary);
    }

    private static RekallAgeCommandResult<RunAgentAuthoringGauntletResult> Failure(
        RunAgentAuthoringGauntletRequest request,
        IReadOnlyList<RekallAgeAgentAuthoringGauntletCheck> checks,
        PackagePlayableGameResult? package,
        AuditPlayablePackageResult? audit,
        string summary,
        IReadOnlyList<RekallAgeCommandError> errors)
    {
        return RekallAgeCommandResult<RunAgentAuthoringGauntletResult>.Failure(
            new RunAgentAuthoringGauntletResult(
                Ready: false,
                AuthoringMode,
                request.ProjectRoot,
                string.IsNullOrWhiteSpace(request.SceneName) ? "Main" : request.SceneName,
                package,
                audit,
                checks,
                FailureNextActions),
            summary,
            errors);
    }

    private static readonly string[] SuccessNextActions =
    [
        "rekall.workflow.inspect_playable_package",
        "rekall.workflow.run_playable_package",
        "rekall.workflow.capture_playable_package_frame",
        "rekall.workflow.audit_playable_package"
    ];

    private static readonly string[] FailureNextActions =
    [
        "rekall.project.create",
        "rekall.scene.apply_blueprint",
        "rekall.module.write_source",
        "rekall.build.modules",
        "rekall.workflow.package_playable_game"
    ];
}
