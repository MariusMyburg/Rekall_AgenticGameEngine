using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.LevelDesign;

public sealed class LevelDesignCommandTests
{
    [Fact]
    public async Task LevelDesignCommandsDuplicateParentPrefabInstantiateAndSnap()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneStore = new RekallAgeSceneStore();
        var entity = RekallAgeEntityDocument.Create("Crate", ["prop"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform2D",
                new JsonObject { ["x"] = 2.2, ["y"] = 5.7 }));
        await sceneStore.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity), CancellationToken.None);

        var registry = new RekallAgeCommandRegistry();
        registry.Register(new DuplicateEntityCommand());
        registry.Register(new ParentEntityCommand());
        registry.Register(new CreatePrefabFromEntityCommand());
        registry.Register(new InstantiatePrefabCommand());
        registry.Register(new SnapEntityToGridCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("level design"), CancellationToken.None);

        var duplicate = await registry.ExecuteAsync<DuplicateEntityRequest, DuplicateEntityResult>(
            "rekall.level.entity.duplicate",
            new DuplicateEntityRequest(root, "Main", entity.Id, "Crate Copy"),
            context);
        Assert.True(duplicate.Ok);

        var parent = await registry.ExecuteAsync<ParentEntityRequest, ParentEntityResult>(
            "rekall.level.entity.parent",
            new ParentEntityRequest(root, "Main", duplicate.Value.EntityId, entity.Id),
            context);
        Assert.True(parent.Ok);

        var prefab = await registry.ExecuteAsync<CreatePrefabFromEntityRequest, CreatePrefabFromEntityResult>(
            "rekall.level.prefab.create_from_entity",
            new CreatePrefabFromEntityRequest(root, "Main", entity.Id, "CratePrefab"),
            context);
        Assert.True(prefab.Ok);

        var instance = await registry.ExecuteAsync<InstantiatePrefabRequest, InstantiatePrefabResult>(
            "rekall.level.prefab.instantiate",
            new InstantiatePrefabRequest(root, "Main", prefab.Value.PrefabId, "Crate Instance"),
            context);
        Assert.True(instance.Ok);

        var snapped = await registry.ExecuteAsync<SnapEntityToGridRequest, SnapEntityToGridResult>(
            "rekall.level.entity.snap_to_grid",
            new SnapEntityToGridRequest(root, "Main", entity.Id, 1.0),
            context);
        Assert.True(snapped.Ok);

        var scene = await sceneStore.LoadAsync(root, "Main", CancellationToken.None);
        Assert.Equal(entity.Id, scene.GetRequiredEntity(duplicate.Value.EntityId).ParentId);
        Assert.Equal(prefab.Value.PrefabId, scene.GetRequiredEntity(instance.Value.EntityId).PrefabSourceId);
        var transform = scene.GetRequiredEntity(entity.Id).Components.Single(component => component.Type == "Rekall.Transform2D");
        Assert.Equal(2, transform.Properties["x"]!.GetValue<double>());
        Assert.Equal(6, transform.Properties["y"]!.GetValue<double>());
    }

    [Fact]
    public async Task AimCameraAtCommandPointsAtATargetEntityAndTheRenderedFrameSeesIt()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneStore = new RekallAgeSceneStore();
        var camera = RekallAgeEntityDocument.Create("MainCamera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
            {
                ["x"] = 0,
                ["y"] = 16,
                ["z"] = 18
            }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
            {
                ["active"] = true,
                ["nearClip"] = 0.05,
                ["farClip"] = 100
            }));
        var target = RekallAgeEntityDocument.Create("World Cube", ["prop"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject
            {
                ["primitive"] = "cube",
                ["color"] = "#4fc3e8"
            }));
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(camera).AddEntity(target),
            CancellationToken.None);

        var registry = new RekallAgeCommandRegistry();
        registry.Register(new AimCameraAtCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("aim camera"), CancellationToken.None);

        var aimed = await registry.ExecuteAsync<AimCameraAtRequest, AimCameraAtResult>(
            "rekall.level.camera.aim_at",
            new AimCameraAtRequest(root, "Main", camera.Id, TargetEntityId: target.Id),
            context);

        Assert.True(aimed.Ok, aimed.Summary);
        Assert.True(aimed.Value.Pitch > 0, $"Expected a positive (downward-tilting) pitch, found {aimed.Value.Pitch}.");
        Assert.Equal(180, aimed.Value.Yaw, precision: 6);

        var scene = await sceneStore.LoadAsync(root, "Main", CancellationToken.None);
        var transform = scene.GetRequiredEntity(camera.Id).Components.Single(component => component.Type == "Rekall.Transform3D");
        Assert.Equal(aimed.Value.Pitch, transform.Properties["pitch"]!.GetValue<double>(), precision: 6);
        Assert.Equal(aimed.Value.Yaw, transform.Properties["yaw"]!.GetValue<double>(), precision: 6);

        var captured = await new Rekall.Age.Rendering.Commands.CaptureRuntimeViewportCommand().ExecuteAsync(
            new Rekall.Age.Rendering.Commands.CaptureRuntimeViewportRequest(root, "Main", 0, Path.Combine(root, "Viewport"), 320, 180, false),
            context);
        Assert.True(captured.Ok, captured.Summary);
        Assert.DoesNotContain(
            "REKALL_VIEWPORT_CAMERA_FACES_AWAY_FROM_CONTENT",
            captured.Value.LayoutDiagnostics.WarningCodes);
        Assert.DoesNotContain(
            "REKALL_VIEWPORT_CAMERA_CONTENT_OUTSIDE_FIELD_OF_VIEW",
            captured.Value.LayoutDiagnostics.WarningCodes);
    }
}
