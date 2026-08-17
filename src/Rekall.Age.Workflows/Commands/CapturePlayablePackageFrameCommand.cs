using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Workflows;

namespace Rekall.Age.Workflows.Commands;

public sealed record CapturePlayablePackageFrameRequest(
    string PackagePath,
    string OutputDirectory,
    int FrameIndex = 1,
    int Width = 320,
    int Height = 180,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null);

public sealed record CapturePlayablePackageFrameResult(
    bool Captured,
    string OutputPath,
    string Kind,
    int FrameIndex,
    int Width,
    int Height,
    bool NonBlank,
    int NonBackgroundPixels,
    int DrawCommandCount,
    IReadOnlyList<string> DrawCommandKinds,
    string Text);

public sealed class CapturePlayablePackageFrameCommand
    : IRekallAgeCommand<CapturePlayablePackageFrameRequest, CapturePlayablePackageFrameResult>
{
    private readonly RunPlayablePackageCommand _runPackage = new();
    private readonly InspectPlayablePackageCommand _inspectPackage = new();
    private readonly CaptureRuntimeViewportCommand _captureViewport = new();

    public string Name => "rekall.workflow.capture_playable_package_frame";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Verifies a packaged playable launch and captures its packaged authored scene through the deterministic runtime viewport. OutputDirectory must be outside a directory package.",
        typeof(CapturePlayablePackageFrameRequest).FullName!,
        typeof(CapturePlayablePackageFrameResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CapturePlayablePackageFrameResult>> ExecuteAsync(
        CapturePlayablePackageFrameRequest request,
        RekallAgeCommandContext context)
    {
        var frameIndex = Math.Clamp(request.FrameIndex, 1, 600);
        var width = Math.Clamp(request.Width, 1, 4096);
        var height = Math.Clamp(request.Height, 1, 4096);
        if (TryCreateUnsafeOutputError(request, out var unsafeOutputError))
        {
            return RekallAgeCommandResult<CapturePlayablePackageFrameResult>.Failure(
                Empty(frameIndex, width, height, "unsafe-output"),
                unsafeOutputError.Message,
                [unsafeOutputError]);
        }

        var inspection = await _inspectPackage.ExecuteAsync(
            new InspectPlayablePackageRequest(request.PackagePath),
            context);
        if (!inspection.Ok)
        {
            return RekallAgeCommandResult<CapturePlayablePackageFrameResult>.Failure(
                Empty(frameIndex, width, height, "invalid-package"),
                inspection.Summary,
                inspection.Errors);
        }

        var run = await _runPackage.ExecuteAsync(
            new RunPlayablePackageRequest(
                request.PackagePath,
                frameIndex,
                request.Inputs),
            context);
        if (!run.Ok)
        {
            var error = new RekallAgeCommandError(
                "REKALL_PLAYABLE_PACKAGE_RUN_FAILED",
                "Packaged playable could not be launched before viewport proof capture.",
                request.PackagePath);
            return RekallAgeCommandResult<CapturePlayablePackageFrameResult>.Failure(
                Empty(frameIndex, width, height, "run-failed"),
                error.Message,
                [.. run.Errors, error]);
        }

        using var package = PreparePackage(request.PackagePath);
        var gameRoot = ResolvePackagedDirectory(package.PackageRoot, inspection.Value.Manifest.GameRoot);
        var capture = await _captureViewport.ExecuteAsync(
            new CaptureRuntimeViewportRequest(
                gameRoot,
                inspection.Value.Manifest.SceneName,
                frameIndex,
                request.OutputDirectory,
                width,
                height),
            context);
        if (!capture.Ok || !capture.Value.Captured)
        {
            return RekallAgeCommandResult<CapturePlayablePackageFrameResult>.Failure(
                Empty(frameIndex, width, height, "runtime-viewport"),
                capture.Summary,
                capture.Errors);
        }

        var viewport = capture.Value;
        var outputPath = Path.GetFullPath(Path.Combine(
            request.OutputDirectory,
            $"package_play_frame_{frameIndex:000}.png"));
        if (!Path.GetFullPath(viewport.ScreenshotPath).Equals(outputPath, PathComparison))
        {
            File.Move(viewport.ScreenshotPath, outputPath, overwrite: true);
            context.Transaction.RecordChangedResource(outputPath);
        }

        var nonBackgroundPixels = viewport.FrameAnalysis.Analyzed
            ? Math.Clamp(
                viewport.FrameAnalysis.TotalPixels - (int)Math.Round(
                    viewport.FrameAnalysis.TotalPixels * viewport.FrameAnalysis.DominantColorRatio),
                0,
                viewport.FrameAnalysis.TotalPixels)
            : 0;
        var resultValue = new CapturePlayablePackageFrameResult(
            true,
            outputPath,
            "runtime-viewport",
            viewport.FrameIndex,
            viewport.Width,
            viewport.Height,
            viewport.NonBlank && viewport.FrameAnalysis.VisuallyInformative,
            nonBackgroundPixels,
            viewport.RenderableCount,
            viewport.RenderableKinds,
            $"Captured packaged authored scene '{inspection.Value.Manifest.SceneName}' using {viewport.BackendId}.");
        return RekallAgeCommandResult<CapturePlayablePackageFrameResult>.Success(
            resultValue,
            $"Captured packaged authored scene frame {viewport.FrameIndex}.");
    }

    internal static bool TryCreateUnsafeOutputError(
        CapturePlayablePackageFrameRequest request,
        out RekallAgeCommandError error)
    {
        var packagePath = Path.GetFullPath(request.PackagePath);
        string? packageRoot = null;
        if (Directory.Exists(packagePath))
        {
            packageRoot = packagePath;
        }
        else if (File.Exists(packagePath) &&
            Path.GetFileName(packagePath).Equals("rekall.package.json", StringComparison.OrdinalIgnoreCase))
        {
            packageRoot = Path.GetDirectoryName(packagePath);
        }

        var output = Path.GetFullPath(request.OutputDirectory);
        if (packageRoot is null || !IsSameOrDescendant(output, packageRoot))
        {
            error = null!;
            return false;
        }

        var safeOutput = Path.Combine(
            Path.GetDirectoryName(packageRoot) ?? Directory.GetCurrentDirectory(),
            $"{Path.GetFileName(packageRoot)}.proof_frames");
        error = new RekallAgeCommandError(
            "REKALL_PACKAGE_PROOF_OUTPUT_UNSAFE",
            "Package proof output must be outside the immutable package directory.",
            output,
            [
                new RekallAgeSuggestedCommand(
                    "rekall.workflow.capture_playable_package_frame",
                    new Dictionary<string, object?>
                    {
                        ["packagePath"] = request.PackagePath,
                        ["outputDirectory"] = safeOutput,
                        ["frameIndex"] = request.FrameIndex,
                        ["width"] = request.Width,
                        ["height"] = request.Height,
                        ["inputs"] = request.Inputs
                    })
            ]);
        return true;
    }

    private static bool IsSameOrDescendant(string candidate, string root)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullCandidate.Equals(fullRoot, PathComparison) ||
            fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static PreparedPackage PreparePackage(string packagePath)
    {
        var fullPath = Path.GetFullPath(packagePath);
        if (Directory.Exists(fullPath))
        {
            return new PreparedPackage(fullPath, null);
        }

        if (Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var extractionRoot = Path.Combine(Path.GetTempPath(), "RekallAgePackageProof", Guid.NewGuid().ToString("N"));
            RekallAgeSafePackageExtraction.Extract(fullPath, extractionRoot);
            return new PreparedPackage(extractionRoot, extractionRoot);
        }

        return new PreparedPackage(
            Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException($"Package manifest '{fullPath}' has no parent directory."),
            null);
    }

    private static string ResolvePackagedDirectory(string packageRoot, string manifestPath)
    {
        if (!InspectPlayablePackageCommand.TryValidateRelativePath(manifestPath, out var normalized))
        {
            throw new InvalidOperationException($"Package game root path '{manifestPath}' is unsafe.");
        }

        var root = Path.GetFullPath(packageRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsSameOrDescendant(resolved, root) || !Directory.Exists(resolved))
        {
            throw new DirectoryNotFoundException($"Package game root '{resolved}' does not exist beneath the package root.");
        }

        return resolved;
    }

    private static CapturePlayablePackageFrameResult Empty(int frameIndex, int width, int height, string kind) =>
        new(false, string.Empty, kind, frameIndex, width, height, false, 0, 0, [], string.Empty);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record PreparedPackage(string PackageRoot, string? TemporaryRoot) : IDisposable
    {
        public void Dispose()
        {
            if (TemporaryRoot is null || !Directory.Exists(TemporaryRoot))
            {
                return;
            }

            try
            {
                Directory.Delete(TemporaryRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
