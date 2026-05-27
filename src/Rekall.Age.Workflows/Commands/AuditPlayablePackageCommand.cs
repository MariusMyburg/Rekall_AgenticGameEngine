using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;

namespace Rekall.Age.Workflows.Commands;

public sealed record AuditPlayablePackageRequest(
    string PackagePath,
    string? OutputDirectory = null,
    int Frames = 1,
    int FrameIndex = 1,
    int Width = 320,
    int Height = 180,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null);

public sealed record RekallAgePlayablePackageAuditCheck(
    string Name,
    bool Passed,
    string Summary);

public sealed record AuditPlayablePackageResult(
    bool Ready,
    InspectPlayablePackageResult Inspection,
    RunPlayablePackageResult Run,
    CapturePlayablePackageFrameResult Capture,
    IReadOnlyList<string> RequiredKeyArtifacts,
    IReadOnlyList<string> MissingKeyArtifacts,
    IReadOnlyList<RekallAgePlayablePackageAuditCheck> Checks);

public sealed class AuditPlayablePackageCommand
    : IRekallAgeCommand<AuditPlayablePackageRequest, AuditPlayablePackageResult>
{
    private static readonly string[] RequiredKeyArtifacts =
    [
        "rekall.package.json",
        "Game/rekall.project.json",
        "Game/Scenes/*.age.scene.json",
        "*.dll|*.exe"
    ];

    private readonly InspectPlayablePackageCommand _inspectPackage = new();
    private readonly RunPlayablePackageCommand _runPackage = new();
    private readonly CapturePlayablePackageFrameCommand _captureFrame = new();

    public string Name => "rekall.workflow.audit_playable_package";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Audits a packaged playable game by inspecting contents, running deterministic frames, and capturing a PNG proof frame.",
        typeof(AuditPlayablePackageRequest).FullName!,
        typeof(AuditPlayablePackageResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AuditPlayablePackageResult>> ExecuteAsync(
        AuditPlayablePackageRequest request,
        RekallAgeCommandContext context)
    {
        var frameCount = Math.Clamp(request.Frames, 1, 600);
        var frameIndex = Math.Clamp(request.FrameIndex, 1, frameCount);
        var outputDirectory = request.OutputDirectory ?? ResolveDefaultOutputDirectory(request.PackagePath);

        var inspection = await _inspectPackage.ExecuteAsync(
            new InspectPlayablePackageRequest(request.PackagePath),
            context);
        var missingKeyArtifacts = FindMissingKeyArtifacts(inspection.Value.Files);

        var run = await _runPackage.ExecuteAsync(
            new RunPlayablePackageRequest(request.PackagePath, frameCount, request.Inputs),
            context);
        var capture = await _captureFrame.ExecuteAsync(
            new CapturePlayablePackageFrameRequest(
                request.PackagePath,
                outputDirectory,
                frameIndex,
                request.Width,
                request.Height,
                request.Inputs),
            context);

        var checks = new[]
        {
            new RekallAgePlayablePackageAuditCheck(
                "manifest-ready",
                inspection.Value.Ready,
                inspection.Value.Ready ? "Package manifest readiness checks passed." : "Package manifest readiness checks failed."),
            new RekallAgePlayablePackageAuditCheck(
                "key-artifacts",
                missingKeyArtifacts.Count == 0,
                missingKeyArtifacts.Count == 0
                    ? "All required package artifacts are present."
                    : $"Missing required package artifacts: {string.Join(", ", missingKeyArtifacts)}."),
            new RekallAgePlayablePackageAuditCheck(
                "run",
                run.Value.Ready,
                run.Value.Ready ? "Packaged player ran successfully." : "Packaged player run failed."),
            new RekallAgePlayablePackageAuditCheck(
                "capture",
                capture.Value.Captured,
                capture.Value.Captured ? "Package proof frame was captured." : "Package proof frame was not captured."),
            new RekallAgePlayablePackageAuditCheck(
                "nonblank-frame",
                capture.Value.NonBlank,
                capture.Value.NonBlank ? "Package proof frame is non-blank." : "Package proof frame is blank.")
        };
        var ready = checks.All(check => check.Passed);
        var result = new AuditPlayablePackageResult(
            ready,
            inspection.Value,
            run.Value,
            capture.Value,
            RequiredKeyArtifacts,
            missingKeyArtifacts,
            checks);

        if (!ready)
        {
            return RekallAgeCommandResult<AuditPlayablePackageResult>.Failure(
                result,
                "Playable package audit failed.",
                [
                    .. inspection.Errors,
                    .. run.Errors,
                    .. capture.Errors,
                    .. checks
                        .Where(check => !check.Passed)
                        .Select(check => new RekallAgeCommandError("REKALL_PLAYABLE_PACKAGE_AUDIT_FAILED", check.Summary, check.Name))
                ]);
        }

        return RekallAgeCommandResult<AuditPlayablePackageResult>.Success(
            result,
            "Playable package audit passed.");
    }

    private static IReadOnlyList<string> FindMissingKeyArtifacts(IReadOnlyList<RekallAgePlayablePackageFile> files)
    {
        var missing = new List<string>();
        if (!files.Any(file => file.Path.Equals("rekall.package.json", StringComparison.Ordinal)))
        {
            missing.Add("rekall.package.json");
        }

        if (!files.Any(file => file.Path.Equals("Game/rekall.project.json", StringComparison.Ordinal)))
        {
            missing.Add("Game/rekall.project.json");
        }

        if (!files.Any(file => file.Path.StartsWith("Game/Scenes/", StringComparison.Ordinal) &&
            file.Path.EndsWith(".age.scene.json", StringComparison.Ordinal)))
        {
            missing.Add("Game/Scenes/*.age.scene.json");
        }

        if (!files.Any(file => file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            file.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            missing.Add("*.dll|*.exe");
        }

        return missing;
    }

    private static string ResolveDefaultOutputDirectory(string packagePath)
    {
        var fullPath = Path.GetFullPath(packagePath);
        if (Directory.Exists(fullPath))
        {
            return Path.Combine(Path.GetDirectoryName(fullPath)!, $"{Path.GetFileName(fullPath)}.audit_frames");
        }

        var parent = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(fullPath);
        if (Path.GetFileName(fullPath).Equals("rekall.package.json", StringComparison.OrdinalIgnoreCase))
        {
            name = $"{Path.GetFileName(parent)}.package";
            parent = Path.GetDirectoryName(parent) ?? parent;
        }

        return Path.Combine(parent, $"{name}.audit_frames");
    }
}
