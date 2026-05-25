using System.Text.Json.Nodes;
using Rekall.Age.Editor;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class ViewportContractTests
{
    [Fact]
    public async Task ViewportModelExtractsCameraAndRenderableSprites()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var viewport = await new RekallAgeViewportModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal("Main", viewport.SceneName);
        Assert.Equal("Camera", viewport.ActiveCameraName);
        Assert.Single(viewport.RenderWorld.Sprites);
        Assert.Equal("Player", viewport.RenderWorld.Sprites[0].EntityName);
    }

    [Fact]
    public void RuntimeFrameBuilderUsesRuntimeRenderProjection()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 4, ["y"] = 8 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })))
            .AddEntity(RekallAgeEntityDocument.Create("Light", ["lighting"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PointLight", new JsonObject { ["intensity"] = 1 })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene) with
        {
            FrameIndex = 2,
            ElapsedTime = TimeSpan.FromSeconds(2.0 / 60.0)
        };

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: true);

        Assert.Equal("Main", frame.SceneName);
        Assert.Equal(2, frame.FrameIndex);
        Assert.Equal(320, frame.Width);
        Assert.Equal(180, frame.Height);
        Assert.Equal("Camera", frame.ActiveCamera?.EntityName);
        Assert.Contains(frame.Renderables, item => item.Kind == "sprite" && item.AssetId == "asset_player");
        Assert.Contains(frame.Renderables, item => item.Kind == "light" && item.EntityName == "Light");
        Assert.True(frame.DebugOverlay.Enabled);
    }

    [Fact]
    public void RuntimeFrameBuilderIncludesActiveCameraTransformForHardwareRendering()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = 2,
                    ["y"] = 3,
                    ["z"] = 8,
                    ["pitch"] = -12,
                    ["yaw"] = 35,
                    ["roll"] = 1
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        Assert.NotNull(frame.ActiveCamera);
        Assert.Equal(2, frame.ActiveCamera.X);
        Assert.Equal(3, frame.ActiveCamera.Y);
        Assert.Equal(8, frame.ActiveCamera.Z);
        Assert.Equal(-12, frame.ActiveCamera.RotationX);
        Assert.Equal(35, frame.ActiveCamera.RotationY);
        Assert.Equal(1, frame.ActiveCamera.RotationZ);
    }

    [Fact]
    public void RuntimeFrameBuilderIncludesCameraRenderSettings()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["projectionMode"] = "perspective",
                    ["fieldOfView"] = 72,
                    ["nearClip"] = 0.25,
                    ["farClip"] = 750,
                    ["clearColor"] = "#0a1020"
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        Assert.NotNull(frame.ActiveCamera);
        Assert.Equal("perspective", frame.ActiveCamera.ProjectionMode);
        Assert.Equal(72, frame.ActiveCamera.FieldOfViewDegrees);
        Assert.Equal(0.25, frame.ActiveCamera.NearClip);
        Assert.Equal(750, frame.ActiveCamera.FarClip);
        Assert.Equal("#0a1020", frame.ActiveCamera.ClearColor);
    }

    [Fact]
    public void VulkanSceneBatchBuilderUsesCameraFieldOfView()
    {
        var mesh = new RekallAgeVulkanSceneMesh(
            "cube-1",
            "Cube",
            "cube",
            [
                new RekallAgeVulkanSceneVertex(-0.5f, -0.5f, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0),
                new RekallAgeVulkanSceneVertex(0.5f, -0.5f, 0, 0, 0, 1, 1, 1, 1, 1, 1, 0),
                new RekallAgeVulkanSceneVertex(0, 0.5f, 0, 0, 0, 1, 1, 1, 1, 1, 0.5f, 1)
            ],
            [0, 1, 2]);
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "cube-1",
            "Cube",
            "mesh",
            "rekall.geometry.cube",
            0,
            0,
            0,
            200);
        var narrowCamera = new RekallAgeRuntimeViewportCamera(
            "camera-1",
            "Camera",
            "Camera3D",
            true,
            0,
            0,
            5,
            ProjectionMode: "perspective",
            FieldOfViewDegrees: 35);
        var wideCamera = narrowCamera with { FieldOfViewDegrees = 90 };
        var baseFrame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            320,
            180,
            narrowCamera,
            [narrowCamera],
            [renderable],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);

        var narrow = new RekallAgeVulkanSceneBatchBuilder().Build(baseFrame, [mesh]);
        var wide = new RekallAgeVulkanSceneBatchBuilder().Build(
            baseFrame with { ActiveCamera = wideCamera, Cameras = [wideCamera] },
            [mesh]);

        Assert.NotEqual(narrow.Frame.ViewProjection.M11, wide.Frame.ViewProjection.M11);
        Assert.True(narrow.Frame.ViewProjection.M11 > wide.Frame.ViewProjection.M11);
    }

    [Fact]
    public void RuntimeFrameBuilderIncludesAuthoredGeometryMeshPayload()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Triangle", ["geometry"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = 1,
                    ["y"] = 2,
                    ["z"] = 3
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryMesh", new JsonObject
                {
                    ["color"] = "#ff6633",
                    ["vertices"] = new JsonArray
                    {
                        new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0, ["nx"] = 0, ["ny"] = 0, ["nz"] = 1 },
                        new JsonObject { ["x"] = 1, ["y"] = 0, ["z"] = 0 },
                        new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0, ["r"] = 0, ["g"] = 1, ["b"] = 0, ["a"] = 0.75 }
                    },
                    ["indices"] = new JsonArray { 0, 1, 2 }
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer", new JsonObject { ["mesh"] = "rekall.geometry.mesh" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        var renderable = Assert.Single(frame.Renderables, item => item.EntityName == "Triangle");
        Assert.Equal("rekall.geometry.mesh", renderable.Variant);
        Assert.Equal("#ff6633", renderable.MaterialColor);
        Assert.NotNull(renderable.GeometryMesh);
        Assert.Equal(3, renderable.GeometryMesh.Vertices.Count);
        Assert.Equal([0, 1, 2], renderable.GeometryMesh.Indices);
        Assert.Equal(1, renderable.X);
        Assert.Equal(2, renderable.Y);
        Assert.Equal(3, renderable.Z);
        Assert.Equal(1, renderable.GeometryMesh.Vertices[0].NormalZ);
        Assert.Equal(1, renderable.GeometryMesh.Vertices[1].NormalZ);
        Assert.Equal(1, renderable.GeometryMesh.Vertices[2].NormalZ);
        Assert.Equal(1, renderable.GeometryMesh.Vertices[1].R);
        Assert.Equal(0.75, renderable.GeometryMesh.Vertices[2].A);
    }

    [Fact]
    public void RuntimeFrameBuilderProjectsPlanetRendererAsTexturedPlanetSurface()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "planet"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Gaia", ["planet"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = 12,
                    ["y"] = -3,
                    ["z"] = 4,
                    ["scaleX"] = 2,
                    ["scaleY"] = 2,
                    ["scaleZ"] = 2
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                {
                    ["Radius"] = 6,
                    ["SurfaceTexture"] = "earth_diffuse",
                    ["Color"] = "#4b86d8"
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.AtmosphereRenderer", new JsonObject
                {
                    ["Height"] = 0.18,
                    ["RayleighColor"] = "#7fb6ff"
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        var planet = Assert.Single(frame.Renderables, item => item.EntityName == "Gaia");
        Assert.Equal("mesh", planet.Kind);
        Assert.Equal("rekall.planet.surface", planet.Variant);
        Assert.Equal("earth_diffuse", planet.TextureAssetId);
        Assert.Equal("#4b86d8", planet.MaterialColor);
        Assert.Equal(12, planet.X);
        Assert.Equal(-3, planet.Y);
        Assert.Equal(4, planet.Z);
        Assert.Equal(24, planet.ScaleX);
        Assert.Equal(24, planet.ScaleY);
        Assert.Equal(24, planet.ScaleZ);
    }

    [Fact]
    public void RuntimeFrameBuilderPreservesAgentAuthoredRenderableContract()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Agent Portal", ["authored"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = 3,
                    ["y"] = 4,
                    ["z"] = 5,
                    ["scaleX"] = 2,
                    ["scaleY"] = 3,
                    ["scaleZ"] = 4
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var entity = Assert.Single(world.Entities);
        world = world with
        {
            Subsystems = world.Subsystems with
            {
                Rendering = world.Subsystems.Rendering with
                {
                    Meshes =
                    [
                        new RekallAgeRuntimeRenderMesh(
                            entity.Id,
                            entity.Name,
                            "agent.portal.asset",
                            Variant: "agent.portal.event-horizon",
                            TextureAssetId: "asset_portal_noise",
                            MaterialColor: "#ff33cc",
                            Kind: "agent.renderable.portal",
                            SortKey: 123,
                            ShaderPipeline: new RekallAgeRuntimeRenderShaderPipeline(
                                "agent/portal.vert",
                                "agent/portal.frag"))
                    ]
                }
            }
        };

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        var renderable = Assert.Single(frame.Renderables);
        Assert.Equal("agent.renderable.portal", renderable.Kind);
        Assert.Equal("agent.portal.event-horizon", renderable.Variant);
        Assert.Equal("agent.portal.asset", renderable.AssetId);
        Assert.Equal("asset_portal_noise", renderable.TextureAssetId);
        Assert.Equal("#ff33cc", renderable.MaterialColor);
        Assert.Equal(123, renderable.SortKey);
        Assert.NotNull(renderable.ShaderPipeline);
        Assert.Equal("agent/portal.vert", renderable.ShaderPipeline.VertexShader);
        Assert.Equal("agent/portal.frag", renderable.ShaderPipeline.FragmentShader);
        Assert.Equal(3, renderable.X);
        Assert.Equal(4, renderable.Y);
        Assert.Equal(5, renderable.Z);
        Assert.Equal(2, renderable.ScaleX);
        Assert.Equal(3, renderable.ScaleY);
        Assert.Equal(4, renderable.ScaleZ);
    }

    [Fact]
    public void RuntimeFrameBuilderAddsColliderDebugRenderablesWhenDebugOverlayIsEnabled()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Floor", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = 0,
                    ["y"] = -0.08,
                    ["z"] = 0
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.BoxCollider3D", new JsonObject
                {
                    ["width"] = 8,
                    ["height"] = 0.16,
                    ["depth"] = 5
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Ball", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = 1,
                    ["y"] = 2,
                    ["z"] = 3
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SphereCollider3D", new JsonObject
                {
                    ["radius"] = 0.9
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var withoutDebug = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);
        var withDebug = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: true);

        Assert.DoesNotContain(withoutDebug.Renderables, renderable => renderable.EntityId.EndsWith(":collider", StringComparison.Ordinal));
        var floor = Assert.Single(withDebug.Renderables, renderable => renderable.EntityName == "Floor Collider");
        var ball = Assert.Single(withDebug.Renderables, renderable => renderable.EntityName == "Ball Collider");
        Assert.Equal("rekall.geometry.cube", floor.Variant);
        Assert.Equal("#33ddff66", floor.MaterialColor);
        Assert.Equal(8, floor.ScaleX);
        Assert.Equal(0.16, floor.ScaleY);
        Assert.Equal(5, floor.ScaleZ);
        Assert.Equal("rekall.geometry.sphere", ball.Variant);
        Assert.Equal("#ffea0066", ball.MaterialColor);
        Assert.Equal(1.8, ball.ScaleX);
        Assert.Equal(1.8, ball.ScaleY);
        Assert.Equal(1.8, ball.ScaleZ);
        Assert.True(floor.SortKey > 300);
        Assert.True(ball.SortKey > floor.SortKey);
    }
}
