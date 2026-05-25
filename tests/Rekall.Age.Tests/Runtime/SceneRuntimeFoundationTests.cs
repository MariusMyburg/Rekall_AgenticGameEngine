using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class SceneRuntimeFoundationTests
{
    [Fact]
    public void BuilderPreservesSceneIdsHierarchyVisibilityAndComponents()
    {
        var parent = RekallAgeEntityDocument.Create("Root", ["level"]);
        var child = RekallAgeEntityDocument.Create("Player", ["player"]) with
        {
            ParentId = parent.Id,
            PrefabSourceId = "prefab_player",
            Locked = true
        };
        child = child.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Transform2D",
            new JsonObject { ["x"] = 12.5, ["y"] = -2, ["rotation"] = 45, ["scaleX"] = 2, ["scaleY"] = 3 }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(parent).AddEntity(child);

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var runtimeChild = world.Entities.Single(entity => entity.Id == child.Id);

        Assert.Equal(scene.Id, world.SceneId);
        Assert.Equal("Main", world.SceneName);
        Assert.Equal(parent.Id, runtimeChild.ParentId);
        Assert.Equal("prefab_player", runtimeChild.PrefabSourceId);
        Assert.True(runtimeChild.Locked);
        Assert.True(runtimeChild.Visible);
        Assert.Equal("Rekall.Transform2D", Assert.Single(runtimeChild.Components).Type);
        Assert.Equal(12.5, runtimeChild.Transform.Position2D.X);
        Assert.Equal(-2, runtimeChild.Transform.Position2D.Y);
        Assert.Equal(45, runtimeChild.Transform.Rotation2D);
        Assert.Equal(2, runtimeChild.Transform.Scale2D.X);
        Assert.Equal(3, runtimeChild.Transform.Scale2D.Y);
    }

    [Fact]
    public void BuilderExtracts3DTransformAndDoesNotMutateAuthoringScene()
    {
        var entity = RekallAgeEntityDocument.Create("Camera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject
                {
                    ["x"] = 1,
                    ["y"] = 2,
                    ["z"] = 3,
                    ["pitch"] = 10,
                    ["yaw"] = 20,
                    ["roll"] = 30,
                    ["scaleX"] = 4,
                    ["scaleY"] = 5,
                    ["scaleZ"] = 6
                }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity);
        var before = scene.Entities.Single().Components.Single().Properties.ToJsonString();

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var after = scene.Entities.Single().Components.Single().Properties.ToJsonString();

        Assert.Equal(before, after);
        Assert.Equal(new RekallAgeRuntimeVector3(1, 2, 3), world.Entities.Single().Transform.Position3D);
        Assert.Equal(new RekallAgeRuntimeVector3(10, 20, 30), world.Entities.Single().Transform.Rotation3D);
        Assert.Equal(new RekallAgeRuntimeVector3(4, 5, 6), world.Entities.Single().Transform.Scale3D);
    }

    [Fact]
    public void BuilderProjectsSubsystemViewsAndWarnings()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 0, ["y"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Sprite", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 1, ["y"] = 2 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_sprite" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody2D", new JsonObject { ["mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.BoxCollider2D", new JsonObject { ["width"] = 1, ["height"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.AudioEmitter", new JsonObject { ["clip"] = "asset_step" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject())))
            .AddEntity(RekallAgeEntityDocument.Create("Mesh", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer", new JsonObject { ["mesh"] = "asset_mesh" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PointLight", new JsonObject { ["intensity"] = 1 })))
            .AddEntity(RekallAgeEntityDocument.Create("HudCanvas", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.UiCanvas", new JsonObject { ["layer"] = 10 })))
            .AddEntity(RekallAgeEntityDocument.Create("HudButton", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.UiElement", new JsonObject { ["interactive"] = true })));

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        Assert.Single(world.Subsystems.Rendering.Cameras);
        Assert.Single(world.Subsystems.Rendering.Sprites);
        Assert.Single(world.Subsystems.Rendering.Meshes);
        Assert.Single(world.Subsystems.Rendering.Lights);
        Assert.Single(world.Subsystems.Rendering.UiLayers);
        Assert.Single(world.Subsystems.Physics.RigidBodies);
        Assert.Single(world.Subsystems.Physics.Colliders);
        Assert.Single(world.Subsystems.Audio.Emitters);
        Assert.Empty(world.Subsystems.Audio.Listeners);
        Assert.Single(world.Subsystems.Animation.Players);
        Assert.Single(world.Subsystems.Ui.Canvases);
        Assert.Single(world.Subsystems.Ui.Elements);
        Assert.Contains(world.Observations, item => item.Code == "REKALL_AUDIO_NO_LISTENER" && item.Severity == "warning");
        Assert.Contains(world.Observations, item => item.Code == "REKALL_ANIMATION_MISSING_CLIP" && item.Subsystem == "animation");
    }

    [Fact]
    public void GameplayInterpreterEmitsStructuredCompatibilityObservations()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })));

        var observation = new RekallAgeGameplayInterpreter()
            .Observe(scene, 3)
            .Single(item => item.System == "Camera2D");

        Assert.Equal(3, observation.Frame);
        Assert.Equal("rendering", observation.Subsystem);
        Assert.Equal("info", observation.Severity);
        Assert.Equal("REKALL_RUNTIME_SYSTEM_EVALUATED", observation.Code);
    }

    [Fact]
    public async Task ExecutionLoopAdvancesFramesDeterministically()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world"]);
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();

        var result = await loop.RunAsync(initial, frames: 3, CancellationToken.None);

        Assert.Equal(3, result.World.FrameIndex);
        Assert.Equal(TimeSpan.FromSeconds(3.0 / 60.0), result.World.ElapsedTime);
        Assert.Equal(3, result.FramesSimulated);
        Assert.Equal(
            [
                "runtime.animation",
                "runtime.audio",
                "runtime.physics",
                "runtime.rendering",
                "runtime.transform",
                "runtime.ui"
            ],
            result.SystemsRun);
    }

    [Fact]
    public async Task InspectSceneRuntimeCommandReturnsCompactSubsystemCounts()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneStore = new RekallAgeSceneStore();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Actor", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 1, ["y"] = 2 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_actor" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody2D", new JsonObject { ["mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.AudioEmitter", new JsonObject { ["clip"] = "asset_step" })));
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(root, "Main", 2),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("inspect runtime"), CancellationToken.None));

        Assert.True(result.Ok);
        Assert.Equal("Main", result.Value.SceneName);
        Assert.Equal(2, result.Value.FrameIndex);
        Assert.Equal(2, result.Value.EntityCount);
        Assert.Equal(2, result.Value.RenderableCount);
        Assert.Equal(1, result.Value.PhysicsBodyCount);
        Assert.Equal(1, result.Value.AudioEmitterCount);
        Assert.Contains(result.Value.Observations, item => item.Code == "REKALL_AUDIO_NO_LISTENER");
    }
}
