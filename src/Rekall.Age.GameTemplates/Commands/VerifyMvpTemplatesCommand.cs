using Rekall.Age.Core.Commands;
using Rekall.Age.Playback.Commands;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record VerifyMvpTemplatesRequest(
    string? WorkRoot = null,
    int Frames = 1,
    bool Cleanup = true);

public sealed record RekallAgeMvpTemplateReadiness(
    string TemplateId,
    string DisplayName,
    bool Ready,
    string ProjectRoot,
    string ModuleAssemblyPath,
    int FrameCount,
    int DrawCommandCount,
    IReadOnlyList<RekallAgeDrawCommandAssertionResult> DrawAssertions,
    string Summary);

public sealed record VerifyMvpTemplatesResult(
    bool Ready,
    IReadOnlyList<RekallAgeMvpTemplateReadiness> Templates);

public sealed class VerifyMvpTemplatesCommand
    : IRekallAgeCommand<VerifyMvpTemplatesRequest, VerifyMvpTemplatesResult>
{
    private readonly RekallAgeGameTemplateCatalog _catalog = RekallAgeGameTemplateCatalog.CreateDefault();
    private readonly CreatePlayableGameFromTemplateCommand _createPlayableGame = new();
    private readonly PlaytestSceneCommand _playtestScene = new();

    public string Name => "rekall.templates.verify_mvp";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates and playtests every built-in MVP template, returning an agent-readable readiness matrix.",
        typeof(VerifyMvpTemplatesRequest).FullName!,
        typeof(VerifyMvpTemplatesResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<VerifyMvpTemplatesResult>> ExecuteAsync(
        VerifyMvpTemplatesRequest request,
        RekallAgeCommandContext context)
    {
        var workRoot = string.IsNullOrWhiteSpace(request.WorkRoot)
            ? Path.Combine(Path.GetTempPath(), "RekallAgeMvpVerification", Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(request.WorkRoot);
        Directory.CreateDirectory(workRoot);

        try
        {
            var templates = new List<RekallAgeMvpTemplateReadiness>();
            foreach (var template in _catalog.Templates)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                templates.Add(await VerifyTemplateAsync(template, workRoot, request, context));
            }

            var ready = templates.All(template => template.Ready);
            var result = new VerifyMvpTemplatesResult(ready, templates);
            if (!ready)
            {
                return RekallAgeCommandResult<VerifyMvpTemplatesResult>.Failure(
                    result,
                    "One or more MVP templates failed readiness verification.",
                    templates
                        .Where(template => !template.Ready)
                        .Select(template => new RekallAgeCommandError(
                            "REKALL_MVP_TEMPLATE_NOT_READY",
                            template.Summary,
                            template.TemplateId))
                        .ToArray());
            }

            return RekallAgeCommandResult<VerifyMvpTemplatesResult>.Success(
                result,
                $"Verified {templates.Count} MVP template(s).");
        }
        finally
        {
            if (request.Cleanup && string.IsNullOrWhiteSpace(request.WorkRoot) && Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }
        }
    }

    private async ValueTask<RekallAgeMvpTemplateReadiness> VerifyTemplateAsync(
        RekallAgeGameTemplate template,
        string workRoot,
        VerifyMvpTemplatesRequest request,
        RekallAgeCommandContext context)
    {
        var projectRoot = Path.Combine(workRoot, template.Id);
        var create = await _createPlayableGame.ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(projectRoot, $"MVP {template.DisplayName}", template.Id),
            context);
        if (!create.Ok)
        {
            return NotReady(template, projectRoot, string.Empty, create.Summary);
        }

        var drawAssertions = template.DrawCommands
            .Select(command => new RekallAgeDrawCommandAssertion(0, command.Kind, command.Id))
            .ToArray();
        var playtest = await _playtestScene.ExecuteAsync(
            new PlaytestSceneRequest(
                projectRoot,
                "Main",
                Math.Clamp(request.Frames, 1, 600),
                DrawAssertions: drawAssertions),
            context);
        var drawCommandCount = playtest.Value.RenderFrames.FirstOrDefault()?.DrawCommands.Count ?? 0;
        return new RekallAgeMvpTemplateReadiness(
            template.Id,
            template.DisplayName,
            playtest.Ok && playtest.Value.Passed,
            projectRoot,
            create.Value.ModuleAssemblyPath,
            playtest.Value.Frames.Count,
            drawCommandCount,
            playtest.Value.DrawAssertions,
            playtest.Summary);
    }

    private static RekallAgeMvpTemplateReadiness NotReady(
        RekallAgeGameTemplate template,
        string projectRoot,
        string moduleAssemblyPath,
        string summary)
    {
        return new RekallAgeMvpTemplateReadiness(
            template.Id,
            template.DisplayName,
            false,
            projectRoot,
            moduleAssemblyPath,
            0,
            0,
            [],
            summary);
    }
}
