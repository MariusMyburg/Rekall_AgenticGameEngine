using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioTextureGenerationOptions(
    string Prompt,
    string? DisplayName,
    string TextureRole,
    string Size,
    string Quality,
    bool Seamless);

internal interface IRekallAgeStudioTextureGenerationCommand
{
    ValueTask<RekallAgeCommandResult<GenerateTextureResult>> GenerateAsync(
        string projectRoot,
        RekallAgeStudioTextureGenerationOptions options,
        CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioTextureGenerationCommand(RekallAgeCommandRegistry registry)
    : IRekallAgeStudioTextureGenerationCommand
{
    public async ValueTask<RekallAgeCommandResult<GenerateTextureResult>> GenerateAsync(
        string projectRoot,
        RekallAgeStudioTextureGenerationOptions options,
        CancellationToken cancellationToken)
    {
        var context = new RekallAgeCommandContext(
            "studio-content-browser",
            RekallAgeTransaction.Begin("Generate texture"),
            cancellationToken);
        return await registry.ExecuteAsync<GenerateTextureRequest, GenerateTextureResult>(
            "rekall.asset.generate_texture",
            new GenerateTextureRequest(
                projectRoot,
                options.Prompt,
                options.DisplayName,
                options.TextureRole,
                options.Size,
                options.Quality,
                options.Seamless,
                ApiKey: null,
                UseEnvironmentApiKey: true),
            context);
    }
}
