using System.Text;
using System.Text.Json;
using Rekall.Age.Core.Product;
using Rekall.Age.Workflows.Web;

namespace Rekall.Age.Tests.Workflows;

public sealed class WebGameExporterTests
{
    [Fact]
    public async Task StagesOnlyTheDeterministicDeclarativeEntrySceneClosure()
    {
        var first = await CreateAndStageProjectAsync("first");
        var second = await CreateAndStageProjectAsync("second");

        Assert.Equal(first.Manifest.BuildIdentity, second.Manifest.BuildIdentity);
        Assert.Equal(
            await File.ReadAllBytesAsync(first.ManifestPath),
            await File.ReadAllBytesAsync(second.ManifestPath));
        Assert.Equal(
            [
                "Assets/Imported/hero.png",
                "Assets/assets.age.catalog.json",
                "Scenes/Main.age.scene.json",
                "rekall.project.json"
            ],
            first.Manifest.Content.Select(entry => entry.Path));
        Assert.True(File.Exists(Path.Combine(first.OutputDirectory, "Assets", "Imported", "hero.png")));
        Assert.False(File.Exists(Path.Combine(first.OutputDirectory, "Assets", "Imported", "unused.png")));
        Assert.False(File.Exists(Path.Combine(first.OutputDirectory, "index.html")));
        Assert.Empty(first.Manifest.Modules);
        Assert.Equal(["rendering2d", "ui"], first.Manifest.RequiredRenderingCapabilities);
        var stagedCatalog = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(first.OutputDirectory, "Assets", "assets.age.catalog.json")));
        var stagedAsset = Assert.Single(stagedCatalog.RootElement.GetProperty("assets").EnumerateArray());
        Assert.Equal("asset.hero", stagedAsset.GetProperty("id").GetString());
        Assert.Equal("Assets/Imported/hero.png", stagedAsset.GetProperty("importedPath").GetString());
    }

    [Fact]
    public async Task RejectsAnAssetCatalogPathThatEscapesTheProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = TestPaths.CreateTempDirectory();
        Directory.Delete(output);
        await WriteProjectAsync(root, "../outside.png");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RekallAgeWebGameExporter().StageAsync(
                new RekallAgeWebGameStageRequest(root, "Main", output),
                CancellationToken.None).AsTask());

        Assert.Contains("outside the project root", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task RejectsAnExistingOrProjectOverlappingOutputWithoutDeletingIt()
    {
        var root = TestPaths.CreateTempDirectory();
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        var existing = TestPaths.CreateTempDirectory();
        var marker = Path.Combine(existing, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RekallAgeWebGameExporter().StageAsync(
                new RekallAgeWebGameStageRequest(root, "Main", existing),
                CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RekallAgeWebGameExporter().StageAsync(
                new RekallAgeWebGameStageRequest(root, "Main", Path.Combine(root, "Builds", "Web")),
                CancellationToken.None).AsTask());

        Assert.Equal("keep", await File.ReadAllTextAsync(marker));
    }

    [Fact]
    public async Task RejectsADeclarativeClosureBeyondTheEntryLimitBeforeReadingAssets()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = TestPaths.CreateTempDirectory();
        Directory.Delete(output);
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        var assetCount = RekallAgeWebGameExporter.MaximumContentEntries;
        var ids = Enumerable.Range(0, assetCount).Select(index => $"asset.{index:D5}").ToArray();
        var scene = new
        {
            id = "scene_main",
            name = "Main",
            schemaVersion = 1,
            capabilities = new[] { "world" },
            entities = new[]
            {
                new
                {
                    id = "refs", name = "References", tags = Array.Empty<string>(),
                    components = new[] { new { type = "Game.References", properties = new { assetIds = ids } } },
                    parentId = (string?)null, prefabSourceId = (string?)null, visible = true, locked = false
                }
            }
        };
        var assets = ids.Select(id => new
        {
            id,
            name = id,
            displayName = id,
            kind = "sprite",
            sourcePath = string.Empty,
            importedPath = $"Assets/Imported/{id}.png",
            contentHash = new string('a', 64)
        }).ToArray();
        await File.WriteAllTextAsync(
            Path.Combine(root, "Scenes", "Main.age.scene.json"),
            JsonSerializer.Serialize(scene));
        await File.WriteAllTextAsync(
            Path.Combine(root, "Assets", "assets.age.catalog.json"),
            JsonSerializer.Serialize(new { assets }));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RekallAgeWebGameExporter().StageAsync(
                new RekallAgeWebGameStageRequest(root, "Main", output),
                CancellationToken.None).AsTask());

        Assert.Contains("content-entry limit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output));
    }

    private static async Task<RekallAgeWebGameStageResult> CreateAndStageProjectAsync(string suffix)
    {
        var root = TestPaths.CreateTempDirectory();
        var output = TestPaths.CreateTempDirectory();
        Directory.Delete(output);
        var importedPath = suffix.Equals("second", StringComparison.Ordinal)
            ? Path.Combine(root, "Assets", "Imported", "hero.png")
            : "Assets/Imported/hero.png";
        await WriteProjectAsync(root, importedPath);
        return await new RekallAgeWebGameExporter().StageAsync(
            new RekallAgeWebGameStageRequest(root, "Main", output),
            CancellationToken.None);
    }

    private static async Task WriteProjectAsync(string root, string importedPath)
    {
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Imported"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "rekall.project.json"),
            """
            {"name":"Closure Game","schemaVersion":1,"capabilities":["world","rendering2d","ui"]}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Scenes", "Main.age.scene.json"),
            """
            {"id":"scene_main","name":"Main","schemaVersion":1,"capabilities":["world","rendering2d","ui"],"entities":[{"id":"hero","name":"Hero","tags":[],"components":[{"type":"Rekall.SpriteRenderer","properties":{"assetId":"asset.hero"}}],"parentId":null,"prefabSourceId":null,"visible":true,"locked":false}]}
            """);
        var catalog = new
        {
            assets = new object[]
            {
                new { id = "asset.hero", name = "hero", displayName = "Hero", kind = "sprite", sourcePath = "hero.png", importedPath, contentHash = new string('a', 64) },
                new { id = "asset.unused", name = "unused", displayName = "Unused", kind = "sprite", sourcePath = "unused.png", importedPath = "Assets/Imported/unused.png", contentHash = new string('b', 64) }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(root, "Assets", "assets.age.catalog.json"),
            JsonSerializer.Serialize(catalog));
        await File.WriteAllBytesAsync(Path.Combine(root, "Assets", "Imported", "hero.png"), Encoding.UTF8.GetBytes("hero-image"));
        await File.WriteAllBytesAsync(Path.Combine(root, "Assets", "Imported", "unused.png"), Encoding.UTF8.GetBytes("unused-image"));
    }
}
