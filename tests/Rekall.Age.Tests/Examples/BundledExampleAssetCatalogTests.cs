using System.Runtime.CompilerServices;
using System.Text.Json;
using Rekall.Age.Assets;

namespace Rekall.Age.Tests.Examples;

public sealed class BundledExampleAssetCatalogTests
{
    [Theory]
    [InlineData("AetherfallCitadel")]
    [InlineData("RainGlass")]
    [InlineData("StellarDominion")]
    public async Task ShippedCatalogLocalPathsArePortableAndResolveInsideTheirProject(string exampleName)
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", exampleName);
        var catalogPath = new RekallAgeAssetCatalogStore().GetCatalogPath(projectRoot);
        var persisted = await File.ReadAllTextAsync(catalogPath);
        var persistedCatalog = JsonSerializer.Deserialize<RekallAgeAssetCatalogDocument>(
            persisted,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(projectRoot, default);

        foreach (var asset in persistedCatalog.Assets)
        {
            if (!Uri.TryCreate(asset.SourcePath, UriKind.Absolute, out var sourceUri) || sourceUri.IsFile)
            {
                Assert.False(Path.IsPathFullyQualified(asset.SourcePath), $"Source path for '{asset.Id}' is private or machine-specific: {asset.SourcePath}");
            }

            Assert.False(Path.IsPathFullyQualified(asset.ImportedPath), $"Imported path for '{asset.Id}' is machine-specific: {asset.ImportedPath}");
        }

        foreach (var asset in catalog.Assets)
        {
            if (!Uri.TryCreate(asset.SourcePath, UriKind.Absolute, out var sourceUri) || sourceUri.IsFile)
            {
                Assert.True(File.Exists(asset.SourcePath), $"Source for '{asset.Id}' does not resolve: {asset.SourcePath}");
                Assert.StartsWith(projectRoot, Path.GetFullPath(asset.SourcePath), StringComparison.OrdinalIgnoreCase);
            }

            var requiredPath = asset.ModelAssetMetadata is null
                ? asset.ImportedPath
                : Path.Combine(projectRoot, asset.ModelAssetMetadata.ModelDocumentPath);
            Assert.True(File.Exists(requiredPath), $"Shipped file for '{asset.Id}' does not resolve: {requiredPath}");
            Assert.StartsWith(projectRoot, Path.GetFullPath(requiredPath), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AetherfallPipelinePathsAreProjectRelativeAndResolvable()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var pipelinePath = Path.Combine(projectRoot, "Assets", "asset-pipeline.age.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(pipelinePath));

        foreach (var groupName in new[] { "sources", "imported", "cookedArtifacts" })
        {
            foreach (var entry in document.RootElement.GetProperty(groupName).EnumerateArray())
            {
                var propertyName = groupName switch
                {
                    "sources" => "sourcePath",
                    "imported" => "importedPath",
                    _ => "artifactPath"
                };
                var storedPath = entry.GetProperty(propertyName).GetString()!;
                Assert.False(Path.IsPathFullyQualified(storedPath), $"{propertyName} is machine-specific: {storedPath}");
                Assert.True(File.Exists(Path.Combine(projectRoot, storedPath)), $"{propertyName} does not resolve: {storedPath}");
            }
        }
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
