using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record PackagePlayableGameRequest(
    string ProjectRoot,
    string SceneName = "Main",
    string? OutputDirectory = null,
    int Frames = 2,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null,
    IReadOnlyList<RekallAgeFrameAssertion>? Assertions = null,
    bool Graphics = false);

public sealed record PackagePlayableGameResult(
    bool Ready,
    string OutputDirectory,
    string LaunchPath,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<RekallAgePlayableGameCheck> Checks,
    string BuildOutput);

public sealed class PackagePlayableGameCommand
    : IRekallAgeCommand<PackagePlayableGameRequest, PackagePlayableGameResult>
{
    private readonly VerifyPlayableGameCommand _verifyPlayableGame = new();
    private readonly BuildPlayerCommand _buildPlayer = new();

    public string Name => "rekall.workflow.package_playable_game";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Verifies a playable game and publishes the Rekall AGE player launch artifact.",
        typeof(PackagePlayableGameRequest).FullName!,
        typeof(PackagePlayableGameResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<PackagePlayableGameResult>> ExecuteAsync(
        PackagePlayableGameRequest request,
        RekallAgeCommandContext context)
    {
        var verification = await _verifyPlayableGame.ExecuteAsync(
            new VerifyPlayableGameRequest(
                request.ProjectRoot,
                request.SceneName,
                request.Frames,
                request.Inputs,
                request.Assertions),
            context);
        if (!verification.Ok)
        {
            return RekallAgeCommandResult<PackagePlayableGameResult>.Failure(
                new PackagePlayableGameResult(
                    Ready: false,
                    OutputDirectory: request.OutputDirectory ?? string.Empty,
                    LaunchPath: string.Empty,
                    Arguments: [],
                    Checks: verification.Value.Checks,
                    BuildOutput: string.Empty),
                verification.Summary,
                verification.Errors);
        }

        var player = await _buildPlayer.ExecuteAsync(
            new BuildPlayerRequest(request.ProjectRoot, request.SceneName, request.OutputDirectory, request.Graphics),
            context);
        var result = new PackagePlayableGameResult(
            Ready: player.Ok,
            OutputDirectory: player.Value.OutputDirectory,
            LaunchPath: player.Value.LaunchPath,
            Arguments: player.Value.Arguments,
            Checks: verification.Value.Checks,
            BuildOutput: player.Value.Output);
        if (!player.Ok)
        {
            return RekallAgeCommandResult<PackagePlayableGameResult>.Failure(
                result,
                player.Summary,
                player.Errors);
        }

        return RekallAgeCommandResult<PackagePlayableGameResult>.Success(
            result,
            $"Packaged playable game '{request.SceneName}' at '{player.Value.OutputDirectory}'.");
    }
}
