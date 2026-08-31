using System.IO;
using Rekall.Age.Assets;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioContentOpenRouterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rekall-content-open-{Guid.NewGuid():N}");

    public StudioContentOpenRouterTests() => Directory.CreateDirectory(_root);

    public static TheoryData<string, string, string, string> InternalRoutes => new()
    {
        { "mesh-edit", "mesh", "modeling", "mesh-edit" },
        { "mesh-edit", "model-asset", "modeling", "mesh-edit" },
        { "modeling-graph", "modeling-graph", "modeling", "node-contracts" },
        { "material-graph", "material-graph", "modeling", "material-graph" },
        { "material-instance", "material-instance", "modeling", "material-graph" },
        { "module-source", "module-source", "code", "source-edit" },
        { "shader-edit", "shader", "external", "shader-source" },
        { "shader-edit", "shader-include", "external", "shader-source" }
    };

    [Theory]
    [MemberData(nameof(InternalRoutes))]
    public async Task InternalContentRoutesToItsFocusedStudioSurface(
        string route, string kind, string workspace, string surface)
    {
        var target = new RecordingTarget();
        var router = new RekallAgeStudioContentOpenRouter(target);
        var item = Item(route, kind, ExistingFile(kind));

        var result = await router.OpenAsync(item, CancellationToken.None);

        Assert.True(result.Opened);
        Assert.Equal("REKALL_CONTENT_OPENED", result.Code);
        Assert.Equal(workspace, result.WorkspaceId);
        Assert.Equal(surface, result.SurfaceId);
        Assert.Equal([(route, item.Id, item.Path!)], target.Calls);
    }

    [Theory]
    [InlineData("texture-preview", "texture")]
    [InlineData("audio-preview", "audio")]
    public async Task PreviewContentUsesInjectedAssociatedApplication(string route, string kind)
    {
        var target = new RecordingTarget();
        var router = new RekallAgeStudioContentOpenRouter(target);
        var item = Item(route, kind, ExistingFile(kind));

        var result = await router.OpenAsync(item, CancellationToken.None);

        Assert.True(result.Opened);
        Assert.Equal("external", result.WorkspaceId);
        Assert.Equal("associated-application", result.SurfaceId);
        Assert.Equal([(route, item.Id, item.Path!)], target.Calls);
    }

    [Fact]
    public async Task ImportedModelWithArbitraryDisplayNameUsesTruthfulExternalFallback()
    {
        var target = new RecordingTarget();
        var item = Item("external", "model", ExistingFile("spaceship")) with
        {
            DisplayName = "My Pretty Spaceship",
            Origin = "Imported",
            Capabilities = [RekallAgeContentCapability.OpenExternal]
        };

        var result = await new RekallAgeStudioContentOpenRouter(target).OpenAsync(item, CancellationToken.None);

        Assert.True(result.Opened);
        Assert.Equal("external", result.WorkspaceId);
        Assert.Equal([("external", item.Id, item.Path!)], target.Calls);
    }

    [Fact]
    public async Task ImportedModelProjectionUsesStablePathAndRoutesToEditableMeshPublishing()
    {
        var importedPath = ExistingFile("imported-model.glb");
        var sourcePath = ExistingFile("source-model.glb");
        await new RekallAgeAssetCatalogStore().SaveAsync(_root,
            RekallAgeAssetCatalogDocument.Empty.AddOrReplace(new(
                "stable-model-id", "ship", "Arbitrary Friendly Name", "glb", sourcePath, importedPath, "hash")),
            CancellationToken.None);

        var item = Assert.Single(await new ImportedContentSource().LoadAsync(_root, CancellationToken.None));

        Assert.Equal("stable-model-id", item.Id);
        Assert.Equal(importedPath, item.Path);
        Assert.Equal("mesh-edit", item.EditorRouteId);
        Assert.Contains(RekallAgeContentCapability.Open, item.Capabilities);
        Assert.DoesNotContain(RekallAgeContentCapability.OpenExternal, item.Capabilities);
    }

    [Theory]
    [InlineData("mesh-edit", "mesh", "open-external")]
    [InlineData("external", "texture", "open")]
    [InlineData("shader-edit", "shader", "open")]
    public async Task RouteRequiresItsSpecificCapability(string route, string kind, string wrongCapability)
    {
        var target = new RecordingTarget();
        var item = Item(route, kind, ExistingFile("capability")) with { Capabilities = [wrongCapability] };

        var result = await new RekallAgeStudioContentOpenRouter(target).OpenAsync(item, CancellationToken.None);

        Assert.False(result.Opened);
        Assert.Equal("REKALL_CONTENT_OPEN_UNAVAILABLE", result.Code);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public async Task UnknownRouteReturnsStableUnavailableResultWithoutCallingATarget()
    {
        var target = new RecordingTarget();
        var result = await new RekallAgeStudioContentOpenRouter(target)
            .OpenAsync(Item("future-editor", "future", ExistingFile("future")), CancellationToken.None);

        Assert.False(result.Opened);
        Assert.Equal("REKALL_CONTENT_OPEN_UNAVAILABLE", result.Code);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public async Task UnknownPathReturnsStableUnavailableResult()
    {
        var result = await new RekallAgeStudioContentOpenRouter(new RecordingTarget())
            .OpenAsync(Item("mesh-edit", "mesh", null), CancellationToken.None);

        Assert.False(result.Opened);
        Assert.Equal("REKALL_CONTENT_OPEN_UNAVAILABLE", result.Code);
        Assert.DoesNotContain(_root, result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingPathReturnsStableUnavailableResultWithoutLeakingPath()
    {
        var missing = Path.Combine(_root, "sentinel-private", "missing.glb");
        var result = await new RekallAgeStudioContentOpenRouter(new RecordingTarget())
            .OpenAsync(Item("mesh-edit", "mesh", missing), CancellationToken.None);

        Assert.False(result.Opened);
        Assert.Equal("REKALL_CONTENT_OPEN_UNAVAILABLE", result.Code);
        Assert.DoesNotContain("sentinel-private", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationPropagatesAndDoesNotBecomeAResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RekallAgeStudioContentOpenRouter(new RecordingTarget()).OpenAsync(
                Item("mesh-edit", "mesh", ExistingFile("mesh")), cancellation.Token).AsTask());
    }

    [Fact]
    public async Task TargetFailureReturnsRedactedStableFailure()
    {
        var sentinel = Path.Combine(_root, "sentinel-private-path.cs");
        var target = new RecordingTarget(new IOException($"Could not open {sentinel}"));

        var result = await new RekallAgeStudioContentOpenRouter(target)
            .OpenAsync(Item("module-source", "module-source", ExistingFile("module")), CancellationToken.None);

        Assert.False(result.Opened);
        Assert.Equal("REKALL_CONTENT_OPEN_FAILED", result.Code);
        Assert.Equal("The selected content could not be opened.", result.Summary);
        Assert.DoesNotContain("sentinel-private-path", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ViewModelOpenCommandRequiresProjectSelectionAndOpenCapability()
    {
        var router = new RecordingRouter(new(true, "REKALL_CONTENT_OPENED", "Opened Content.", "modeling", "mesh-edit"));
        await using var viewModel = new RekallAgeStudioViewModel(router);
        var item = Item("mesh-edit", "mesh", ExistingFile("command"));

        viewModel.SelectedContentItem = item;
        Assert.False(viewModel.OpenSelectedContentCommand.CanExecute(null));

        var project = Path.Combine(_root, "project");
        viewModel.ProjectPathInput = project;
        viewModel.ProjectNameInput = "Content Open Test";
        viewModel.SceneNameInput = "Main";
        await ((RekallAgeAsyncCommand)viewModel.CreateCommand).ExecuteAsync(null);

        viewModel.SelectedContentItem = item;
        Assert.True(viewModel.OpenSelectedContentCommand.CanExecute(null));
        viewModel.SelectedContentItem = item with { Capabilities = [] };
        Assert.False(viewModel.OpenSelectedContentCommand.CanExecute(null));
    }

    [Fact]
    public async Task ViewModelOpenCommandProjectsStructuredRouterResultIntoContentStatus()
    {
        var router = new RecordingRouter(new(true, "REKALL_CONTENT_OPENED", "Opened Content.", "modeling", "node-contracts"));
        await using var viewModel = new RekallAgeStudioViewModel(router);
        var project = Path.Combine(_root, "command-project");
        viewModel.ProjectPathInput = project;
        viewModel.ProjectNameInput = "Content Open Test";
        viewModel.SceneNameInput = "Main";
        await ((RekallAgeAsyncCommand)viewModel.CreateCommand).ExecuteAsync(null);
        viewModel.SelectedContentItem = Item("module-source", "module-source", ExistingFile("source"));

        await ((RekallAgeAsyncCommand)viewModel.OpenSelectedContentCommand).ExecuteAsync(null);

        Assert.Equal("REKALL_CONTENT_OPENED · Opened Content.", viewModel.ContentStatusText);
        Assert.Equal("Modeling", viewModel.SelectedStudioWorkspace);
        Assert.Equal("node-contracts", viewModel.SelectedModelingSurface);
        Assert.Single(router.Items);
    }

    [Fact]
    public async Task FailedAuthoredMeshOpenDoesNotMutateSelectedMeshAssetId()
    {
        await using var viewModel = new RekallAgeStudioViewModel();
        var project = Path.Combine(_root, "missing-mesh-project");
        viewModel.ProjectPathInput = project;
        viewModel.ProjectNameInput = "Missing Mesh Test";
        viewModel.SceneNameInput = "Main";
        await ((RekallAgeAsyncCommand)viewModel.CreateCommand).ExecuteAsync(null);
        viewModel.SelectedContentItem = Item("mesh-edit", "mesh", ExistingFile("not-an-authored-mesh")) with
        {
            DisplayName = "friendly-name-is-not-an-asset-id"
        };

        await ((RekallAgeAsyncCommand)viewModel.OpenSelectedContentCommand).ExecuteAsync(null);

        Assert.Null(viewModel.SelectedMeshAssetId);
        Assert.StartsWith("REKALL_CONTENT_OPEN_FAILED", viewModel.ContentStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionCommandRejectsExternalRouteWithoutOpenExternalCapability()
    {
        await using var viewModel = new RekallAgeStudioViewModel();
        var project = Path.Combine(_root, "external-capability-project");
        viewModel.ProjectPathInput = project;
        viewModel.ProjectNameInput = "External Capability Test";
        viewModel.SceneNameInput = "Main";
        await ((RekallAgeAsyncCommand)viewModel.CreateCommand).ExecuteAsync(null);
        viewModel.SelectedContentItem = Item("external", "texture", ExistingFile("do-not-launch")) with
        {
            Capabilities = [RekallAgeContentCapability.Open]
        };

        Assert.False(viewModel.OpenSelectedContentCommand.CanExecute(null));
    }

    [Fact]
    public void ShellAndModelingWorkspaceBindToRouterNavigationState()
    {
        var repository = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(repository, "src", "Rekall.Age.Studio", "MainWindow.xaml"));
        var modeling = File.ReadAllText(Path.Combine(repository, "src", "Rekall.Age.Studio", "ModelingWorkspace.xaml"));

        Assert.Contains("SelectedValue=\"{Binding SelectedStudioWorkspace, Mode=TwoWay}\"", shell, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Tag\"", shell, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding SelectedModelingSurface, Mode=TwoWay}\"", modeling, StringComparison.Ordinal);
        Assert.Contains("Tag=\"mesh-edit\"", modeling, StringComparison.Ordinal);
        Assert.Contains("Tag=\"node-contracts\"", modeling, StringComparison.Ordinal);
        Assert.Contains("Tag=\"material-graph\"", modeling, StringComparison.Ordinal);
    }

    private string ExistingFile(string name)
    {
        var path = Path.Combine(_root, name + ".asset");
        File.WriteAllText(path, "fixture");
        return path;
    }

    private static RekallAgeContentBrowserItem Item(string route, string kind, string? path) => new(
        "content-id", "Content", "family", kind, "Authored", path, null, "1", route,
        [route is "external" or "texture-preview" or "audio-preview" or "shader-edit"
            ? RekallAgeContentCapability.OpenExternal
            : RekallAgeContentCapability.Open], "Healthy", null, new());

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (current is not null && !Directory.Exists(Path.Combine(current, "src")))
            current = Directory.GetParent(current)?.FullName;
        return current ?? throw new InvalidOperationException("Repository root was not found.");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class RecordingTarget(Exception? failure = null) : IRekallAgeStudioContentOpenTarget
    {
        public List<(string Route, string Id, string Path)> Calls { get; } = [];

        public ValueTask SelectMeshAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Record("mesh-edit", item, token);
        public ValueTask SelectGraphAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Record("modeling-graph", item, token);
        public ValueTask SelectMaterialAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Record(item.EditorRouteId, item, token);
        public ValueTask SelectModuleSourceAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Record("module-source", item, token);
        public ValueTask OpenAssociatedAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Record(item.EditorRouteId, item, token);

        private ValueTask Record(string route, RekallAgeContentBrowserItem item, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (failure is not null) return ValueTask.FromException(failure);
            Calls.Add((route, item.Id, item.Path!));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRouter(RekallAgeStudioContentOpenResult result) : IRekallAgeStudioContentOpenRouter
    {
        public List<RekallAgeContentBrowserItem> Items { get; } = [];
        public bool CanOpen(RekallAgeContentBrowserItem item) =>
            item.Capabilities.Contains(RekallAgeContentCapability.Open, StringComparer.OrdinalIgnoreCase)
            || item.Capabilities.Contains(RekallAgeContentCapability.OpenExternal, StringComparer.OrdinalIgnoreCase);
        public ValueTask<RekallAgeStudioContentOpenResult> OpenAsync(RekallAgeContentBrowserItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.Add(item);
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task AcceptanceExternalLauncherValidatesAndRecordsWithoutStartingAnAssociatedApplication()
    {
        var path = ExistingFile("acceptance.wav");
        var recorded = new List<string>();
        IRekallAgeStudioExternalContentLauncher launcher =
            new RekallAgeStudioValidatingExternalContentLauncher(recorded.Add);

        await launcher.OpenAsync(path, CancellationToken.None);

        Assert.Equal([Path.GetFullPath(path)], recorded);
        var acceptance = Source("RekallAgeStudioContentBrowserAcceptance.cs");
        Assert.Contains("RekallAgeStudioValidatingExternalContentLauncher", acceptance, StringComparison.Ordinal);
        Assert.DoesNotContain("RekallAgeStudioShellExternalContentLauncher", acceptance, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", acceptance, StringComparison.Ordinal);
    }

    private static string Source(string fileName)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", fileName));
    }
}
