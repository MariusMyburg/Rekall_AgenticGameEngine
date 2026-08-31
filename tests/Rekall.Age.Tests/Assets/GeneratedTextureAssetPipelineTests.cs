using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using System.Text.Json.Nodes;

namespace Rekall.Age.Tests.Assets;

public sealed class GeneratedTextureAssetPipelineTests
{
    [Fact]
    public async Task GenerateTextureRequiresApiKeyBeforeCallingProvider()
    {
        var client = new FakeTextureClient();
        var result = await new GenerateTextureCommand(client).ExecuteAsync(
            new GenerateTextureRequest(TestPaths.CreateTempDirectory(), "mossy stone", ApiKey: null, UseEnvironmentApiKey: false),
            Context("missing texture key"));

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "TEXTURE_API_KEY_MISSING");
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task GenerateTextureImportsPngRecordsProvenanceAndDeletesStaging()
    {
        var root = TestPaths.CreateTempDirectory();
        var client = new FakeTextureClient { Bytes = Convert.FromBase64String(Png1x1) };
        var context = Context("generate texture");

        var result = await new GenerateTextureCommand(client).ExecuteAsync(
            new GenerateTextureRequest(root, "weathered copper plates", "Copper", "baseColor", "1024x1024", "high", true, "openai", "gpt-image-2", "test-key"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("texture", result.Value.Asset!.Kind);
        Assert.Equal("baseColor", result.Value.TextureRole);
        Assert.True(File.Exists(result.Value.Asset.ImportedPath));
        Assert.False(File.Exists(result.Value.StagingPath));
        Assert.Contains("OpenAI", result.Value.Asset.Provenance!.Attribution, StringComparison.Ordinal);
        Assert.Equal("gpt-image-2", client.Options!.Model);
        Assert.True(client.Options.Seamless);
        Assert.Contains(context.Transaction.ChangedResources, path => path.EndsWith("assets.age.catalog.json", StringComparison.Ordinal));
        Assert.Contains(context.Transaction.ChangedResources, path => path.EndsWith("asset-pipeline.age.json", StringComparison.Ordinal));

        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(root, CancellationToken.None);
        Assert.Contains(catalog.Assets, asset => asset.Id == result.Value.Asset.Id && asset.Provenance is not null);
    }

    [Fact]
    public void DefaultRegistryAndMcpCatalogExposeGenerateTexture()
    {
        var registry = Rekall.Age.Workflows.RekallAgeDefaultCommandRegistry.Create();
        Assert.Contains(registry.Schemas, schema => schema.Name == "rekall.asset.generate_texture");
        var catalog = Rekall.Age.Mcp.RekallAgeMcpCatalog.FromRegistry(registry);
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.asset.generate_texture");
    }

    [Fact]
    public async Task ProgressiveToolSearchFindsTextureGenerationFromUserIntent()
    {
        var registry = Rekall.Age.Workflows.RekallAgeDefaultCommandRegistry.Create();
        var executor = new Rekall.Age.Mcp.RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);

        var result = await executor.ExecuteAsync(
            "rekall.tools.search",
            new JsonObject { ["query"] = "generate custom seamless texture map" },
            CancellationToken.None);

        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Contains(result["tools"]!.AsArray(), tool => tool!["name"]!.GetValue<string>() == "rekall.asset.generate_texture");
    }

    private static RekallAgeCommandContext Context(string name) =>
        new("test", RekallAgeTransaction.Begin(name), CancellationToken.None);

    private const string Png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    private sealed class FakeTextureClient : IRekallAgeTextureGenerationClient
    {
        public int Calls { get; private set; }
        public RekallAgeTextureGenerationOptions? Options { get; private set; }
        public byte[] Bytes { get; init; } = [];

        public ValueTask<byte[]> GenerateAsync(RekallAgeTextureGenerationOptions options, string apiKey, CancellationToken cancellationToken)
        {
            Calls++;
            Options = options;
            return ValueTask.FromResult(Bytes);
        }
    }
}
