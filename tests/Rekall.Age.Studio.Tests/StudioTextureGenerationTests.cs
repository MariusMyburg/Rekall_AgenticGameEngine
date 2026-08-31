using System.IO;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioTextureGenerationTests
{
    [Fact]
    public async Task AdapterExecutesRegisteredCanonicalCommandWithoutPassingAnApiKey()
    {
        var registry = new RekallAgeCommandRegistry();
        var command = new RecordingGenerateTextureCommand();
        registry.Register(command);
        var adapter = new RekallAgeStudioTextureGenerationCommand(registry);
        var options = new RekallAgeStudioTextureGenerationOptions(
            "mossy stone", "Moss", "normal", "1024x1024", "high", true);

        var result = await adapter.GenerateAsync("C:/project", options, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.NotNull(command.Request);
        Assert.Equal("mossy stone", command.Request!.Prompt);
        Assert.Equal("Moss", command.Request.DisplayName);
        Assert.Equal("normal", command.Request.TextureRole);
        Assert.True(command.Request.Seamless);
        Assert.Null(command.Request.ApiKey);
        Assert.True(command.Request.UseEnvironmentApiKey);
    }

    [Fact]
    public void DialogSourceExposesAllGenerationFieldsAndNoApiKeyField()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "GenerateTextureDialog.xaml"));
        var browser = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "ContentBrowser.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "RekallAgeStudioViewModel.cs"));

        Assert.Contains("Generate Texture…", browser, StringComparison.Ordinal);
        Assert.All(new[] { "Prompt", "Display name", "Texture role", "Size", "Quality", "Seamless" },
            label => Assert.Contains(label, xaml, StringComparison.Ordinal));
        Assert.DoesNotContain("API key", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_contentIndex.RefreshAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("_previewSession.InvalidateAssetsAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("RefreshEditPreviewAsync(result.Summary)", viewModel, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Rekall.Age.Studio"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class RecordingGenerateTextureCommand : IRekallAgeCommand<GenerateTextureRequest, GenerateTextureResult>
    {
        public GenerateTextureRequest? Request { get; private set; }
        public string Name => "rekall.asset.generate_texture";
        public RekallAgeCommandSchema Schema => new(Name, "test", typeof(GenerateTextureRequest).FullName!, typeof(GenerateTextureResult).FullName!);

        public ValueTask<RekallAgeCommandResult<GenerateTextureResult>> ExecuteAsync(GenerateTextureRequest request, RekallAgeCommandContext context)
        {
            Request = request;
            var asset = new RekallAgeAssetDocument("generated", "generated", "Generated", "texture", "openai://generated", "Assets/texture/generated.png", "hash");
            return ValueTask.FromResult(RekallAgeCommandResult<GenerateTextureResult>.Success(
                new("openai", "gpt-image-2", request.TextureRole, asset, null, []), "Generated."));
        }
    }
}
