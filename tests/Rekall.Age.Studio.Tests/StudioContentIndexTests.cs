using Rekall.Age.Editor.Contracts;
using Rekall.Age.Studio;
using System.IO;
using Rekall.Age.Assets;
using Rekall.Age.Modeling;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioContentIndexTests
{
    [Fact]
    public async Task RefreshDeduplicatesExactKindAndIdWhileKeepingDifferentKindsWithSameId()
    {
        var duplicate = Item("shared", "Zulu", "texture");
        var index = new RekallAgeStudioContentIndex([
            new StubSource("imported", [duplicate, Item("b", "Beta", "model")]),
            new StubSource("authored", [duplicate with { DisplayName = "Ignored" }, Item("shared", "Shared Model", "model"), Item("a", "Alpha", "model")])
        ]);

        var result = await index.RefreshAsync("C:\\project", CancellationToken.None);

        Assert.Equal(4, result.Items.Count);
        Assert.Equal("Zulu", Assert.Single(result.Items, item => item.Id == "shared" && item.Kind == "texture").DisplayName);
        Assert.Contains(result.Items, item => item.Id == "shared" && item.Kind == "model");
    }

    [Fact]
    public async Task CreateDefaultIndexesEveryCanonicalAuthoredContentFamily()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-content-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Touch(new RekallAgeModelAssetStore().GetModelPath(root, "published-model"));
            Touch(new RekallAgeMeshAssetStore().GetMeshPath(root, "mesh"));
            Touch(new RekallAgeModelingGraphAssetStore().GetGraphPath(root, "modeling-graph"));
            Touch(new RekallAgeMaterialGraphAssetStore().GetGraphPath(root, "material-graph"));
            Touch(new RekallAgeMaterialInstanceAssetStore().GetInstancePath(root, "material-instance"));
            Touch(new RekallAgeCurveAssetStore().GetCurvePath(root, "curve"));
            Touch(new RekallAgeRigAssetStore().GetRigPath(root, "rig"));
            Touch(Path.Combine(root, "Shaders", "surface.vert"));
            Touch(Path.Combine(root, "Shaders", "lighting.glslinc"));
            Touch(Path.Combine(root, "Modules", "Gameplay", "Game.cs"));

            var result = await RekallAgeStudioContentIndex.CreateDefault().RefreshAsync(root, CancellationToken.None);

            Assert.Empty(result.Warnings);
            Assert.Contains(result.Items, item => item.Kind == "model-asset");
            Assert.Contains(result.Items, item => item.Kind == "mesh");
            Assert.Contains(result.Items, item => item.Kind == "modeling-graph");
            Assert.Contains(result.Items, item => item.Kind == "material-graph");
            Assert.Contains(result.Items, item => item.Kind == "material-instance");
            Assert.Contains(result.Items, item => item.Kind == "curve");
            Assert.Contains(result.Items, item => item.Kind == "rig");
            Assert.Contains(result.Items, item => item.Kind == "shader");
            Assert.Contains(result.Items, item => item.Kind == "shader-include");
            Assert.Contains(result.Items, item => item.Kind == "module-source");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshIsolatesSourceFailureAndRedactsItsDetails()
    {
        var index = new RekallAgeStudioContentIndex([
            new StubSource("module", [Item("source", "Game.cs", "module-source")]),
            new ThrowingSource("shader", new IOException("sentinel-private-path C:\\secret\\shader.glsl"))
        ]);

        var result = await index.RefreshAsync("C:\\project", CancellationToken.None);

        Assert.Contains(result.Items, item => item.Kind == "module-source");
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("REKALL_CONTENT_SOURCE_FAILED", warning.Code);
        Assert.Equal("shader", warning.Family);
        Assert.DoesNotContain("sentinel-private-path", warning.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", warning.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshPreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var index = new RekallAgeStudioContentIndex([new CancellingSource()]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await index.RefreshAsync("C:\\project", cancellation.Token));
    }

    [Theory]
    [InlineData("All", "", 3)]
    [InlineData("model", "", 1)]
    [InlineData("All", "hero", 1)]
    [InlineData("texture", "PNG", 1)]
    public void ProjectionFiltersByCategoryAndSearchAcrossUsefulFields(string category, string search, int expected)
    {
        var items = new[]
        {
            Item("hero", "Hero Mesh", "model") with { Kind = "mesh", Path = "Modeling/Hero.age.mesh.json" },
            Item("albedo", "Stone", "texture") with { Kind = "png", Path = "Assets/stone.PNG" },
            Item("theme", "Theme", "audio")
        };

        Assert.Equal(expected, RekallAgeStudioContentProjection.Filter(items, category, search).Count);
        Assert.Equal(["All", "audio", "model", "texture"], RekallAgeStudioContentProjection.Categories(items));
    }

    private static RekallAgeContentBrowserItem Item(string id, string name, string family) => new(
        id, name, family, family, "Authored", null, null, "1", "external", ["open"], "Healthy", null, new());

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
    }

    private sealed class StubSource(string family, IReadOnlyList<RekallAgeContentBrowserItem> items) : IRekallAgeStudioContentSource
    {
        public string Family => family;
        public ValueTask<IReadOnlyList<RekallAgeContentBrowserItem>> LoadAsync(string projectRoot, CancellationToken cancellationToken) => ValueTask.FromResult(items);
    }

    private sealed class ThrowingSource(string family, Exception exception) : IRekallAgeStudioContentSource
    {
        public string Family => family;
        public ValueTask<IReadOnlyList<RekallAgeContentBrowserItem>> LoadAsync(string projectRoot, CancellationToken cancellationToken) => ValueTask.FromException<IReadOnlyList<RekallAgeContentBrowserItem>>(exception);
    }

    private sealed class CancellingSource : IRekallAgeStudioContentSource
    {
        public string Family => "cancel";
        public ValueTask<IReadOnlyList<RekallAgeContentBrowserItem>> LoadAsync(string projectRoot, CancellationToken cancellationToken) => ValueTask.FromCanceled<IReadOnlyList<RekallAgeContentBrowserItem>>(cancellationToken);
    }
}
