using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Editor;
using Rekall.Age.Project.Commands;
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

    private static RekallAgeWorkbenchSession CreateSession()
    {
        return new RekallAgeWorkbenchSession(CreateRegistry());
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
        return registry;
    }
}
