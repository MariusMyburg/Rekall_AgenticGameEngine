using System.Numerics;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules;
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
            .AddEntity(RekallAgeEntityDocument.Create("Planet", ["planet"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject { ["Radius"] = 2 })))
            .AddEntity(RekallAgeEntityDocument.Create("HudCanvas", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.UiCanvas", new JsonObject { ["layer"] = 10 })))
            .AddEntity(RekallAgeEntityDocument.Create("HudButton", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.UiElement", new JsonObject { ["interactive"] = true })));

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        Assert.Single(world.Subsystems.Rendering.Cameras);
        Assert.Single(world.Subsystems.Rendering.Sprites);
        Assert.Equal(2, world.Subsystems.Rendering.Meshes.Count);
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
    public void BuilderProjectsVisibleStellarBodiesAsPointLights()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "celestial", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Sol", ["celestial"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CelestialBody",
                    new JsonObject
                    {
                        ["bodyId"] = "Sol",
                        ["type"] = "StellarBody",
                        ["massKg"] = 1.98847e30,
                        ["color"] = "#ffb347"
                    })));

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var light = Assert.Single(world.Subsystems.Rendering.Lights);
        Assert.Equal("Sol", light.EntityName);
        Assert.Equal("PointLight", light.Kind);
        Assert.Equal(4, light.Intensity);
        Assert.Equal("#ffb347", light.Color);
    }

    [Fact]
    public void BuilderDoesNotDuplicateAuthoredStellarPointLights()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "celestial", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Warm Star", ["celestial"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CelestialBody",
                    new JsonObject
                    {
                        ["bodyId"] = "WarmStar",
                        ["type"] = "StellarBody",
                        ["color"] = "#ffb347"
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PointLight",
                    new JsonObject { ["intensity"] = 2.25 })));

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var light = Assert.Single(world.Subsystems.Rendering.Lights);
        Assert.Equal("PointLight", light.Kind);
        Assert.Equal(2.25, light.Intensity);
        Assert.Equal("#ffb347", light.Color);
    }

    [Fact]
    public void GameplayInterpreterEmitsStructuredCompatibilityObservations()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Project Game Marker", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Game.Tests.CustomController", new JsonObject { ["speed"] = 8 })));

        var observations = new RekallAgeGameplayInterpreter().Observe(scene, 3);
        var observation = Assert.Single(observations, item => item.System == "Camera2D");

        Assert.Equal(3, observation.Frame);
        Assert.Equal("rendering", observation.Subsystem);
        Assert.Equal("info", observation.Severity);
        Assert.Equal("REKALL_RUNTIME_SYSTEM_EVALUATED", observation.Code);
        Assert.DoesNotContain(observations, item => item.System == "CustomController");
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
                "runtime.input.actions",
                "runtime.events.lifecycle",
                "runtime.xr.pose",
                "runtime.events.pointer",
                "runtime.events.timer",
                "runtime.celestial.kepler",
                "runtime.celestial.rotation",
                "runtime.animation.graph",
                "runtime.animation",
                "runtime.animation.skeletal",
                "runtime.audio",
                "runtime.physics.bepu",
                "runtime.rendering",
                "runtime.transform",
                "runtime.ui",
                "runtime.animation.morph",
                "runtime.events.collision",
                "runtime.events.trigger",
                "runtime.destruction",
                "runtime.ui.interaction",
                "runtime.input.camera",
                "runtime.input.camera_target_cycle",
                "runtime.camera.target3d"
            ],
            result.SystemsRun);
    }

    [Fact]
    public async Task ExecutionLoopAppliesTransformAnimationYawOverTime()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "animation", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("SlowSpinningCube", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["yaw"] = 10, ["scaleX"] = 2, ["scaleY"] = 2, ["scaleZ"] = 2 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.TransformAnimation",
                    new JsonObject { ["yawDegreesPerSecond"] = 90 })));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, frames: 60, CancellationToken.None);

        var cube = Assert.Single(result.World.Entities);
        Assert.Equal(60, result.World.FrameIndex);
        Assert.Equal(100, cube.Transform.Rotation3D.Y, precision: 3);
        Assert.Equal("TransformAnimation", Assert.Single(result.World.Subsystems.Animation.Players).Kind);
    }

    [Fact]
    public async Task ExecutionLoopUsesBepuPhysicsForDynamic3DBodies()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Falling Box", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = 4, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["width"] = 1, ["height"] = 1, ["depth"] = 1 })));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, frames: 60, CancellationToken.None);

        var body = Assert.Single(result.World.Entities);
        Assert.Contains("runtime.physics.bepu", result.SystemsRun);
        Assert.True(body.Transform.Position3D.Y < 4);
        Assert.True(body.Transform.Position3D.Y > -2);
        Assert.Contains(body.Components, component =>
            component.Type == "Rekall.PhysicsState3D"
            && component.Properties["backend"]!.GetValue<string>() == "bepu");
    }

    [Fact]
    public async Task BepuPhysicsBodiesCollideWithStaticBoxColliders()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ground", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = -0.5, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["width"] = 20, ["height"] = 1, ["depth"] = 20 })))
            .AddEntity(RekallAgeEntityDocument.Create("Falling Box", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = 3, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["width"] = 1, ["height"] = 1, ["depth"] = 1 })));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, frames: 180, CancellationToken.None);

        var body = result.World.Entities.Single(entity => entity.Name == "Falling Box");
        Assert.InRange(body.Transform.Position3D.Y, 0.45, 0.65);
    }

    [Fact]
    public async Task BepuPhysicsIgnoresCollisionsBetweenNonAcceptingLayers()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ground", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = -0.5, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["width"] = 20, ["height"] = 1, ["depth"] = 20 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CollisionFilter",
                    new JsonObject { ["layer"] = "terrain" })))
            .AddEntity(RekallAgeEntityDocument.Create("Falling Box", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = 3, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["width"] = 1, ["height"] = 1, ["depth"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CollisionFilter",
                    new JsonObject
                    {
                        ["layer"] = "ghost",
                        ["collidesWith"] = new JsonArray("nothing")
                    })));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, frames: 180, CancellationToken.None);

        var body = result.World.Entities.Single(entity => entity.Name == "Falling Box");
        Assert.True(body.Transform.Position3D.Y < -5, $"Expected the box to fall through the non-accepting ground, actual Y={body.Transform.Position3D.Y}.");
    }

    [Fact]
    public async Task BepuPhysicsDynamicBoxesCollideAndSettleIntoAStack()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ground", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["Y"] = -0.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 20, ["Height"] = 1, ["Depth"] = 20 })));

        for (var index = 0; index < 3; index++)
        {
            scene = scene.AddEntity(RekallAgeEntityDocument.Create($"Box {index}", ["block"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject
                    {
                        ["X"] = index * 0.03,
                        ["Y"] = 1.5 + (index * 2),
                        ["Yaw"] = index * 7,
                        ["Roll"] = index * 3
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 1, ["Height"] = 1, ["Depth"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsMaterial3D",
                    new JsonObject
                    {
                        ["Friction"] = 0.62,
                        ["Restitution"] = 0.18,
                        ["SpringFrequency"] = 24,
                        ["DampingRatio"] = 0.75
                    })));
        }

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), 600, CancellationToken.None);

        var boxes = result.World.Entities
            .Where(entity => entity.Tags.Contains("block", StringComparer.OrdinalIgnoreCase))
            .OrderBy(entity => entity.Transform.Position3D.Y)
            .ToArray();
        Assert.Equal(3, boxes.Length);
        Assert.InRange(boxes[0].Transform.Position3D.Y, 0.4, 0.75);
        Assert.True(boxes[1].Transform.Position3D.Y > boxes[0].Transform.Position3D.Y + 0.7);
        Assert.True(boxes[2].Transform.Position3D.Y > boxes[1].Transform.Position3D.Y + 0.7);
        Assert.All(boxes, box =>
        {
            var state = box.FindComponent("Rekall.PhysicsState3D")!;
            var linear = state.Properties["linearVelocity"]!.AsObject();
            var angular = state.Properties["angularVelocity"]!.AsObject();
            Assert.InRange(Math.Abs(linear["x"]!.GetValue<float>()), 0, 0.05);
            Assert.InRange(Math.Abs(linear["y"]!.GetValue<float>()), 0, 0.05);
            Assert.InRange(Math.Abs(linear["z"]!.GetValue<float>()), 0, 0.05);
            Assert.InRange(Math.Abs(angular["x"]!.GetValue<float>()), 0, 0.5);
            Assert.InRange(Math.Abs(angular["y"]!.GetValue<float>()), 0, 0.5);
            Assert.InRange(Math.Abs(angular["z"]!.GetValue<float>()), 0, 0.5);
            Assert.False(state.Properties["awake"]!.GetValue<bool>());
        });
    }

    [Fact]
    public async Task BepuPhysicsKeepsSleepingBodiesAndContactStateWhenEntitiesSpawnIncrementally()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ground", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["Y"] = -0.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 20, ["Height"] = 1, ["Depth"] = 20 })))
            .AddEntity(RekallAgeEntityDocument.Create("Settled Box", ["block"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["Y"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 1, ["Height"] = 1, ["Depth"] = 1 })));
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();
        var settled = await loop.RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(scene),
            600,
            CancellationToken.None);
        var settledBox = settled.World.Entities.Single(entity => entity.Name == "Settled Box");
        Assert.False(settledBox.FindComponent("Rekall.PhysicsState3D")!
            .Properties["awake"]!.GetValue<bool>());

        var spawned = RekallAgeRuntimeModuleSdk.CreateEntity("spawned", "Spawned Box")
            .WithPosition3D(new RekallAgeRuntimeVector3(5, 5, 0))
            .WithComponentNumber("Rekall.Rigidbody3D", "mass", 1)
            .WithComponentNumber("Rekall.BoxCollider3D", "width", 1)
            .WithComponentNumber("Rekall.BoxCollider3D", "height", 1)
            .WithComponentNumber("Rekall.BoxCollider3D", "depth", 1);
        var afterSpawn = await loop.RunAsync(
            settled.World.AddEntity(spawned),
            1,
            CancellationToken.None);

        settledBox = afterSpawn.World.Entities.Single(entity => entity.Name == "Settled Box");
        Assert.False(settledBox.FindComponent("Rekall.PhysicsState3D")!
            .Properties["awake"]!.GetValue<bool>());

        var afterRemoval = await loop.RunAsync(
            afterSpawn.World.RemoveEntity("spawned"),
            1,
            CancellationToken.None);
        Assert.DoesNotContain(afterRemoval.World.Entities, entity => entity.Id == "spawned");
        settledBox = afterRemoval.World.Entities.Single(entity => entity.Name == "Settled Box");
        Assert.False(settledBox.FindComponent("Rekall.PhysicsState3D")!
            .Properties["awake"]!.GetValue<bool>());
        loop.Dispose();
    }

    [Fact]
    public async Task BepuPhysicsDoesNotInventHorizontalBounceForRotatedStaticGeometry()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Vertical Slab", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["Roll"] = 90 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 20, ["Height"] = 0.2, ["Depth"] = 20 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsMaterial3D",
                    new JsonObject { ["Restitution"] = 0.9 })))
            .AddEntity(RekallAgeEntityDocument.Create("Falling Sphere", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["X"] = 5, ["Y"] = 3 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["Radius"] = 0.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsMaterial3D",
                    new JsonObject
                    {
                        ["Restitution"] = 0.9,
                        ["MinimumBounceSpeed"] = 0.4,
                        ["MaximumRecoveryVelocity"] = 8
                    })));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), frames: 70, CancellationToken.None);

        var sphere = result.World.Entities.Single(entity => entity.Name == "Falling Sphere");
        var velocityY = sphere.FindComponent("Rekall.PhysicsState3D")!
            .Properties["linearVelocity"]!["y"]!.GetValue<float>();
        Assert.True(sphere.Transform.Position3D.Y < -2);
        Assert.True(velocityY < -5);
    }

    [Fact]
    public async Task BepuPhysicsAppliesAuthorableRestitutionForBouncyBodies()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ground", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["y"] = -0.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 20, ["Height"] = 1, ["Depth"] = 20 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsMaterial3D",
                    new JsonObject { ["Friction"] = 0.35, ["Restitution"] = 0.9 })))
            .AddEntity(RekallAgeEntityDocument.Create("Bouncy Sphere", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["y"] = 3 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["Radius"] = 0.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsMaterial3D",
                    new JsonObject
                    {
                        ["Friction"] = 0.25,
                        ["Restitution"] = 0.9,
                        ["MinimumBounceSpeed"] = 0.4,
                        ["MaximumRecoveryVelocity"] = 8
                    })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();
        var contacted = false;
        var maximumHeightAfterContact = double.MinValue;
        for (var frame = 0; frame < 120; frame++)
        {
            world = (await loop.RunAsync(world, frames: 1, CancellationToken.None)).World;
            var sphere = world.Entities.Single(entity => entity.Name == "Bouncy Sphere");
            contacted |= sphere.Transform.Position3D.Y <= 0.55;
            if (contacted)
            {
                maximumHeightAfterContact = Math.Max(maximumHeightAfterContact, sphere.Transform.Position3D.Y);
            }
        }

        Assert.True(
            contacted && maximumHeightAfterContact > 1,
            $"Expected a visible BEPU contact-spring rebound, but the maximum post-contact Y was {maximumHeightAfterContact}.");
    }

    [Fact]
    public async Task BepuPhysicsReadsPascalCaseAuthoringSchemaProperties()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(RekallAgeEntityDocument.Create("Floating Box", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = 4, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 2 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 2, ["Height"] = 2, ["Depth"] = 2 })));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, frames: 60, CancellationToken.None);

        var body = result.World.Entities.Single(entity => entity.Name == "Floating Box");
        Assert.Equal(4, body.Transform.Position3D.Y, precision: 3);
    }

    [Fact]
    public async Task BepuPhysicsSupportsSphereAndCapsuleColliders()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ground", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["y"] = -0.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 20, ["Height"] = 1, ["Depth"] = 20 })))
            .AddEntity(RekallAgeEntityDocument.Create("Sphere", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = -2, ["y"] = 3 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["Radius"] = 0.5 })))
            .AddEntity(RekallAgeEntityDocument.Create("Capsule", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 2, ["y"] = 3 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CapsuleCollider3D",
                    new JsonObject { ["Radius"] = 0.5, ["Length"] = 1 })));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, frames: 180, CancellationToken.None);

        var sphere = result.World.Entities.Single(entity => entity.Name == "Sphere");
        var capsule = result.World.Entities.Single(entity => entity.Name == "Capsule");
        Assert.InRange(sphere.Transform.Position3D.Y, 0.45, 0.65);
        Assert.InRange(capsule.Transform.Position3D.Y, 0.95, 1.15);
    }

    [Fact]
    public async Task BepuPhysicsSupportsStaticMeshCollidersFromGeometryMesh()
    {
        var groundVertices = new JsonArray
        {
            new JsonObject { ["x"] = -10, ["y"] = 0, ["z"] = -10 },
            new JsonObject { ["x"] = -10, ["y"] = 0, ["z"] = 10 },
            new JsonObject { ["x"] = 10, ["y"] = 0, ["z"] = 10 },
            new JsonObject { ["x"] = 10, ["y"] = 0, ["z"] = -10 }
        };
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Mesh Ground", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GeometryMesh",
                    new JsonObject
                    {
                        ["vertices"] = groundVertices,
                        ["indices"] = new JsonArray { 0, 1, 2, 0, 2, 3 }
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshCollider",
                    new JsonObject())))
            .AddEntity(RekallAgeEntityDocument.Create("Falling Sphere", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = 3, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["Radius"] = 0.5 })));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, frames: 180, CancellationToken.None);

        var sphere = result.World.Entities.Single(entity => entity.Name == "Falling Sphere");
        Assert.InRange(sphere.Transform.Position3D.Y, 0.45, 0.65);
    }

    [Fact]
    public async Task BepuPhysicsSupportsDynamicConvexMeshCollidersFromGeometryMesh()
    {
        var cubeVertices = new JsonArray
        {
            new JsonObject { ["x"] = -0.5, ["y"] = -0.5, ["z"] = -0.5 },
            new JsonObject { ["x"] = -0.5, ["y"] = -0.5, ["z"] = 0.5 },
            new JsonObject { ["x"] = -0.5, ["y"] = 0.5, ["z"] = -0.5 },
            new JsonObject { ["x"] = -0.5, ["y"] = 0.5, ["z"] = 0.5 },
            new JsonObject { ["x"] = 0.5, ["y"] = -0.5, ["z"] = -0.5 },
            new JsonObject { ["x"] = 0.5, ["y"] = -0.5, ["z"] = 0.5 },
            new JsonObject { ["x"] = 0.5, ["y"] = 0.5, ["z"] = -0.5 },
            new JsonObject { ["x"] = 0.5, ["y"] = 0.5, ["z"] = 0.5 }
        };
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ground", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["y"] = -0.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 20, ["Height"] = 1, ["Depth"] = 20 })))
            .AddEntity(RekallAgeEntityDocument.Create("Convex Mesh", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["y"] = 3 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GeometryMesh",
                    new JsonObject { ["vertices"] = cubeVertices, ["indices"] = new JsonArray() }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshCollider",
                    new JsonObject { ["Convex"] = true })));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, frames: 180, CancellationToken.None);

        var mesh = result.World.Entities.Single(entity => entity.Name == "Convex Mesh");
        Assert.InRange(mesh.Transform.Position3D.Y, 0.45, 0.65);
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

    [Fact]
    public async Task ExecutionLoopUsesBepuPhysicsForDynamic2DBodiesOnTheXyPlane()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Falling Box", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform2D",
                    new JsonObject { ["X"] = 2, ["Y"] = 4 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody2D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider2D",
                    new JsonObject { ["Width"] = 1, ["Height"] = 1 })));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), 60, CancellationToken.None);

        var body = Assert.Single(result.World.Entities);
        Assert.Equal(2, body.Transform.Position2D.X, precision: 3);
        Assert.True(body.Transform.Position2D.Y < 4);
        Assert.Equal(0, body.Transform.Position3D.Z, precision: 3);
        Assert.Contains(body.Components, component =>
            component.Type == "Rekall.PhysicsState2D"
            && component.Properties["backend"]!.GetValue<string>() == "bepu");
    }

    [Fact]
    public async Task BepuPhysicsAppliesTransformRotationTo2DPrimitiveColliders()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(RekallAgeEntityDocument.Create("Rotated Wall", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform2D",
                    new JsonObject { ["Rotation"] = 90 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider2D",
                    new JsonObject { ["Width"] = 4, ["Height"] = 0.2 })))
            .AddEntity(RekallAgeEntityDocument.Create("Moving Circle", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform2D",
                    new JsonObject { ["X"] = -2, ["Y"] = 1.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody2D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CircleCollider2D",
                    new JsonObject { ["Radius"] = 0.25 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsState2D",
                    new JsonObject
                    {
                        ["linearVelocity"] = new JsonObject { ["x"] = 5, ["y"] = 0, ["z"] = 0 }
                    })));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), 60, CancellationToken.None);

        var circle = result.World.Entities.Single(entity => entity.Name == "Moving Circle");
        Assert.InRange(circle.Transform.Position2D.X, -0.6, 0.5);
    }

    [Fact]
    public async Task BepuPhysicsAppliesTransformRotationTo3DPrimitiveColliders()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(RekallAgeEntityDocument.Create("Rotated Wall", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["Roll"] = 90 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 4, ["Height"] = 0.2, ["Depth"] = 4 })))
            .AddEntity(RekallAgeEntityDocument.Create("Moving Sphere", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["X"] = -2, ["Y"] = 1.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["Radius"] = 0.25 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsState3D",
                    new JsonObject
                    {
                        ["linearVelocity"] = new JsonObject { ["x"] = 5, ["y"] = 0, ["z"] = 0 }
                    })));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), 60, CancellationToken.None);

        var sphere = result.World.Entities.Single(entity => entity.Name == "Moving Sphere");
        Assert.InRange(sphere.Transform.Position3D.X, -0.6, 0.5);
    }

    [Fact]
    public async Task BepuPhysicsPublishesEulerRotationEquivalentToItsQuaternionAcrossMultiAxisMotion()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(RekallAgeEntityDocument.Create("Spinning Box", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["Pitch"] = 37, ["Yaw"] = -61, ["Roll"] = 24 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject
                    {
                        ["Mass"] = 1,
                        ["AngularVelocityX"] = 100,
                        ["AngularVelocityY"] = 70,
                        ["AngularVelocityZ"] = -80
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 1, ["Height"] = 1, ["Depth"] = 1 })));

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();
        for (var frame = 0; frame < 300; frame++)
        {
            world = (await loop.RunAsync(world, 1, CancellationToken.None)).World;
            var box = world.Entities.Single(entity => entity.Name == "Spinning Box");
            var orientation = box.FindComponent("Rekall.PhysicsState3D")!.Properties["orientation"]!.AsObject();
            var physics = Quaternion.Normalize(new Quaternion(
                orientation["x"]!.GetValue<float>(),
                orientation["y"]!.GetValue<float>(),
                orientation["z"]!.GetValue<float>(),
                orientation["w"]!.GetValue<float>()));
            var rotation = box.Transform.Rotation3D;
            var rendered = Quaternion.CreateFromRotationMatrix(
                Matrix4x4.CreateRotationX((float)(rotation.X * Math.PI / 180))
                * Matrix4x4.CreateRotationY((float)(rotation.Y * Math.PI / 180))
                * Matrix4x4.CreateRotationZ((float)(rotation.Z * Math.PI / 180)));
            Assert.True(
                Math.Abs(Quaternion.Dot(physics, rendered)) > 0.99999f,
                $"Physics and rendered rotation diverged at frame {frame + 1}: physics={physics}, rendered={rendered}, euler={rotation}.");
        }
    }

    [Fact]
    public async Task BepuPhysicsPreservesAuthoredOrientationDuringUnobstructedFall()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Falling Box", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject
                    {
                        ["X"] = 0,
                        ["Y"] = 100,
                        ["Z"] = 0,
                        ["Pitch"] = 23,
                        ["Yaw"] = 41,
                        ["Roll"] = -17
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 1, ["Height"] = 1, ["Depth"] = 1 })));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), 60, CancellationToken.None);

        var box = result.World.Entities.Single(entity => entity.Name == "Falling Box");
        Assert.InRange(box.Transform.Rotation3D.X, 22.99, 23.01);
        Assert.InRange(box.Transform.Rotation3D.Y, 40.99, 41.01);
        Assert.InRange(box.Transform.Rotation3D.Z, -17.01, -16.99);
        var state = box.Components.Single(component => component.Type == "Rekall.PhysicsState3D");
        Assert.InRange(Math.Abs(state.Properties["angularVelocity"]!["x"]!.GetValue<float>()), 0, 0.001);
        Assert.InRange(Math.Abs(state.Properties["angularVelocity"]!["y"]!.GetValue<float>()), 0, 0.001);
        Assert.InRange(Math.Abs(state.Properties["angularVelocity"]!["z"]!.GetValue<float>()), 0, 0.001);
    }

    [Fact]
    public async Task BepuPhysicsPersistsAuthoredAngularMotionAcrossFrames()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(RekallAgeEntityDocument.Create("Tumbling Box", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["Mass"] = 1, ["AngularVelocityY"] = 90 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 1, ["Height"] = 1, ["Depth"] = 1 })));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), 60, CancellationToken.None);

        var box = result.World.Entities.Single(entity => entity.Name == "Tumbling Box");
        Assert.InRange(Math.Abs(box.Transform.Rotation3D.Y), 80, 100);
        var state = box.Components.Single(component => component.Type == "Rekall.PhysicsState3D");
        Assert.InRange(state.Properties["angularVelocity"]!["y"]!.GetValue<float>(), 80, 100);
        Assert.NotNull(state.Properties["orientation"]);
    }

    [Fact]
    public async Task InspectSceneRuntimeReportsBoundedPhysicsStateAndPeakSpeeds()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(RekallAgeEntityDocument.Create("Measured Box", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject
                    {
                        ["Mass"] = 1,
                        ["LinearVelocityX"] = 3,
                        ["AngularVelocityY"] = 90
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BoxCollider3D",
                    new JsonObject { ["Width"] = 1, ["Height"] = 1, ["Depth"] = 1 })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(root, "Main", 30),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("physics telemetry"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        var body = result.Value.EntityStates.Single(state => state.EntityName == "Measured Box").Physics;
        Assert.NotNull(body);
        Assert.Equal("bepu", body.Backend);
        Assert.True(body.Awake);
        Assert.InRange(body.LinearSpeed, 2.99, 3.01);
        Assert.InRange(body.AngularSpeedDegrees, 89.9, 90.1);
        Assert.InRange(body.PeakLinearSpeed, 2.99, 3.01);
        Assert.InRange(body.PeakAngularSpeedDegrees, 89.9, 90.1);
        Assert.Equal(1, body.PeakLinearSpeedFrame);
        Assert.Equal(1, body.PeakAngularSpeedFrame);
        Assert.InRange(Math.Abs(
            (body.Orientation.X * body.Orientation.X)
            + (body.Orientation.Y * body.Orientation.Y)
            + (body.Orientation.Z * body.Orientation.Z)
            + (body.Orientation.W * body.Orientation.W)
            - 1), 0, 0.0001);
    }

    [Fact]
    public async Task InspectSceneRuntimeCommandExposesBoundedPostSimulationEntityState()
    {
        var root = TestPaths.CreateTempDirectory();
        var animated = RekallAgeEntityDocument.Create("Animated", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["X"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["Version"] = 1,
                    ["DurationSeconds"] = 1,
                    ["Tracks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["component"] = "Rekall.Transform3D",
                            ["property"] = "X",
                            ["interpolation"] = "linear",
                            ["keys"] = new JsonArray
                            {
                                new JsonObject { ["time"] = 0, ["value"] = 0 },
                                new JsonObject { ["time"] = 1, ["value"] = 6 }
                            }
                        }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["Playing"] = true, ["LoopMode"] = "clamp" }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["animation"]).AddEntity(animated),
            CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(root, "Main", 30),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("inspect state"), CancellationToken.None));
        var json = System.Text.Json.JsonSerializer.SerializeToNode(result.Value)!.AsObject();
        var state = Assert.Single(json["EntityStates"]!.AsArray())!.AsObject();

        Assert.Equal("Animated", state["EntityName"]!.GetValue<string>());
        Assert.Equal(0, state["InitialTransform"]!["Position3D"]!["X"]!.GetValue<double>(), precision: 3);
        Assert.Equal(3, state["Transform"]!["Position3D"]!["X"]!.GetValue<double>(), precision: 3);
        Assert.Equal(3, state["PositionDelta3D"]!["X"]!.GetValue<double>(), precision: 3);
        Assert.Equal(0, state["PositionDelta3D"]!["Y"]!.GetValue<double>(), precision: 3);
        Assert.False(json["EntityStatesTruncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task InspectSceneRuntimeCommandEvaluatesGenericBehaviorAssertionsAgainstFinalState()
    {
        var root = TestPaths.CreateTempDirectory();
        var animated = RekallAgeEntityDocument.Create("Animated", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["X"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["Version"] = 1,
                    ["DurationSeconds"] = 1,
                    ["Tracks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["component"] = "Rekall.Transform3D",
                            ["property"] = "X",
                            ["interpolation"] = "linear",
                            ["keys"] = new JsonArray
                            {
                                new JsonObject { ["time"] = 0, ["value"] = 0 },
                                new JsonObject { ["time"] = 1, ["value"] = 6 }
                            }
                        }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["Playing"] = true, ["LoopMode"] = "clamp" }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["animation"]).AddEntity(animated),
            CancellationToken.None);
        var assertions = new[]
        {
            new InspectSceneRuntimeAssertion("Animated", "component", "exists")
            {
                ComponentType = "Rekall.AnimationPlayer"
            },
            new InspectSceneRuntimeAssertion("Animated", "component.property", "equals")
            {
                ComponentType = "Rekall.AnimationPlayer",
                PropertyName = "Playing",
                Expected = JsonValue.Create(true)
            },
            new InspectSceneRuntimeAssertion("Animated", "delta.position3d.x", "greater-than-or-equal")
            {
                Expected = JsonValue.Create(2.9)
            },
            new InspectSceneRuntimeAssertion("Animated", "delta.transform.position3d.x", "greater-than-or-equal")
            {
                Expected = JsonValue.Create(2.9)
            },
            new InspectSceneRuntimeAssertion("Animated", "delta.component.property", "greater-than-or-equal")
            {
                ComponentType = "Rekall.Transform3D",
                PropertyName = "X",
                Expected = JsonValue.Create(2.9)
            },
            new InspectSceneRuntimeAssertion("Animated", "changed.component.property", "equals")
            {
                ComponentType = "Rekall.Transform3D",
                PropertyName = "X",
                Expected = JsonValue.Create(true)
            },
            new InspectSceneRuntimeAssertion("Missing", "entity", "not-exists")
        };

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(root, "Main", 30, Assertions: assertions),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("assert runtime state"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.AssertionsPassed);
        Assert.Equal(7, result.Value.AssertionResults.Count);
        Assert.All(result.Value.AssertionResults, assertion => Assert.True(assertion.Passed, assertion.Summary));
    }

    [Fact]
    public async Task InspectSceneRuntimeCommandFailsWhenRequiredGameplayComponentIsNotAttached()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))),
            CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                Assertions:
                [
                    new InspectSceneRuntimeAssertion("Player", "component", "exists")
                    {
                        ComponentType = "Game.Modules.Rules.PlayerState"
                    }
                ]),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("reject missing gameplay state"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.False(result.Value.AssertionsPassed);
        var assertion = Assert.Single(result.Value.AssertionResults);
        Assert.False(assertion.Passed);
        Assert.Contains("Game.Modules.Rules.PlayerState", assertion.Summary, StringComparison.Ordinal);
        Assert.Contains("Player", result.Summary, StringComparison.Ordinal);
        Assert.Contains("component", result.Summary, StringComparison.Ordinal);
        Assert.Contains("Game.Modules.Rules.PlayerState", result.Summary, StringComparison.Ordinal);
        Assert.Contains("actual (missing)", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RUNTIME_ASSERTION_FAILED");
    }

    [Fact]
    public async Task InspectSceneRuntimeCommandFrontLoadsBoundedNumericAssertionEvidence()
    {
        var root = TestPaths.CreateTempDirectory();
        var player = RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["X"] = 0 }));
        var diagnostic = RekallAgeEntityDocument.Create(new string('E', 512), ["diagnostic"]);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(player)
                .AddEntity(diagnostic),
            CancellationToken.None);
        var assertions = Enumerable.Range(0, 16)
            .Select(_ => new InspectSceneRuntimeAssertion("Player", "delta.position3d.x", "greater-than")
            {
                Expected = JsonValue.Create(0)
            })
            .ToArray();

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(root, "Main", 1, Assertions: assertions),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("front-load assertion evidence"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Contains("Player", result.Summary, StringComparison.Ordinal);
        Assert.Contains("delta.position3d.x", result.Summary, StringComparison.Ordinal);
        Assert.Contains("expected greater-than 0", result.Summary, StringComparison.Ordinal);
        Assert.Contains("actual 0", result.Summary, StringComparison.Ordinal);
        Assert.Contains("8 more", result.Summary, StringComparison.Ordinal);
        Assert.InRange(result.Summary.Length, 1, 4_000);
        Assert.Equal(16, result.Value.AssertionResults.Count);
    }

    [Fact]
    public async Task InspectSceneRuntimeCommandRejectsMissingAssertionIdentityWithoutThrowing()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])),
            CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                Assertions:
                [
                    new InspectSceneRuntimeAssertion(null!, "delta.position3d.x", "greater-than")
                    {
                        Expected = JsonValue.Create(0)
                    }
                ]),
            new RekallAgeCommandContext(
                "test",
                RekallAgeTransaction.Begin("reject incomplete assertion"),
                CancellationToken.None));

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_RUNTIME_ASSERTION_FIELD_REQUIRED", error.Code);
        Assert.Equal("assertions[0].entityName", error.Target);
        Assert.Contains("entityName", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedComponentAssertionReportsMissingPropertyInsteadOfFalse()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Game.PlayerState",
                        new JsonObject { ["Enabled"] = true }))),
            CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                Assertions:
                [
                    new InspectSceneRuntimeAssertion(
                        "Player",
                        "changed.component.property",
                        "equals")
                    {
                        ComponentType = "Game.PlayerState",
                        PropertyName = "Active",
                        Expected = JsonValue.Create(true)
                    }
                ]),
            new RekallAgeCommandContext(
                "test",
                RekallAgeTransaction.Begin("report missing changed property"),
                CancellationToken.None));

        Assert.False(result.Ok);
        var assertion = Assert.Single(result.Value.AssertionResults);
        Assert.Null(assertion.Actual);
        Assert.Contains("property 'Active' was not found", assertion.Summary, StringComparison.Ordinal);
        Assert.Contains("actual (missing)", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectSceneRuntimeCommandExposesBoundedUiContractsForAgentVerification()
    {
        var root = TestPaths.CreateTempDirectory();
        var canvas = RekallAgeEntityDocument.Create("HUD", ["ui"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.UiCanvas",
                new JsonObject { ["ReferenceWidth"] = 200, ["ReferenceHeight"] = 100 }));
        var button = RekallAgeEntityDocument.Create("Ready", ["ui"]) with { ParentId = canvas.Id };
        button = button.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Button",
            new JsonObject
            {
                ["Text"] = "SYSTEMS READY",
                ["Interactive"] = true,
                ["Width"] = 120,
                ["Height"] = 30
            }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["ui"]).AddEntity(canvas).AddEntity(button),
            CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(root, "Main", 1),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("inspect ui"), CancellationToken.None));

        Assert.Equal(1, result.Value.UiCanvasCount);
        var inspectedCanvas = Assert.Single(result.Value.UiCanvases);
        Assert.Equal(200, inspectedCanvas.ReferenceWidth);
        Assert.Equal(100, inspectedCanvas.ReferenceHeight);
        Assert.Equal(1, result.Value.InteractiveUiElementCount);
        var inspected = Assert.Single(result.Value.UiElements);
        Assert.Equal("SYSTEMS READY", inspected.Text);
        Assert.Equal("Button", inspected.Kind);
        Assert.NotNull(inspected.Layout);
        Assert.Equal(200, inspected.Layout.ReferenceWidth);
        Assert.False(result.Value.UiElementsTruncated);
    }

    [Fact]
    public async Task InspectSceneRuntimeCommandReportsRenderablesCulledByActiveCameraMask()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "world"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Visible Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })))
            .AddEntity(RekallAgeEntityDocument.Create("Hidden Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(root, "Main", 0),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("inspect runtime culling"), CancellationToken.None));

        Assert.True(result.Ok);
        Assert.Equal(3, result.Value.RenderableCount);
        Assert.Equal(1, result.Value.VisibleRenderableCount);
        Assert.Equal(1, result.Value.CulledRenderableCount);
        Assert.Contains(result.Value.CulledRenderables, renderable =>
            renderable.EntityName == "Hidden Cube"
            && renderable.Layer == "helpers"
            && renderable.Reason == "camera-culling-mask");
    }
}
