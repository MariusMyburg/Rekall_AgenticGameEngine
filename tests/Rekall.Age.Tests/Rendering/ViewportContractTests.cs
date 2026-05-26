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
    public void RuntimeFrameBuilderIncludesStereoCameraSettings()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Vr Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "vr",
                    ["stereoRenderMode"] = "side-by-side",
                    ["interpupillaryDistance"] = 0.07,
                    ["stereoConvergenceDistance"] = 25,
                    ["xrViewConfiguration"] = "primary-stereo-with-foveated-inset",
                    ["foveatedRendering"] = true
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 1000, 500, debugOverlay: false);

        Assert.NotNull(frame.ActiveCamera);
        Assert.Equal("stereo", frame.ActiveCamera.StereoMode);
        Assert.Equal("side-by-side", frame.ActiveCamera.StereoRenderMode);
        Assert.Equal(0.07, frame.ActiveCamera.InterpupillaryDistance);
        Assert.Equal(25, frame.ActiveCamera.StereoConvergenceDistance);
        Assert.NotNull(frame.Stereo);
        Assert.True(frame.Stereo.Enabled);
        Assert.Equal("side-by-side", frame.Stereo.RenderMode);
        Assert.False(frame.Stereo.PreferSinglePassMultiview);
        Assert.True(frame.Stereo.FoveatedRendering);
        Assert.Equal("primary-stereo-with-foveated-inset", frame.Stereo.XrViewConfiguration);
        Assert.Equal(2, frame.Stereo.Eyes.Count);
        Assert.Equal(-0.035, frame.Stereo.Eyes[0].OffsetX, 6);
        Assert.Equal(0.035, frame.Stereo.Eyes[1].OffsetX, 6);
        Assert.Equal(500, frame.Stereo.Eyes[0].ViewportWidth);
        Assert.Equal(500, frame.Stereo.Eyes[1].ViewportX);
    }

    [Fact]
    public void RuntimeFrameBuilderKeepsViewportCameraSeparateFromHeadsetCamera()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Spectator Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["renderOrder"] = -10,
                    ["cullingMask"] = "ui"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Head Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["renderOrder"] = 0,
                    ["stereoMode"] = "stereo",
                    ["stereoRenderMode"] = "single-pass-multiview",
                    ["cullingMask"] = "world"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })))
            .AddEntity(RekallAgeEntityDocument.Create("Ui Marker", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "ui" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "plane" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 640, 360, debugOverlay: false);
        var headsetFrame = frame.ForHeadsetOutput();

        Assert.Equal("Spectator Camera", frame.ActiveCamera?.EntityName);
        Assert.Equal("Head Camera", frame.HeadsetCamera?.EntityName);
        Assert.NotNull(frame.Stereo);
        Assert.Contains(frame.Renderables, renderable => renderable.EntityName == "Ui Marker");
        Assert.DoesNotContain(frame.Renderables, renderable => renderable.EntityName == "World Cube");
        Assert.Equal("Head Camera", headsetFrame.ActiveCamera?.EntityName);
        Assert.Contains(headsetFrame.Renderables, renderable => renderable.EntityName == "World Cube");
        Assert.DoesNotContain(headsetFrame.Renderables, renderable => renderable.EntityName == "Ui Marker");
    }

    [Fact]
    public void RuntimeFrameBuilderProjectsGenericMaterialSettingsForMeshRenderables()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Lamp", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "sphere" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Material", new JsonObject
                {
                    ["baseColor"] = "#202020",
                    ["baseColorTexture"] = "asset_base",
                    ["metallicFactor"] = 0.25,
                    ["roughnessFactor"] = 0.4,
                    ["metallicRoughnessTexture"] = "asset_mr",
                    ["normalTexture"] = "asset_normal",
                    ["normalScale"] = 0.75,
                    ["occlusionTexture"] = "asset_occ",
                    ["occlusionStrength"] = 0.5,
                    ["emissiveColor"] = "#ff8800",
                    ["emissiveTexture"] = "asset_glow",
                    ["emissiveStrength"] = 4
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        var renderable = Assert.Single(frame.Renderables, item => item.Kind == "mesh");
        Assert.Equal("#202020", renderable.MaterialColor);
        Assert.Equal("asset_base", renderable.TextureAssetId);
        Assert.Equal("asset_mr", renderable.MetallicRoughnessTextureAssetId);
        Assert.Equal("asset_normal", renderable.NormalTextureAssetId);
        Assert.Equal("asset_occ", renderable.OcclusionTextureAssetId);
        Assert.Equal(0.25, renderable.MetallicFactor);
        Assert.Equal(0.4, renderable.RoughnessFactor);
        Assert.Equal(0.75, renderable.NormalScale);
        Assert.Equal(0.5, renderable.OcclusionStrength);
        Assert.Equal("#ff8800", renderable.EmissiveColor);
        Assert.Equal("asset_glow", renderable.EmissiveTextureAssetId);
        Assert.Equal(4, renderable.EmissiveStrength);
    }

    [Fact]
    public void RuntimeProjectionExcludesInvisibleRenderEntities()
    {
        var hiddenCamera = RekallAgeEntityDocument.Create("Hidden Camera", ["camera"]) with { Visible = false };
        hiddenCamera = hiddenCamera.AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true }));
        var visibleCamera = RekallAgeEntityDocument.Create("Visible Camera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true }));
        var hiddenMesh = RekallAgeEntityDocument.Create("Hidden Mesh", ["prop"]) with { Visible = false };
        hiddenMesh = hiddenMesh.AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }));
        var visibleMesh = RekallAgeEntityDocument.Create("Visible Mesh", ["prop"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(hiddenCamera)
            .AddEntity(visibleCamera)
            .AddEntity(hiddenMesh)
            .AddEntity(visibleMesh);

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        Assert.Equal("Visible Camera", frame.ActiveCamera?.EntityName);
        Assert.DoesNotContain(frame.Cameras, camera => camera.EntityName == "Hidden Camera");
        Assert.Contains(frame.Renderables, renderable => renderable.EntityName == "Visible Mesh");
        Assert.DoesNotContain(frame.Renderables, renderable => renderable.EntityName == "Hidden Mesh");
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
    public void VulkanSceneBatchBuilderCreatesStereoEyeUniformsWithSharedGeometry()
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
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera-1",
            "Camera",
            "Camera3D",
            true,
            0,
            0,
            5,
            ProjectionMode: "perspective",
            StereoMode: "stereo",
            StereoRenderMode: "single-pass-multiview",
            InterpupillaryDistance: 0.064);
        var stereo = new RekallAgeRuntimeViewportStereoSettings(
            true,
            "stereo",
            "single-pass-multiview",
            2,
            0.064,
            10,
            "primary-stereo",
            false,
            true,
            [
                new RekallAgeRuntimeViewportEye("left", 0, -0.032, 0, 0, 0, 0, 320, 180),
                new RekallAgeRuntimeViewportEye("right", 1, 0.032, 0, 0, 0, 0, 320, 180)
            ]);
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            320,
            180,
            camera,
            [camera],
            [
                new RekallAgeRuntimeViewportRenderable("cube-1", "Cube", "mesh", null, 0, 0, 0, 200)
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            [],
            stereo);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]);

        Assert.Equal(3, batch.Vertices.Count);
        Assert.Single(batch.Draws);
        Assert.NotNull(batch.Stereo);
        Assert.True(batch.Stereo.PreferSinglePassMultiview);
        Assert.Equal(2, batch.Stereo.Views.Count);
        Assert.NotEqual(batch.Stereo.Views[0].ViewProjection, batch.Stereo.Views[1].ViewProjection);
        Assert.Equal(-0.032f, batch.Stereo.Views[0].EyePosition.X, 6);
        Assert.Equal(0.032f, batch.Stereo.Views[1].EyePosition.X, 6);
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
    public void RuntimeFrameBuilderProjectsOrbitPathRendererAroundParentBody()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "celestial"])
            .AddEntity(RekallAgeEntityDocument.Create("Sol", ["celestial"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = 5,
                    ["z"] = 7
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.CelestialBody", new JsonObject
                {
                    ["bodyId"] = "Sol",
                    ["type"] = "StellarBody",
                    ["massKg"] = 1.98847e30
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Luna", ["celestial"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.CelestialBody", new JsonObject
                {
                    ["bodyId"] = "Luna",
                    ["parentBodyId"] = "Sol"
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.KeplerOrbit", new JsonObject
                {
                    ["parentBodyId"] = "Sol",
                    ["semiMajorAxisKm"] = 10,
                    ["eccentricity"] = 0,
                    ["distanceScale"] = 1
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                {
                    ["radius"] = 1,
                    ["color"] = "#545454"
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.OrbitPathRenderer", new JsonObject
                {
                    ["segments"] = 32,
                    ["thickness"] = 0.2,
                    ["color"] = "#88aaff"
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        var planet = Assert.Single(frame.Renderables, item => item.EntityName == "Luna" && item.Variant == "rekall.planet.surface");
        var orbit = Assert.Single(frame.Renderables, item => item.EntityName == "Luna" && item.Variant == "rekall.orbit.path");
        Assert.NotEqual(planet.EntityId, orbit.EntityId);
        Assert.Equal("mesh", orbit.Kind);
        Assert.Equal(5, orbit.X);
        Assert.Equal(0, orbit.Y);
        Assert.Equal(7, orbit.Z);
        Assert.Equal("#88aaff", orbit.MaterialColor);
        Assert.Equal("#88aaff", orbit.EmissiveColor);
        Assert.True(orbit.EmissiveStrength > 0);
        Assert.NotNull(orbit.GeometryMesh);
        Assert.Equal(64, orbit.GeometryMesh.Vertices.Count);
        Assert.Equal(192, orbit.GeometryMesh.Indices.Count);
        Assert.True(orbit.GeometryMesh.Vertices.Max(vertex => vertex.X) > 9);
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
    public void RuntimeFrameBuilderProjectsAgentAuthoredLineSegments()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Debug Axes", ["debug", "lines"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = 3,
                    ["y"] = 4,
                    ["z"] = 5,
                    ["yaw"] = 15,
                    ["scaleX"] = 2,
                    ["scaleY"] = 2,
                    ["scaleZ"] = 2
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.LineSegments", new JsonObject
                {
                    ["color"] = "#33ddff88",
                    ["thickness"] = 0.04,
                    ["segments"] = new JsonArray
                    {
                        new JsonObject { ["fromX"] = 0, ["fromY"] = 0, ["fromZ"] = 0, ["toX"] = 1, ["toY"] = 0, ["toZ"] = 0 },
                        new JsonObject { ["fromX"] = 0, ["fromY"] = 0, ["fromZ"] = 0, ["toX"] = 0, ["toY"] = 1, ["toZ"] = 0 },
                        new JsonObject { ["fromX"] = 0, ["fromY"] = 0, ["fromZ"] = 0, ["toX"] = 0, ["toY"] = 0, ["toZ"] = 1 }
                    }
                })));
        var world = new RekallAgeRuntimeProjectionBuilder().Project(new RekallAgeRuntimeWorldBuilder().Build(scene));

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        var renderable = Assert.Single(frame.Renderables, item => item.EntityName == "Debug Axes");
        Assert.Equal("mesh", renderable.Kind);
        Assert.Equal("rekall.geometry.lines", renderable.Variant);
        Assert.Equal("#33ddff88", renderable.MaterialColor);
        Assert.Equal(150, renderable.SortKey);
        Assert.Equal(3, renderable.X);
        Assert.Equal(4, renderable.Y);
        Assert.Equal(5, renderable.Z);
        Assert.Equal(15, renderable.RotationY);
        Assert.Equal(2, renderable.ScaleX);
        Assert.Null(renderable.GeometryMesh);
        Assert.NotNull(renderable.LineSegments);
        Assert.Equal(0.04, renderable.LineSegments!.Thickness);
        Assert.Equal(3, renderable.LineSegments.Segments.Count);
        Assert.Contains(renderable.LineSegments.Segments, segment =>
            segment.FromX == 0 && segment.FromY == 0 && segment.FromZ == 0
            && segment.ToX == 0 && segment.ToY == 0 && segment.ToZ == 1);
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
        Assert.Equal("rekall.debug.collider.lines", floor.Variant);
        Assert.Equal("#33ddff66", floor.MaterialColor);
        Assert.Null(floor.GeometryMesh);
        Assert.NotNull(floor.LineSegments);
        Assert.Equal(12, floor.LineSegments!.Segments.Count);
        Assert.Equal(0.08, floor.LineSegments.Thickness);
        Assert.DoesNotContain(floor.LineSegments.Segments, segment =>
            Math.Abs(segment.FromX) < 3.9 && Math.Abs(segment.FromY) < 0.07 && Math.Abs(segment.FromZ) < 2.4
            && Math.Abs(segment.ToX) < 3.9 && Math.Abs(segment.ToY) < 0.07 && Math.Abs(segment.ToZ) < 2.4);
        Assert.Equal("rekall.debug.collider.lines", ball.Variant);
        Assert.Equal("#ffea0066", ball.MaterialColor);
        Assert.Null(ball.GeometryMesh);
        Assert.NotNull(ball.LineSegments);
        Assert.True(ball.LineSegments!.Segments.Count > floor.LineSegments.Segments.Count);
        Assert.True(floor.SortKey > 300);
        Assert.True(ball.SortKey > floor.SortKey);
    }

    [Fact]
    public void RuntimeFrameBuilderAdds2DColliderDebugLinesWhenDebugOverlayIsEnabled()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d", "physics2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Platform", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject
                {
                    ["x"] = 4,
                    ["y"] = -2,
                    ["rotation"] = 15
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.BoxCollider2D", new JsonObject
                {
                    ["width"] = 3,
                    ["height"] = 0.5
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Coin", ["pickup"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject
                {
                    ["x"] = -1,
                    ["y"] = 3
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.CircleCollider2D", new JsonObject
                {
                    ["radius"] = 0.75
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: true);

        var platform = Assert.Single(frame.Renderables, renderable => renderable.EntityName == "Platform Collider");
        var coin = Assert.Single(frame.Renderables, renderable => renderable.EntityName == "Coin Collider");
        Assert.Equal("rekall.debug.collider.lines", platform.Variant);
        Assert.Equal(4, platform.X);
        Assert.Equal(-2, platform.Y);
        Assert.Equal(15, platform.RotationZ);
        Assert.Null(platform.GeometryMesh);
        Assert.NotNull(platform.LineSegments);
        Assert.Equal(4, platform.LineSegments!.Segments.Count);
        Assert.DoesNotContain(platform.LineSegments.Segments, segment =>
            Math.Abs(segment.FromX) < 1.4 && Math.Abs(segment.FromY) < 0.2
            && Math.Abs(segment.ToX) < 1.4 && Math.Abs(segment.ToY) < 0.2);
        Assert.Equal("rekall.debug.collider.lines", coin.Variant);
        Assert.Equal(-1, coin.X);
        Assert.Equal(3, coin.Y);
        Assert.Null(coin.GeometryMesh);
        Assert.NotNull(coin.LineSegments);
        Assert.True(coin.LineSegments!.Segments.Count > platform.LineSegments.Segments.Count);
    }
}
