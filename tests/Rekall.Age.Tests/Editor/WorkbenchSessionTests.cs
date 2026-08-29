using System.Text.Json;
using Rekall.Age.AssetPipeline;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.Modeling;
using Rekall.Age.Project.Commands;
using Rekall.Age.Workflows;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Editor;

public sealed class WorkbenchSessionTests
{
    [Fact]
    public async Task SessionCreatesProjectAndSceneThroughCanonicalCommands()
    {
        var root = TestPaths.CreateTempDirectory();
        var session = CreateSession();

        var result = await session.CreateProjectAsync(
            root,
            "AI Game",
            "Main",
            ["world", "rendering2d"],
            ["world", "rendering2d"],
            "studio",
            CancellationToken.None);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(Path.GetFullPath(root), session.ProjectRoot);
        Assert.Equal("Main", session.SceneName);
        Assert.Equal("AI Game", session.Model!.Project.Name);
        Assert.Equal("Main", session.Model.Scene.Name);
        Assert.Contains(session.Model.Transactions.Transactions, transaction => transaction.Name == "Create AI Game");
    }

    [Fact]
    public async Task SuccessfulCommandRefreshesModelPersistsTransactionAndSupportsSelection()
    {
        var root = TestPaths.CreateTempDirectory();
        var session = CreateSession();
        Assert.True((await session.CreateProjectAsync(
            root,
            "Editable",
            "Main",
            ["world"],
            ["world"],
            "studio",
            CancellationToken.None)).Ok);

        var result = await session.ExecuteAsync(
            "rekall.entity.create",
            JsonSerializer.Serialize(new
            {
                projectRoot = root,
                sceneName = "Main",
                name = "Player",
                tags = new[] { "player" }
            }),
            "Create Player",
            "studio",
            CancellationToken.None);
        var entityId = Assert.IsType<CreateEntityResult>(result.Value).EntityId;
        await session.SelectEntityAsync(entityId, CancellationToken.None);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(entityId, session.SelectedEntityId);
        Assert.Equal("Player", session.Model!.Inspector.SelectedEntityName);
        Assert.Contains(session.Model.Scene.RootEntities, entity => entity.EntityId == entityId);
        Assert.Contains(session.Model.Transactions.Transactions, transaction => transaction.Name == "Create Player");
    }

    [Fact]
    public async Task FailedCommandPreservesLastValidModelAndSelection()
    {
        var root = TestPaths.CreateTempDirectory();
        var session = CreateSession();
        Assert.True((await session.CreateProjectAsync(
            root,
            "Stable",
            "Main",
            ["world"],
            ["world"],
            "studio",
            CancellationToken.None)).Ok);
        var before = session.Model;

        var result = await session.ExecuteAsync(
            "rekall.command.does_not_exist",
            "{}",
            "Invalid operation",
            "studio",
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Same(before, session.Model);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_COMMAND_NOT_FOUND");
    }

    [Fact]
    public async Task SessionSwitchesBetweenExistingScenesAndRejectsMissingScene()
    {
        var root = TestPaths.CreateTempDirectory();
        var session = CreateSession();
        Assert.True((await session.CreateProjectAsync(
            root,
            "Scenes",
            "Main",
            ["world"],
            ["world"],
            "studio",
            CancellationToken.None)).Ok);
        Assert.True((await session.ExecuteAsync(
            "rekall.scene.create",
            JsonSerializer.Serialize(new { projectRoot = root, name = "Arena", capabilities = new[] { "world" } }),
            "Create Arena",
            "studio",
            CancellationToken.None)).Ok);

        var switched = await session.OpenSceneAsync("Arena", CancellationToken.None);
        var beforeMissing = session.Model;
        var missing = await session.OpenSceneAsync("Missing", CancellationToken.None);

        Assert.True(switched.Ok, switched.Summary);
        Assert.Equal("Arena", session.SceneName);
        Assert.False(missing.Ok);
        Assert.Same(beforeMissing, session.Model);
        Assert.Contains(missing.Errors, error => error.Code == "REKALL_WORKBENCH_SCENE_OPEN_FAILED");
    }

    [Fact]
    public async Task ReloadObservesExternalCanonicalChangesAndPreservesSelectionWhenPresent()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.CreateProjectAsync(
            root,
            "Reloadable",
            "Main",
            ["world"],
            ["world"],
            "studio",
            CancellationToken.None)).Ok);
        var context = new RekallAgeCommandContext(
            "external-agent",
            Rekall.Age.Core.Transactions.RekallAgeTransaction.Begin("External edit"),
            CancellationToken.None);
        var created = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, "Main", "External Entity", ["agent-authored"]),
            context);
        Assert.True(created.Ok, created.Summary);

        var reloaded = await session.ReloadAsync(CancellationToken.None);

        Assert.True(reloaded.Ok, reloaded.Summary);
        Assert.Contains(session.Model!.Scene.RootEntities, entity => entity.EntityId == created.Value.EntityId);
    }

    [Fact]
    public async Task SessionAuthorsGenericComponentsAndPropertiesThroughCanonicalCommands()
    {
        var root = TestPaths.CreateTempDirectory();
        var session = CreateSession();
        Assert.True((await session.CreateProjectAsync(
            root, "Component Authoring", "Main", ["world"], ["world"], "studio", CancellationToken.None)).Ok);
        var created = await session.ExecuteAsync(
            "rekall.entity.create",
            JsonSerializer.Serialize(new { projectRoot = root, sceneName = "Main", name = "Authored", tags = Array.Empty<string>() }),
            "Create Authored",
            "studio",
            CancellationToken.None);
        var entityId = Assert.IsType<CreateEntityResult>(created.Value).EntityId;
        Assert.True((await session.SelectEntityAsync(entityId, CancellationToken.None)).Ok);

        Assert.True((await session.ExecuteAsync(
            "rekall.component.add",
            JsonSerializer.Serialize(new { projectRoot = root, sceneName = "Main", entityId, componentType = "Game.State", properties = new { } }),
            "Add State",
            "studio",
            CancellationToken.None)).Ok);
        Assert.True((await session.ExecuteAsync(
            "rekall.component.set_property",
            JsonSerializer.Serialize(new { projectRoot = root, sceneName = "Main", entityId, componentType = "Game.State", propertyName = "score", value = 42 }),
            "Set Score",
            "studio",
            CancellationToken.None)).Ok);

        var component = Assert.Single(session.Model!.Inspector.Components, item => item.Type == "Game.State");
        Assert.Contains(component.Properties, property => property.Name == "score" && property.Value == "42");

        Assert.True((await session.ExecuteAsync(
            "rekall.component.remove_property",
            JsonSerializer.Serialize(new { projectRoot = root, sceneName = "Main", entityId, componentType = "Game.State", propertyName = "score" }),
            "Remove Score",
            "studio",
            CancellationToken.None)).Ok);
        Assert.True((await session.ExecuteAsync(
            "rekall.component.remove",
            JsonSerializer.Serialize(new { projectRoot = root, sceneName = "Main", entityId, componentType = "Game.State" }),
            "Remove State",
            "studio",
            CancellationToken.None)).Ok);
        Assert.DoesNotContain(session.Model!.Inspector.Components, item => item.Type == "Game.State");
    }

    [Fact]
    public async Task SessionUndoAndRedoRestoreCanonicalTransactionPreimages()
    {
        var root = TestPaths.CreateTempDirectory();
        var session = CreateSession();
        Assert.True((await session.CreateProjectAsync(
            root, "Undoable", "Main", ["world"], ["world"], "studio", CancellationToken.None)).Ok);
        Assert.True((await session.ExecuteAsync(
            "rekall.entity.create",
            JsonSerializer.Serialize(new { projectRoot = root, sceneName = "Main", name = "Undo Me", tags = Array.Empty<string>() }),
            "Create Undo Me",
            "studio",
            CancellationToken.None)).Ok);
        Assert.True(session.CanUndo);
        Assert.Contains(session.Model!.Scene.RootEntities, entity => entity.Name == "Undo Me");

        var undone = await session.UndoAsync("studio", CancellationToken.None);

        Assert.True(undone.Ok, undone.Summary);
        Assert.DoesNotContain(session.Model!.Scene.RootEntities, entity => entity.Name == "Undo Me");
        Assert.True(session.CanRedo);

        var redone = await session.RedoAsync("studio", CancellationToken.None);

        Assert.True(redone.Ok, redone.Summary);
        Assert.Contains(session.Model!.Scene.RootEntities, entity => entity.Name == "Undo Me");
        Assert.True(session.CanUndo);
    }

    [Fact]
    public async Task SessionScopedUndoDoesNotTargetTransactionsFromBeforeTheSceneWasOpened()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAndSceneAsync(registry, root);
        var setup = new RekallAgeWorkbenchSession(registry);
        Assert.True((await setup.OpenAsync(root, "Main", default)).Ok);
        Assert.True((await setup.ExecuteAsync(
            "rekall.entity.create",
            JsonSerializer.Serialize(new { projectRoot = root, sceneName = "Main", name = "Preexisting", tags = Array.Empty<string>() }),
            "Create Preexisting",
            "agent",
            default)).Ok);

        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(root, "Main", default)).Ok);
        Assert.True(session.CanUndo);
        Assert.False(session.CanUndoSinceOpen);

        Assert.True((await session.ExecuteAsync(
            "rekall.entity.create",
            JsonSerializer.Serialize(new { projectRoot = root, sceneName = "Main", name = "Current Edit", tags = Array.Empty<string>() }),
            "Create Current Edit",
            "studio",
            default)).Ok);
        Assert.True(session.CanUndoSinceOpen);

        Assert.True((await session.UndoSinceOpenAsync("studio", default)).Ok);
        Assert.False(session.CanUndoSinceOpen);
        Assert.Contains(session.Model!.Scene.RootEntities, entity => entity.Name == "Preexisting");
        Assert.DoesNotContain(session.Model.Scene.RootEntities, entity => entity.Name == "Current Edit");

        var refused = await session.UndoSinceOpenAsync("studio", default);
        Assert.False(refused.Ok);
        Assert.Equal("REKALL_WORKBENCH_UNDO_SESSION_EMPTY", Assert.Single(refused.Errors).Code);
        Assert.Contains(session.Model.Scene.RootEntities, entity => entity.Name == "Preexisting");
    }

    [Fact]
    public async Task WorkbenchUndoRejectsLegacyDeletePreimageForFrozenImmutableOutputBeforeAnyMutation()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = RekallAgeDefaultCommandRegistry.Create();
        var projectContext = new RekallAgeCommandContext(
            "setup", RekallAgeTransaction.Begin("create workbench project"), default);
        Assert.True((await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new(root, "Protected Undo", ["world"]),
            projectContext)).Ok);
        Assert.True((await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create",
            new(root, "Main", ["world"]),
            projectContext)).Ok);

        var meshStore = new RekallAgeMeshAssetStore();
        var mesh = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "hero-mesh", "Hero Mesh", default);
        await meshStore.SaveAsync(root, mesh, default);
        var outputStore = new RekallAgePublishedModelOutputStore();
        var staged = await outputStore.WriteStagedAsync(
            root,
            "hero-model",
            new RekallAgeMeshCompiler().Compile(mesh),
            default);
        await outputStore.CommitStagedImmutableAsync(root, staged, default);
        await outputStore.DeleteStagedAsync(root, staged, default);
        var outputPath = outputStore.GetFinalPath(root, "hero-model", staged.ContentHash);
        var ordinaryPath = Path.Combine(root, "ordinary-before-protected.age.json");
        await File.WriteAllTextAsync(ordinaryPath, "{\"value\":1}");
        var legacy = RekallAgeTransaction.Begin("legacy publication owns frozen output");
        legacy.CaptureResourcePreimage(ordinaryPath);
        await File.WriteAllTextAsync(ordinaryPath, "{\"value\":2}");
        legacy.RecordChangedResource(ordinaryPath);
        legacy.RecordResourcePreimage(outputPath, existedBefore: false, content: []);
        legacy.RecordChangedResource(outputPath);
        var history = new RekallAgeTransactionLogStore();
        await history.AppendAsync(root, legacy, "legacy-agent", default);

        var publishing = new RekallAgeModelPublishingService();
        var published = await publishing.PublishAsync(
            root,
            new("hero-model", "Hero Model", new(RekallAgeModelSourceKind.Mesh, "hero-mesh"), RekallAgeDocumentRevision.Missing),
            RekallAgeTransaction.Begin("adopt immutable output"),
            default);
        var frozen = await publishing.FreezeAsync(
            root,
            "hero-model",
            published.ModelFileRevision,
            RekallAgeTransaction.Begin("freeze adopted output"),
            default);
        var scenePath = new RekallAgeSceneStore().GetScenePath(root, "Main");
        var sceneBytes = await File.ReadAllBytesAsync(scenePath);
        var outputBytes = await File.ReadAllBytesAsync(outputPath);
        var modelStore = new RekallAgeModelAssetStore();
        var modelPath = modelStore.GetModelPath(root, "hero-model");
        var modelBytes = await File.ReadAllBytesAsync(modelPath);
        var historyBytes = await File.ReadAllBytesAsync(history.GetPath(root));
        var ordinaryBytes = await File.ReadAllBytesAsync(ordinaryPath);
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(root, "Main", default)).Ok);
        Assert.True(session.CanUndo);

        var result = await session.UndoAsync("studio", default);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_RESOURCE_RESTORE_PROTECTED", Assert.Single(result.Errors).Code);
        Assert.True(session.CanUndo);
        Assert.Equal(outputBytes, await File.ReadAllBytesAsync(outputPath));
        Assert.Equal(ordinaryBytes, await File.ReadAllBytesAsync(ordinaryPath));
        Assert.Equal(modelBytes, await File.ReadAllBytesAsync(modelPath));
        Assert.Equal(sceneBytes, await File.ReadAllBytesAsync(scenePath));
        Assert.Equal(historyBytes, await File.ReadAllBytesAsync(history.GetPath(root)));
        Assert.Equal(RekallAgeModelBuildState.Frozen, frozen.Value.BuildState);
        Assert.Equal(
            RekallAgeModelBuildState.Frozen,
            (await publishing.InspectAsync(root, "hero-model", default)).BuildState);
    }

    [Fact]
    public async Task WorkbenchUndoRejectsHardLinkAliasOverwriteForFrozenImmutableOutput()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = RekallAgeDefaultCommandRegistry.Create();
        await CreateProjectAndSceneAsync(registry, root);
        var (publishing, outputPath, published) = await PublishModelAsync(root);
        _ = await publishing.FreezeAsync(
            root,
            "hero-model",
            published.ModelFileRevision,
            RekallAgeTransaction.Begin("freeze hard-link adopter"),
            default);
        var aliasPath = Path.Combine(root, "WorkbenchHardLinkAlias.age.compiled-mesh.json");
        CreateFileHardLinkOrSkip(aliasPath, outputPath);
        var legacy = RekallAgeTransaction.Begin("legacy hard-link Workbench restore");
        legacy.RecordResourcePreimage(
            aliasPath,
            existedBefore: true,
            System.Text.Encoding.UTF8.GetBytes("{\"legacy\":true}"));
        legacy.RecordChangedResource(aliasPath);
        var history = new RekallAgeTransactionLogStore();
        await history.AppendAsync(root, legacy, "legacy-agent", default);
        var scenePath = new RekallAgeSceneStore().GetScenePath(root, "Main");
        var sceneBytes = await File.ReadAllBytesAsync(scenePath);
        var outputBytes = await File.ReadAllBytesAsync(outputPath);
        var modelPath = new RekallAgeModelAssetStore().GetModelPath(root, "hero-model");
        var modelBytes = await File.ReadAllBytesAsync(modelPath);
        var historyBytes = await File.ReadAllBytesAsync(history.GetPath(root));
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(root, "Main", default)).Ok);

        var result = await session.UndoAsync("studio", default);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_RESOURCE_RESTORE_PROTECTED", Assert.Single(result.Errors).Code);
        Assert.True(File.Exists(aliasPath));
        Assert.Equal(outputBytes, await File.ReadAllBytesAsync(outputPath));
        Assert.Equal(outputBytes, await File.ReadAllBytesAsync(aliasPath));
        Assert.Equal(modelBytes, await File.ReadAllBytesAsync(modelPath));
        Assert.Equal(sceneBytes, await File.ReadAllBytesAsync(scenePath));
        Assert.Equal(historyBytes, await File.ReadAllBytesAsync(history.GetPath(root)));
        Assert.Equal(
            RekallAgeModelBuildState.Frozen,
            (await publishing.InspectAsync(root, "hero-model", default)).BuildState);
    }

    private static RekallAgeWorkbenchSession CreateSession()
    {
        return new RekallAgeWorkbenchSession(CreateRegistry());
    }

    private static async ValueTask CreateProjectAndSceneAsync(
        RekallAgeCommandRegistry registry,
        string root)
    {
        var context = new RekallAgeCommandContext(
            "setup", RekallAgeTransaction.Begin("create workbench fixture"), default);
        Assert.True((await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new(root, "Protected Undo", ["world"]),
            context)).Ok);
        Assert.True((await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create",
            new(root, "Main", ["world"]),
            context)).Ok);
    }

    private static async ValueTask<(RekallAgeModelPublishingService Service, string OutputPath, RekallAgePublishModelResult Published)>
        PublishModelAsync(string root)
    {
        var meshStore = new RekallAgeMeshAssetStore();
        var mesh = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "hero-mesh", "Hero Mesh", default);
        await meshStore.SaveAsync(root, mesh, default);
        var publishing = new RekallAgeModelPublishingService();
        var published = await publishing.PublishAsync(
            root,
            new("hero-model", "Hero Model", new(RekallAgeModelSourceKind.Mesh, "hero-mesh"), RekallAgeDocumentRevision.Missing),
            RekallAgeTransaction.Begin("publish hard-link adopter"),
            default);
        return (publishing, published.CompiledOutputPath, published);
    }

    private static void CreateFileHardLinkOrSkip(string linkPath, string targetPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(
                OperatingSystem.IsWindows() ? "cmd.exe" : "ln")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (OperatingSystem.IsWindows())
            {
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add("mklink");
                startInfo.ArgumentList.Add("/H");
                startInfo.ArgumentList.Add(linkPath);
                startInfo.ArgumentList.Add(targetPath);
            }
            else
            {
                startInfo.ArgumentList.Add(targetPath);
                startInfo.ArgumentList.Add(linkPath);
            }
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw Xunit.Sdk.SkipException.ForSkip(
                    $"Filesystem hard-link capability unavailable (exit {process.ExitCode}): {process.StandardError.ReadToEnd()}");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                $"Filesystem hard-link capability unavailable: {error.GetType().Name}: {error.Message}");
        }
    }

    private static RekallAgeCommandRegistry CreateRegistry()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        registry.Register(new AddComponentCommand());
        registry.Register(new SetComponentPropertyCommand());
        registry.Register(new RemoveComponentPropertyCommand());
        registry.Register(new RemoveComponentCommand());
        registry.Register(new RestoreTransactionPreimageCommand(
            new RekallAgeResourceRestorationPolicy()));
        return registry;
    }
}
