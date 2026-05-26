using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record RunAgentAuthoringGauntletRequest(
    string ProjectRoot,
    string ProjectName,
    string TemplateId,
    string SceneName = "Main",
    string? OutputDirectory = null,
    string? AuditOutputDirectory = null,
    int Frames = 1,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null,
    bool Graphics = false);

public sealed record RekallAgeAgentAuthoringGauntletCheck(
    string Name,
    bool Passed,
    string Summary,
    IReadOnlyList<string> RecommendedNextActions);

public sealed record RunAgentAuthoringGauntletResult(
    bool Ready,
    string TemplateId,
    string ProjectRoot,
    string SceneName,
    string PackageArchivePath,
    string ProofFramePath,
    IReadOnlyList<RekallAgeAgentAuthoringGauntletCheck> Checks,
    IReadOnlyList<string> RecommendedNextActions);

public sealed class RunAgentAuthoringGauntletCommand
    : IRekallAgeCommand<RunAgentAuthoringGauntletRequest, RunAgentAuthoringGauntletResult>
{
    private readonly CreatePlayableGameFromTemplateCommand _createPlayableGame = new();
    private readonly VerifyPlayableGameCommand _verifyPlayableGame = new();
    private readonly PackagePlayableGameCommand _packagePlayableGame = new();
    private readonly AuditPlayablePackageCommand _auditPackage = new();

    public string Name => "rekall.workflow.agent_authoring_gauntlet";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Runs the closed-loop agent authoring gauntlet: create, verify, package, audit, and report next actions for a playable template game.",
        typeof(RunAgentAuthoringGauntletRequest).FullName!,
        typeof(RunAgentAuthoringGauntletResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RunAgentAuthoringGauntletResult>> ExecuteAsync(
        RunAgentAuthoringGauntletRequest request,
        RekallAgeCommandContext context)
    {
        var frames = Math.Clamp(request.Frames, 1, 600);
        var checks = new List<RekallAgeAgentAuthoringGauntletCheck>();

        var create = await _createPlayableGame.ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(
                request.ProjectRoot,
                request.ProjectName,
                request.TemplateId),
            context);
        checks.Add(Check(
            "create-playable",
            create.Ok,
            create.Summary,
            create.Ok
                ? ["rekall.workflow.verify_playable_game"]
                : ["rekall.templates.inspect", "rekall.workflow.create_playable_game_from_template"]));
        if (!create.Ok)
        {
            return Failure(request, checks, string.Empty, string.Empty, create.Summary, create.Errors);
        }

        var verify = await _verifyPlayableGame.ExecuteAsync(
            new VerifyPlayableGameRequest(
                request.ProjectRoot,
                request.SceneName,
                frames,
                request.Inputs),
            context);
        checks.Add(Check(
            "verify-playable",
            verify.Ok && verify.Value.Ready,
            verify.Summary,
            verify.Ok && verify.Value.Ready
                ? ["rekall.workflow.package_playable_game"]
                : ["rekall.validation.scene", "rekall.build.modules", "rekall.playtest.scene"]));
        if (!verify.Ok || !verify.Value.Ready)
        {
            return Failure(request, checks, string.Empty, string.Empty, verify.Summary, verify.Errors);
        }

        var package = await _packagePlayableGame.ExecuteAsync(
            new PackagePlayableGameRequest(
                request.ProjectRoot,
                request.SceneName,
                request.OutputDirectory,
                frames,
                request.Inputs,
                Graphics: request.Graphics),
            context);
        checks.Add(Check(
            "package-playable",
            package.Ok && package.Value.Ready,
            package.Summary,
            package.Ok && package.Value.Ready
                ? ["rekall.workflow.audit_playable_package"]
                : ["rekall.workflow.verify_playable_game", "rekall.build.player"]));
        if (!package.Ok || !package.Value.Ready)
        {
            return Failure(request, checks, package.Value.ArchivePath, string.Empty, package.Summary, package.Errors);
        }

        var audit = await _auditPackage.ExecuteAsync(
            new AuditPlayablePackageRequest(
                package.Value.ArchivePath,
                request.AuditOutputDirectory,
                frames,
                FrameIndex: 1,
                Inputs: request.Inputs),
            context);
        checks.Add(Check(
            "audit-package",
            audit.Ok && audit.Value.Ready,
            audit.Summary,
            audit.Ok && audit.Value.Ready
                ? ["rekall.workflow.inspect_playable_package", "rekall.workflow.run_playable_package"]
                : ["rekall.workflow.audit_playable_package", "rekall.workflow.capture_playable_package_frame"]));

        var ready = audit.Ok && audit.Value.Ready;
        var result = BuildResult(
            request,
            ready,
            checks,
            package.Value.ArchivePath,
            audit.Value.Capture.OutputPath);
        if (!ready)
        {
            return RekallAgeCommandResult<RunAgentAuthoringGauntletResult>.Failure(
                result,
                "Agent authoring gauntlet failed.",
                audit.Errors);
        }

        return RekallAgeCommandResult<RunAgentAuthoringGauntletResult>.Success(
            result,
            $"Agent authoring gauntlet passed for template '{request.TemplateId}'.");
    }

    private static RekallAgeAgentAuthoringGauntletCheck Check(
        string name,
        bool passed,
        string summary,
        IReadOnlyList<string> nextActions)
    {
        return new RekallAgeAgentAuthoringGauntletCheck(
            name,
            passed,
            summary,
            nextActions);
    }

    private static RekallAgeCommandResult<RunAgentAuthoringGauntletResult> Failure(
        RunAgentAuthoringGauntletRequest request,
        IReadOnlyList<RekallAgeAgentAuthoringGauntletCheck> checks,
        string packageArchivePath,
        string proofFramePath,
        string summary,
        IReadOnlyList<RekallAgeCommandError> errors)
    {
        var result = BuildResult(request, false, checks, packageArchivePath, proofFramePath);
        var commandErrors = errors.Count == 0
            ? [new RekallAgeCommandError("REKALL_AGENT_AUTHORING_GAUNTLET_FAILED", summary, request.TemplateId)]
            : errors;
        return RekallAgeCommandResult<RunAgentAuthoringGauntletResult>.Failure(
            result,
            summary,
            commandErrors);
    }

    private static RunAgentAuthoringGauntletResult BuildResult(
        RunAgentAuthoringGauntletRequest request,
        bool ready,
        IReadOnlyList<RekallAgeAgentAuthoringGauntletCheck> checks,
        string packageArchivePath,
        string proofFramePath)
    {
        return new RunAgentAuthoringGauntletResult(
            ready,
            request.TemplateId,
            request.ProjectRoot,
            request.SceneName,
            packageArchivePath,
            proofFramePath,
            checks,
            checks
                .SelectMany(check => check.RecommendedNextActions)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }
}
