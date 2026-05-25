using System.Text.Json.Nodes;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.VerticalSlice;

public sealed class WorkbenchFoundationTests
{
    [Fact]
    public async Task AgentAndStudioCanShareAuthoringLoop()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "player.png");
        await File.WriteAllBytesAsync(source, [10, 20, 30, 40], CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        registry.Register(new AddComponentCommand());
        registry.Register(new ImportAssetWithReportCommand());
        registry.Register(new DuplicateEntityCommand());
        registry.Register(new CreatePrefabFromEntityCommand());
        registry.Register(new InstantiatePrefabCommand());
        registry.Register(new PlaySceneCommand());
        registry.Register(new InspectSceneRuntimeCommand());
        registry.Register(new CaptureScreenshotCommand());
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("workbench loop"), CancellationToken.None);

        Assert.True((await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new CreateProjectRequest(root, "Workbench Game", ["world", "rendering2d", "input"]),
            context)).Ok);
        Assert.True((await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create",
            new CreateSceneRequest(root, "Main", ["world", "rendering2d"]),
            context)).Ok);
        var camera = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, "Main", "MainCamera", ["camera"]),
            context);
        Assert.True(camera.Ok);
        Assert.True((await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(root, "Main", camera.Value.EntityId, "Rekall.Camera2D", new JsonObject { ["active"] = true }),
            context)).Ok);
        var player = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, "Main", "Player", ["player"]),
            context);
        Assert.True(player.Ok);
        Assert.True((await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(root, "Main", player.Value.EntityId, "Rekall.Transform2D", new JsonObject { ["x"] = 0, ["y"] = 0 }),
            context)).Ok);
        Assert.True((await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report",
            new ImportAssetWithReportRequest(root, source, "sprite", "Player"),
            context)).Ok);
        var duplicate = await registry.ExecuteAsync<DuplicateEntityRequest, DuplicateEntityResult>(
            "rekall.level.entity.duplicate",
            new DuplicateEntityRequest(root, "Main", player.Value.EntityId, "Player Copy"),
            context);
        Assert.True(duplicate.Ok);
        var prefab = await registry.ExecuteAsync<CreatePrefabFromEntityRequest, CreatePrefabFromEntityResult>(
            "rekall.level.prefab.create_from_entity",
            new CreatePrefabFromEntityRequest(root, "Main", player.Value.EntityId, "PlayerPrefab"),
            context);
        Assert.True(prefab.Ok);
        Assert.True((await registry.ExecuteAsync<InstantiatePrefabRequest, InstantiatePrefabResult>(
            "rekall.level.prefab.instantiate",
            new InstantiatePrefabRequest(root, "Main", prefab.Value.PrefabId, "Prefab Player"),
            context)).Ok);
        var runtime = await registry.ExecuteAsync<InspectSceneRuntimeRequest, InspectSceneRuntimeResult>(
            "rekall.runtime.inspect_scene",
            new InspectSceneRuntimeRequest(root, "Main", 1),
            context);
        Assert.True(runtime.Ok);
        Assert.True(runtime.Value.EntityCount >= 3);
        Assert.True(runtime.Value.RenderableCount >= 1);
        await new RekallAgeTransactionLogStore().AppendAsync(root, context.Transaction, context.Actor, CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);
        Assert.Equal("Workbench Game", model.Project.Name);
        Assert.True(model.Scene.RootEntities.Count >= 3);
        Assert.Single(model.Assets.Assets);
        Assert.DoesNotContain(model.Diagnostics.Issues, issue => issue.Severity == "blocking");
        Assert.Contains(model.Transactions.Transactions, transaction => transaction.Name == "workbench loop");

        var capture = await registry.ExecuteAsync<CaptureScreenshotRequest, CaptureScreenshotResult>(
            "rekall.capture.screenshot",
            new CaptureScreenshotRequest(root, "Main", Path.Combine(root, "Artifacts", "Screenshots")),
            context);
        Assert.True(capture.Ok);
        Assert.True(File.Exists(capture.Value.ScreenshotPath));
        Assert.NotEmpty(context.Transaction.ChangedResources);
    }
}
