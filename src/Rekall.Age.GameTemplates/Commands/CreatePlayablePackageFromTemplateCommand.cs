using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record CreatePlayablePackageFromTemplateRequest(
    string ProjectRoot,
    string ProjectName,
    string TemplateId,
    string? OutputDirectory = null,
    string SceneName = "Main",
    string? CaptureOutputDirectory = null,
    int Frames = 1,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null,
    bool Graphics = false);

public sealed record CreatePlayablePackageFromTemplateResult(
    bool Ready,
    string TemplateId,
    CreatePlayableGameFromTemplateResult Game,
    PackagePlayableGameResult Package,
    InspectPlayablePackageResult Inspection,
    RunPlayablePackageResult Run,
    CapturePlayablePackageFrameResult Capture);

public sealed class CreatePlayablePackageFromTemplateCommand
    : IRekallAgeCommand<CreatePlayablePackageFromTemplateRequest, CreatePlayablePackageFromTemplateResult>
{
    private readonly CreatePlayableGameFromTemplateCommand _createPlayableGame = new();
    private readonly PackagePlayableGameCommand _packagePlayableGame = new();
    private readonly InspectPlayablePackageCommand _inspectPackage = new();
    private readonly RunPlayablePackageCommand _runPackage = new();
    private readonly CapturePlayablePackageFrameCommand _capturePackageFrame = new();

    public string Name => "rekall.workflow.create_playable_package_from_template";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a playable template game, packages it, runs the packaged artifact, and captures a PNG proof frame.",
        typeof(CreatePlayablePackageFromTemplateRequest).FullName!,
        typeof(CreatePlayablePackageFromTemplateResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreatePlayablePackageFromTemplateResult>> ExecuteAsync(
        CreatePlayablePackageFromTemplateRequest request,
        RekallAgeCommandContext context)
    {
        var create = await _createPlayableGame.ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(
                request.ProjectRoot,
                request.ProjectName,
                request.TemplateId),
            context);
        if (!create.Ok)
        {
            return Failure(request.TemplateId, create.Value, create.Summary, create.Errors);
        }

        var package = await _packagePlayableGame.ExecuteAsync(
            new PackagePlayableGameRequest(
                request.ProjectRoot,
                request.SceneName,
                request.OutputDirectory,
                Frames: request.Frames,
                Inputs: request.Inputs,
                Assertions: null,
                Graphics: request.Graphics),
            context);
        var inspect = package.Ok
            ? await _inspectPackage.ExecuteAsync(new InspectPlayablePackageRequest(package.Value.ArchivePath), context)
            : EmptyInspection(package.Value.ManifestPath);
        var run = package.Ok
            ? await _runPackage.ExecuteAsync(
                new RunPlayablePackageRequest(package.Value.ArchivePath, request.Frames, request.Inputs),
                context)
            : EmptyRun(package.Value.ArchivePath);
        var captureOutput = request.CaptureOutputDirectory
            ?? Path.Combine(request.ProjectRoot, "Artifacts", "PackageFrames");
        var capture = run.Ok
            ? await _capturePackageFrame.ExecuteAsync(
                new CapturePlayablePackageFrameRequest(
                    package.Value.ArchivePath,
                    captureOutput,
                    FrameIndex: 1,
                    Inputs: request.Inputs),
                context)
            : EmptyCapture(captureOutput);

        var ready = package.Ok &&
            inspect.Ok &&
            inspect.Value.Ready &&
            run.Ok &&
            run.Value.Ready &&
            capture.Ok &&
            capture.Value.Captured &&
            capture.Value.NonBlank;
        var result = new CreatePlayablePackageFromTemplateResult(
            ready,
            create.Value.Template.Id,
            create.Value,
            package.Value,
            inspect.Value,
            run.Value,
            capture.Value);
        if (!ready)
        {
            return RekallAgeCommandResult<CreatePlayablePackageFromTemplateResult>.Failure(
                result,
                $"Playable package workflow for template '{request.TemplateId}' did not become ready.",
                package.Errors.Concat(inspect.Errors).Concat(run.Errors).Concat(capture.Errors).ToArray());
        }

        return RekallAgeCommandResult<CreatePlayablePackageFromTemplateResult>.Success(
            result,
            $"Created playable package for template '{request.TemplateId}'.");
    }

    private static RekallAgeCommandResult<CreatePlayablePackageFromTemplateResult> Failure(
        string templateId,
        CreatePlayableGameFromTemplateResult game,
        string summary,
        IReadOnlyList<RekallAgeCommandError> errors)
    {
        var emptyPackage = new PackagePlayableGameResult(false, string.Empty, string.Empty, string.Empty, string.Empty, [], [], string.Empty);
        return RekallAgeCommandResult<CreatePlayablePackageFromTemplateResult>.Failure(
            new CreatePlayablePackageFromTemplateResult(
                false,
                templateId,
                game,
                emptyPackage,
                EmptyInspection(string.Empty).Value,
                EmptyRun(string.Empty).Value,
                EmptyCapture(string.Empty).Value),
            summary,
            errors);
    }

    private static RekallAgeCommandResult<InspectPlayablePackageResult> EmptyInspection(string manifestPath)
    {
        return RekallAgeCommandResult<InspectPlayablePackageResult>.Success(
            new InspectPlayablePackageResult(
                false,
                manifestPath,
                new RekallAgePlayablePackageManifest(string.Empty, string.Empty, string.Empty, string.Empty, [], [], null, [], [])));
    }

    private static RekallAgeCommandResult<RunPlayablePackageResult> EmptyRun(string archivePath)
    {
        return RekallAgeCommandResult<RunPlayablePackageResult>.Success(
            new RunPlayablePackageResult(false, string.Empty, string.Empty, archivePath, -1, [], [], string.Empty));
    }

    private static RekallAgeCommandResult<CapturePlayablePackageFrameResult> EmptyCapture(string outputDirectory)
    {
        return RekallAgeCommandResult<CapturePlayablePackageFrameResult>.Success(
            new CapturePlayablePackageFrameResult(false, outputDirectory, string.Empty, 0, 0, 0, false, 0, 0, [], string.Empty));
    }
}
