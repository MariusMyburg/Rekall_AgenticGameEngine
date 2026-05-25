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
                "runtime.celestial.kepler",
                "runtime.celestial.rotation",
                "runtime.animation",
                "runtime.audio",
                "runtime.physics.bepu",
                "runtime.rendering",
                "runtime.transform",
                "runtime.ui",
                "runtime.input.camera",
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
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, frames: 70, CancellationToken.None);

        var sphere = result.World.Entities.Single(entity => entity.Name == "Bouncy Sphere");
        var velocity = sphere.Components.Single(component => component.Type == "Rekall.PhysicsState3D")
            .Properties["linearVelocity"]!.AsObject();
        Assert.True(sphere.Transform.Position3D.Y > 0.62);
        Assert.True(velocity["y"]!.GetValue<float>() > 0);
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
}
