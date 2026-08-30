using Rekall.Age.Assets;
using System.Text.Json;

namespace Rekall.Age.Tests.Assets;

public sealed class AssetCatalogPortabilityTests
{
    [Fact]
    public async Task SavePersistsOnlyPortableLocalPathsAndLoadResolvesThemAgainstProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var importedPath = Path.Combine(root, "Assets", "texture", "hero.png");
        Directory.CreateDirectory(Path.GetDirectoryName(importedPath)!);
        await File.WriteAllTextAsync(importedPath, "asset");
        var privateSourcePath = Path.Combine(Path.GetTempPath(), "private-authoring", "hero.png");
        var catalog = new RekallAgeAssetCatalogDocument(
        [
            new RekallAgeAssetDocument(
                "hero",
                "hero",
                "Hero",
                "texture",
                privateSourcePath,
                importedPath,
                "hash")
        ]);
        var store = new RekallAgeAssetCatalogStore();

        await store.SaveAsync(root, catalog, default);

        var persisted = await File.ReadAllTextAsync(store.GetCatalogPath(root));
        var persistedAsset = Assert.Single(JsonSerializer.Deserialize<RekallAgeAssetCatalogDocument>(
            persisted,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.Assets);
        var loaded = Assert.Single((await store.LoadAsync(root, default)).Assets);
        Assert.Equal("Assets/texture/hero.png", persistedAsset.SourcePath);
        Assert.Equal("Assets/texture/hero.png", persistedAsset.ImportedPath);
        Assert.Equal(Path.GetFullPath(importedPath), loaded.SourcePath);
        Assert.Equal(Path.GetFullPath(importedPath), loaded.ImportedPath);
    }

    [Fact]
    public async Task SavePreservesRemoteSourceProvenanceWhileKeepingImportedPathPortable()
    {
        var root = TestPaths.CreateTempDirectory();
        var importedPath = Path.Combine(root, "Assets", "texture", "sky.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(importedPath)!);
        await File.WriteAllTextAsync(importedPath, "asset");
        const string sourceUrl = "https://assets.example/sky.jpg";
        var store = new RekallAgeAssetCatalogStore();

        await store.SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument("sky", "sky", "Sky", "texture", sourceUrl, importedPath, "hash")
            ]),
            default);

        var persisted = await File.ReadAllTextAsync(store.GetCatalogPath(root));
        var persistedAsset = Assert.Single(JsonSerializer.Deserialize<RekallAgeAssetCatalogDocument>(
            persisted,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.Assets);
        var loaded = Assert.Single((await store.LoadAsync(root, default)).Assets);
        Assert.Equal(sourceUrl, persistedAsset.SourcePath);
        Assert.Equal("Assets/texture/sky.jpg", persistedAsset.ImportedPath);
        Assert.Equal(sourceUrl, loaded.SourcePath);
        Assert.Equal(Path.GetFullPath(importedPath), loaded.ImportedPath);
    }

    [Fact]
    public async Task LoadRemapsLegacyAbsoluteProjectPathsAfterProjectRelocation()
    {
        var root = TestPaths.CreateTempDirectory();
        var importedPath = Path.Combine(root, "Assets", "texture", "sky.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(importedPath)!);
        await File.WriteAllTextAsync(importedPath, "asset");
        var oldRoot = Path.Combine("X:\\retired-worktree", Path.GetFileName(root));
        var legacyPath = Path.Combine(oldRoot, "Assets", "texture", "sky.jpg");
        var store = new RekallAgeAssetCatalogStore();
        await File.WriteAllTextAsync(
            store.GetCatalogPath(root),
            JsonSerializer.Serialize(
                new RekallAgeAssetCatalogDocument(
                [
                    new RekallAgeAssetDocument("sky", "sky", "Sky", "texture", legacyPath, legacyPath, "hash")
                ]),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var loaded = Assert.Single((await store.LoadAsync(root, default)).Assets);

        Assert.Equal(Path.GetFullPath(importedPath), loaded.SourcePath);
        Assert.Equal(Path.GetFullPath(importedPath), loaded.ImportedPath);
    }
}
