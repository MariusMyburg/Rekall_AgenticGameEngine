using System.Numerics;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanSceneBatchBuilderTests
{
    [Fact]
    public void BuildProjectsAuthoredEnvironmentIntoFrameUniform()
    {
        var frame = CreateFrame() with
        {
            Environment = new RekallAgeRuntimeViewportEnvironment(
                "environment",
                "Environment",
                null,
                AmbientEnergy: 0.55,
                Exposure: -0.35,
                ToneMapper: "agx",
                WhitePoint: 11.2,
                ColorGradeAssetId: null,
                BackgroundPolicy: "color")
            {
                AmbientSkyColor = "#80a0c0",
                AmbientGroundColor = "#604020"
            }
        };

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, []);

        Assert.Equal(new Vector4(0.55f, -0.35f, 11.2f, 1), batch.Frame.EnvironmentParameters);
        Assert.Equal(new Vector4(128 / 255f, 160 / 255f, 192 / 255f, 1), batch.Frame.EnvironmentAmbientSkyColor);
        Assert.Equal(new Vector4(96 / 255f, 64 / 255f, 32 / 255f, 1), batch.Frame.EnvironmentAmbientGroundColor);
    }

    [Fact]
    public void DynamicRebuildReusesStableTopologyAndUpdatesDrawTransform()
    {
        var initial = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "mesh", "Mesh", "mesh", "rekall.primitive.cube", 0, 0, 5, 1,
            Variant: "rekall.geometry.cube"));
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(initial);
        var builder = new RekallAgeVulkanSceneBatchBuilder();
        var stable = builder.Build(initial, meshes);
        var moved = initial with
        {
            Renderables = [initial.Renderables[0] with { X = 7 }]
        };

        var dynamicBatch = builder.BuildDynamic(moved, meshes, stable);

        Assert.Same(stable.Vertices, dynamicBatch.Vertices);
        Assert.Same(stable.Indices, dynamicBatch.Indices);
        Assert.NotEqual(stable.Draws[0].Model, dynamicBatch.Draws[0].Model);
        Assert.Equal(7, dynamicBatch.Draws[0].Model.M41);
    }

    [Fact]
    public void BuildPreservesAuthoredShaderPipelineOnDraw()
    {
        var pipeline = new RekallAgeRuntimeViewportShaderPipeline("agent/tint", "agent/tint");
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "cube-1",
            "Shader Cube",
            "mesh",
            "rekall.primitive.cube",
            0,
            0,
            0,
            1,
            Variant: "rekall.geometry.cube",
            ShaderPipeline: pipeline));
        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.Equal(pipeline, draw.ShaderPipeline);
    }

    [Fact]
    public void BuildPreservesLocalVerticesAndCreatesPerMeshDraws()
    {
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "cube-1",
                "Cube",
                "mesh",
                "rekall.primitive.cube",
                3,
                4,
                5,
                1,
                Variant: "rekall.geometry.cube",
                RotationY: 45,
                ScaleX: 2,
                ScaleY: 3,
                ScaleZ: 4));
        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]);

        Assert.Equal(mesh.Vertices.Count, batch.Vertices.Count);
        Assert.Equal(mesh.Indices.Count, batch.Indices.Count);
        Assert.Equal(mesh.Vertices[0].X, batch.Vertices[0].X);
        Assert.Equal(mesh.Vertices[0].Y, batch.Vertices[0].Y);
        Assert.Equal(mesh.Vertices[0].Z, batch.Vertices[0].Z);
        var draw = Assert.Single(batch.Draws);
        Assert.Equal((uint)mesh.Indices.Count, draw.IndexCount);
        Assert.Equal(0u, draw.FirstIndex);
        Assert.Equal(0, draw.VertexOffset);
        Assert.InRange(draw.Model.M41, 2.99f, 3.01f);
        Assert.InRange(draw.Model.M42, 3.99f, 4.01f);
        Assert.InRange(draw.Model.M43, 4.99f, 5.01f);
    }

    [Fact]
    public void BuildOrientsCameraFacingRenderablesFromActiveCameraPlane()
    {
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "label-1",
            "Label",
            "mesh",
            null,
            10,
            20,
            30,
            1,
            Variant: "rekall.text.label",
            ScaleX: 2,
            ScaleY: 3,
            ScaleZ: 4,
            FacingMode: "camera-plane");
        var camera = new RekallAgeRuntimeViewportCamera("camera-1", "Camera", "Rekall.Camera3D", true, 0, 0, 10, 0, 90, 0);
        var frame = CreateFrame(renderable) with { ActiveCamera = camera, Cameras = [camera] };
        var mesh = new RekallAgeVulkanSceneMesh(
            "label-1",
            "Label",
            "line-segments",
            [
                new RekallAgeVulkanSceneVertex(0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 0, 0),
                new RekallAgeVulkanSceneVertex(1, 0, 0, 0, 1, 0, 1, 1, 1, 1, 0, 0),
                new RekallAgeVulkanSceneVertex(0, 0, -1, 0, 1, 0, 1, 1, 1, 1, 0, 0)
            ],
            [0, 1, 2]);

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.Equal(0, draw.Model.M11, precision: 4);
        Assert.Equal(0, draw.Model.M12, precision: 4);
        Assert.Equal(2, draw.Model.M13, precision: 4);
        Assert.Equal(0, draw.Model.M31, precision: 4);
        Assert.Equal(-4, draw.Model.M32, precision: 4);
        Assert.Equal(0, draw.Model.M33, precision: 4);
        Assert.Equal(10, draw.Model.M41, precision: 4);
        Assert.Equal(20, draw.Model.M42, precision: 4);
        Assert.Equal(30, draw.Model.M43, precision: 4);
    }

    [Fact]
    public void BuildKeepsCameraPlaneRenderableTextDirectionReadableForTopDownTargetCamera()
    {
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "label-1",
            "Label",
            "mesh",
            null,
            0,
            0,
            0,
            1,
            Variant: "rekall.text.label",
            ScaleX: 1,
            FacingMode: "camera-plane");
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera-1",
            "Camera",
            "Rekall.Camera3D",
            true,
            0,
            20000,
            1600,
            85.42607874009913,
            180,
            0);
        var frame = CreateFrame(renderable) with { ActiveCamera = camera, Cameras = [camera] };
        var mesh = new RekallAgeVulkanSceneMesh(
            "label-1",
            "Label",
            "line-segments",
            [
                new RekallAgeVulkanSceneVertex(0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 0, 0),
                new RekallAgeVulkanSceneVertex(1, 0, 0, 0, 1, 0, 1, 1, 1, 1, 0, 0),
                new RekallAgeVulkanSceneVertex(0, 0, -1, 0, 1, 0, 1, 1, 1, 1, 0, 0)
            ],
            [0, 1, 2]);

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.True(draw.Model.M11 > 0);
    }

    [Fact]
    public void BuildUsesActiveCameraAndLightInFrameUniform()
    {
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "cube-1",
                "Cube",
                "mesh",
                "rekall.primitive.cube",
                0,
                0,
                0,
                1,
                Variant: "rekall.geometry.cube"),
            new RekallAgeRuntimeViewportRenderable(
                "light-1",
                "Sun",
                "light",
                null,
                0,
                0,
                0,
                2,
                RotationX: -30,
                RotationY: 15,
                Intensity: 2));
        var camera = new RekallAgeRuntimeViewportCamera("camera-1", "Camera", "Rekall.Camera3D", true, 0, 1, 6, -5, 0, 0);
        frame = frame with { ActiveCamera = camera, Cameras = [camera] };
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);

        Assert.NotEqual(Matrix4x4.Identity, batch.Frame.ViewProjection);
        Assert.InRange(batch.Frame.LightDirection.Length(), 0.99f, 1.01f);
        Assert.Equal(new Vector4(2, 2, 2, 1), batch.Frame.LightColor);
        Assert.Equal(0, batch.Frame.LightPosition.W);
    }

    [Fact]
    public void BuildPublishesTheAutoFramedCameraUsedBySceneAndFogConsumers()
    {
        var authored = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Default Camera",
            "Camera3D",
            true,
            0,
            0,
            0);
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "cube",
            "Offset Cube",
            "mesh",
            "rekall.primitive.cube",
            10,
            4,
            20,
            1,
            Variant: "rekall.geometry.cube",
            ScaleX: 2,
            ScaleY: 2,
            ScaleZ: 2)) with
        {
            ActiveCamera = authored,
            Cameras = [authored]
        };
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);

        Assert.NotEqual(Vector3.Zero, batch.EffectiveCamera.Position);
        var toCenter = new Vector3(10, 4, 20) - batch.EffectiveCamera.Position;
        Assert.InRange(Vector3.Cross(Vector3.Normalize(toCenter), batch.EffectiveCamera.Forward).Length(), 0, 0.01f);
        Assert.True(Vector3.Dot(toCenter, batch.EffectiveCamera.Forward) > 0);
        Assert.Equal(batch.EffectiveCamera.Position, new Vector3(
            batch.Frame.CameraPosition.X,
            batch.Frame.CameraPosition.Y,
            batch.Frame.CameraPosition.Z));
        Assert.Equal(batch.EffectiveCamera.ViewProjection, batch.Frame.ViewProjection);
        Assert.True(batch.EffectiveCamera.AutoFramed);
    }

    [Fact]
    public void BuildPublishesOrthographicPerPixelOriginsAndParallelRays()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Orthographic Camera",
            "Camera3D",
            true,
            4,
            3,
            -5,
            ProjectionMode: "orthographic",
            OrthographicSize: 8,
            NearClip: 0.1,
            FarClip: 80);
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "cube",
            "Cube",
            "mesh",
            "rekall.primitive.cube",
            4,
            3,
            5,
            1,
            Variant: "rekall.geometry.cube")) with
        {
            ActiveCamera = camera,
            Cameras = [camera]
        };
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var effective = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes).EffectiveCamera;
        var leftOrigin = effective.ViewOrigin(new Vector2(0, 0.5f));
        var rightOrigin = effective.ViewOrigin(new Vector2(1, 0.5f));

        Assert.True(effective.Orthographic);
        Assert.InRange(Vector3.Distance(effective.Forward, effective.ViewRay(new Vector2(0, 0.5f))), 0, 0.0001f);
        Assert.InRange(Vector3.Distance(effective.Forward, effective.ViewRay(new Vector2(1, 0.5f))), 0, 0.0001f);
        Assert.InRange(Vector3.Distance(leftOrigin, rightOrigin), 14.21f, 14.23f);
        Assert.True(leftOrigin.X > rightOrigin.X);
    }

    [Theory]
    [InlineData("perspective")]
    [InlineData("orthographic")]
    public void EffectiveCameraViewReconstructionRoundTripsTopAndBottomFramebufferPixels(string projectionMode)
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Rotated Camera",
            "Camera3D",
            true,
            1,
            2,
            -5,
            RotationX: 12,
            RotationY: 20,
            RotationZ: 8,
            ProjectionMode: projectionMode,
            OrthographicSize: 8,
            NearClip: 0.1,
            FarClip: 80);
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "cube",
            "Cube",
            "mesh",
            "rekall.primitive.cube",
            4,
            4,
            8,
            1,
            Variant: "rekall.geometry.cube")) with
        {
            ActiveCamera = camera,
            Cameras = [camera]
        };
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);
        var effective = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes).EffectiveCamera;
        var topUv = new Vector2(0.37f, 0.18f);
        var bottomUv = new Vector2(0.37f, 0.82f);

        var topWorld = effective.ViewOrigin(topUv) + effective.ViewRay(topUv) * 12;
        var bottomWorld = effective.ViewOrigin(bottomUv) + effective.ViewRay(bottomUv) * 12;
        var projectedTop = ProjectToFramebufferUv(effective.ViewProjection, topWorld);
        var projectedBottom = ProjectToFramebufferUv(effective.ViewProjection, bottomWorld);

        Assert.InRange(Vector2.Distance(topUv, projectedTop), 0, 0.0001f);
        Assert.InRange(Vector2.Distance(bottomUv, projectedBottom), 0, 0.0001f);
        Assert.True(Vector3.Dot(topWorld - bottomWorld, effective.Up) > 0,
            "Framebuffer-top reconstruction must lie above framebuffer-bottom reconstruction in camera space.");
    }

    [Fact]
    public void BuildUsesPointLightPositionWhenFrameContainsPointLight()
    {
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "planet-1",
                "Planet",
                "mesh",
                "rekall.planet.surface",
                30,
                0,
                0,
                1,
                Variant: "rekall.planet.surface"),
            new RekallAgeRuntimeViewportRenderable(
                "sun-light",
                "Sun Light",
                "light",
                null,
                0,
                0,
                0,
                2,
                Variant: "PointLight",
                Intensity: 3));
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);

        Assert.Equal(new Vector4(0, 0, 0, 1), batch.Frame.LightPosition);
        Assert.Equal(new Vector4(3, 3, 3, 1), batch.Frame.LightColor);
    }

    [Fact]
    public void BuildSelectsFourPracticalsByPriorityIntensityAndStableId()
    {
        var lights = new[]
        {
            new RekallAgeRuntimeViewportRenderable("z-low", "Low", "light", null, 0, 1, 0, 1, Variant: "PointLight", Intensity: 20, MaterialColor: "#ffffff") { LightPriority = 1 },
            new RekallAgeRuntimeViewportRenderable("b-priority", "B", "light", null, 0, 2, 0, 2, Variant: "PointLight", Intensity: 4, MaterialColor: "#ffffff") { LightPriority = 8 },
            new RekallAgeRuntimeViewportRenderable("a-priority", "A", "light", null, 0, 3, 0, 3, Variant: "PointLight", Intensity: 4, MaterialColor: "#ffffff") { LightPriority = 8 },
            new RekallAgeRuntimeViewportRenderable("c-priority", "C", "light", null, 0, 4, 0, 4, Variant: "PointLight", Intensity: 2, MaterialColor: "#ffffff") { LightPriority = 8 },
            new RekallAgeRuntimeViewportRenderable("d-middle", "D", "light", null, 0, 5, 0, 5, Variant: "PointLight", Intensity: 9, MaterialColor: "#ffffff") { LightPriority = 4 }
        };
        var frame = CreateFrame(lights);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, []);

        Assert.Equal(["a-priority", "b-priority", "c-priority", "d-middle"], batch.Frame.PointLights.Select(item => item.EntityId));
        Assert.Equal(new Vector4(0, 3, 0, 1), batch.Frame.PointLights[0].Position);
        Assert.Equal(batch.Frame.PointLights[0].Position, batch.Frame.AdditionalLightPosition);
    }

    [Fact]
    public void HighQualityBatchKeepsSixteenPracticalsInStablePriorityOrder()
    {
        var lights = Enumerable.Range(1, 18)
            .Select(index => new RekallAgeRuntimeViewportRenderable(
                $"light-{index:D2}",
                $"Light {index:D2}",
                "light",
                null,
                index,
                2,
                0,
                index,
                Variant: "PointLight",
                Intensity: index,
                MaterialColor: "#ffffff")
            {
                LightPriority = index,
                LightRange = 12
            })
            .ToArray();
        var frame = CreateFrame(lights) with
        {
            ResolvedQualityPlan = new RekallAgeRenderQualityProfileResolver().Resolve(
                new RekallAgeRenderQualityIntent("High"),
                RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
                128,
                72)
        };

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, []);

        Assert.Equal(16, batch.Frame.PointLights.Count);
        Assert.Equal(
            Enumerable.Range(3, 16).Reverse().Select(index => $"light-{index:D2}"),
            batch.Frame.PointLights.Select(item => item.EntityId));
        Assert.Equal(16, batch.Frame.PointLightBudget);
        Assert.Equal(["light-02", "light-01"], batch.Frame.DroppedPointLightEntityIds);
    }

    [Fact]
    public void BuildResolvesASpotLightWithDirectionFromRotationAndConeAnglesFromDegrees()
    {
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "flashlight", "Flashlight", "light", null, 1, 2, 3, 1,
                Variant: "SpotLight", Intensity: 5, MaterialColor: "#ffffff",
                RotationX: 0, RotationY: 180, RotationZ: 0)
            {
                LightRange = 25,
                LightPriority = 7,
                LightInnerConeAngle = 10,
                LightOuterConeAngle = 20
            });

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, []);

        Assert.Single(batch.Frame.SpotLights);
        var spot = batch.Frame.SpotLights[0];
        Assert.Equal("flashlight", spot.EntityId);
        Assert.Equal(new Vector4(1, 2, 3, 1), spot.Position);
        Assert.Equal(25, spot.Parameters.X);
        Assert.Equal(7, spot.Parameters.Y);
        Assert.Equal(MathF.Cos(10 * MathF.PI / 180f), spot.Parameters.Z, 5);
        Assert.Equal(MathF.Cos(20 * MathF.PI / 180f), spot.Parameters.W, 5);
        // A 180-degree yaw about Y should flip the default +Z forward direction to -Z.
        Assert.Equal(-1, spot.Direction.Z, 3);
    }

    [Fact]
    public void BuildKeepsAtMostFourSpotLightsByPriorityIntensityAndStableId()
    {
        var lights = new[]
        {
            new RekallAgeRuntimeViewportRenderable("z-low", "Low", "light", null, 0, 1, 0, 1, Variant: "SpotLight", Intensity: 20, MaterialColor: "#ffffff") { LightPriority = 1 },
            new RekallAgeRuntimeViewportRenderable("b-priority", "B", "light", null, 0, 2, 0, 2, Variant: "SpotLight", Intensity: 4, MaterialColor: "#ffffff") { LightPriority = 8 },
            new RekallAgeRuntimeViewportRenderable("a-priority", "A", "light", null, 0, 3, 0, 3, Variant: "SpotLight", Intensity: 4, MaterialColor: "#ffffff") { LightPriority = 8 },
            new RekallAgeRuntimeViewportRenderable("c-priority", "C", "light", null, 0, 4, 0, 4, Variant: "SpotLight", Intensity: 2, MaterialColor: "#ffffff") { LightPriority = 8 },
            new RekallAgeRuntimeViewportRenderable("d-middle", "D", "light", null, 0, 5, 0, 5, Variant: "SpotLight", Intensity: 9, MaterialColor: "#ffffff") { LightPriority = 4 },
            new RekallAgeRuntimeViewportRenderable("e-dropped", "E", "light", null, 0, 6, 0, 6, Variant: "SpotLight", Intensity: 1, MaterialColor: "#ffffff") { LightPriority = 0 }
        };
        var frame = CreateFrame(lights);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, []);

        Assert.Equal(4, batch.Frame.SpotLightBudget);
        Assert.Equal(["a-priority", "b-priority", "c-priority", "d-middle"], batch.Frame.SpotLights.Select(item => item.EntityId));
        Assert.Equal(["z-low", "e-dropped"], batch.Frame.DroppedSpotLightEntityIds);
    }

    [Fact]
    public void BuildClampsAnInvertedConeSoInnerNeverExceedsOuter()
    {
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "confused", "Confused", "light", null, 0, 0, 0, 1,
                Variant: "SpotLight", Intensity: 5, MaterialColor: "#ffffff")
            {
                LightInnerConeAngle = 45,
                LightOuterConeAngle = 10
            });

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, []);

        var spot = batch.Frame.SpotLights[0];
        Assert.Equal(spot.Parameters.W, spot.Parameters.Z, 5);
    }

    [Fact]
    public void DefaultBatchKeepsDirectionalKeyAndFirstPointPractical()
    {
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "mesh", "Mesh", "mesh", "rekall.primitive.cube", 0, 0, 5, 1,
                Variant: "rekall.geometry.cube"),
            new RekallAgeRuntimeViewportRenderable(
                "moon", "Moon", "light", null, 0, 0, 0, 2,
                Variant: "DirectionalLight", Intensity: 2, MaterialColor: "#8090a0"),
            new RekallAgeRuntimeViewportRenderable(
                "lamp", "Lamp", "light", null, 1, 2, 3, 3,
                Variant: "PointLight", Intensity: 3, MaterialColor: "#ff8000"));
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);

        Assert.Equal(0, batch.Frame.LightPosition.W);
        Assert.Equal(new Vector4(1, 2, 3, 1), batch.Frame.AdditionalLightPosition);
        Assert.Equal(3, batch.Frame.AdditionalLightColor.X);
        Assert.Equal(3 * 128f / 255f, batch.Frame.AdditionalLightColor.Y, 5);
        Assert.Equal(0, batch.Frame.AdditionalLightColor.Z);
    }

    [Fact]
    public void AdditionalPracticalUsesPriorityThenIntensityThenStableEntityIdAndPreservesRange()
    {
        var low = new RekallAgeRuntimeViewportRenderable(
            "first", "First", "light", null, 1, 0, 0, 2,
            Variant: "PointLight", Intensity: 2, MaterialColor: "#ffffff")
        {
            LightRange = 30
        };
        var hero = new RekallAgeRuntimeViewportRenderable(
            "hero", "Hero", "light", null, 2, 3, 4, 3,
            Variant: "PointLight", Intensity: 8, MaterialColor: "#d99b54")
        {
            LightRange = 7.5,
            LightPriority = 5
        };
        var brighterButLowerPriority = new RekallAgeRuntimeViewportRenderable(
            "bright", "Bright", "light", null, 9, 9, 9, 4,
            Variant: "PointLight", Intensity: 12, MaterialColor: "#ffffff")
        {
            LightRange = 20,
            LightPriority = 1
        };
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "mesh", "Mesh", "mesh", "rekall.primitive.cube", 0, 0, 5, 1,
                Variant: "rekall.geometry.cube"),
            low,
            brighterButLowerPriority,
            hero);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);

        Assert.Equal(new Vector4(2, 3, 4, 1), batch.Frame.AdditionalLightPosition);
        Assert.Equal(7.5f, batch.Frame.AdditionalLightParameters.X);
        Assert.Equal(5, batch.Frame.AdditionalLightParameters.Y);
        Assert.Equal(8 * 217f / 255f, batch.Frame.AdditionalLightColor.X, 5);
    }

    [Fact]
    public void ShadowSelectedDirectionalLightIsTheDirectLightAttenuatedBySceneShading()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Camera3D",
            true,
            0,
            2,
            -6,
            NearClip: 0.1,
            FarClip: 80);
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "mesh",
                "Mesh",
                "mesh",
                "rekall.primitive.cube",
                0,
                0,
                5,
                1,
                Variant: "rekall.geometry.cube"),
            new RekallAgeRuntimeViewportRenderable(
                "point",
                "Point",
                "light",
                null,
                0,
                2,
                3,
                2,
                Variant: "PointLight",
                Intensity: 4,
                MaterialColor: "#00ff00"),
            new RekallAgeRuntimeViewportRenderable(
                "lower-directional",
                "Lower Directional",
                "light",
                null,
                0,
                0,
                0,
                3,
                Variant: "DirectionalLight",
                RotationY: 30,
                Intensity: 3,
                MaterialColor: "#0000ff")
            {
                ShadowPriority = 10
            },
            new RekallAgeRuntimeViewportRenderable(
                "shadow-directional",
                "Shadow Directional",
                "light",
                null,
                0,
                0,
                0,
                4,
                Variant: "DirectionalLight",
                RotationX: 55,
                RotationY: -20,
                Intensity: 2,
                MaterialColor: "#ff0000")
            {
                ShadowPriority = 20
            }) with
        {
            ActiveCamera = camera,
            Cameras = [camera],
            PostProcessStack = new RekallAgeRuntimeViewportPostProcessStack(
                "post",
                "Post",
                true,
                [new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")]),
            ResolvedQualityPlan = new RekallAgeRenderQualityProfileResolver().Resolve(
                new RekallAgeRenderQualityIntent("High", Bloom: false),
                RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
                128,
                72)
        };
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var highFidelity = Assert.IsType<RekallAgeVulkanHighFidelityFramePlan>(
            new RekallAgeVulkanHighFidelityFrameRenderer().Plan(frame, meshes));
        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(
            frame,
            meshes,
            highFidelity.ShadowPlan.LightEntityId);

        Assert.Equal("shadow-directional", highFidelity.ShadowPlan.LightEntityId);
        Assert.Equal(0, batch.Frame.LightPosition.W);
        Assert.Equal(new Vector4(2, 0, 0, 1), batch.Frame.LightColor);
        Assert.Equal(new Vector4(0, 2, 3, 1), batch.Frame.AdditionalLightPosition);
        Assert.Equal(new Vector4(0, 4, 0, 1), batch.Frame.AdditionalLightColor);
    }

    [Fact]
    public void HighFidelitySelectionIgnoresPointFirstAndUsesHighestPriorityDirectionalEverywhere()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera", "Camera", "Camera3D", true, 0, 2, -6, NearClip: 0.1, FarClip: 80);
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "mesh", "Mesh", "mesh", "rekall.primitive.cube", 0, 0, 5, 1,
                Variant: "rekall.geometry.cube"),
            new RekallAgeRuntimeViewportRenderable(
                "point-first", "Point", "light", null, 0, 2, 3, 2,
                Variant: "PointLight", Intensity: 4, MaterialColor: "#00ff00"),
            new RekallAgeRuntimeViewportRenderable(
                "directional-low", "Low", "light", null, 0, 0, 0, 3,
                Variant: "DirectionalLight", RotationY: 30, Intensity: 3, MaterialColor: "#0000ff")
            {
                ShadowPriority = 10
            },
            new RekallAgeRuntimeViewportRenderable(
                "directional-selected", "Selected", "light", null, 0, 0, 0, 4,
                Variant: "DirectionalLight", RotationX: 55, Intensity: 2, MaterialColor: "#ff0000")
            {
                ShadowPriority = 20
            }) with
        {
            ActiveCamera = camera,
            Cameras = [camera],
            PostProcessStack = new RekallAgeRuntimeViewportPostProcessStack(
                "post", "Post", true, [new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")]),
            ResolvedQualityPlan = new RekallAgeRenderQualityProfileResolver().Resolve(
                new RekallAgeRenderQualityIntent("High", Bloom: false),
                RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
                128,
                72),
            FogVolumes =
            [
                new RekallAgeRuntimeViewportFogVolume(
                    "fog", "Fog", "global", 0.1, "#ffffff", "#000000", 0, 0, 0, 0,
                    RekallAgeRuntimeViewportTransform.Identity)
            ]
        };
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var plan = Assert.IsType<RekallAgeVulkanHighFidelityFramePlan>(
            new RekallAgeVulkanHighFidelityFrameRenderer().Plan(frame, meshes));
        var prepared = RekallAgeVulkanScenePreparedFrameBuilder.Build(
            frame,
            meshes,
            RekallAgeVulkanSceneRenderTarget.OffscreenCapture(128, 72),
            directionalLight: plan.DirectionalLight,
            effectiveCamera: plan.EffectiveCamera);

        Assert.True(plan.DirectionalLight.Available);
        Assert.Equal("directional-selected", plan.DirectionalLight.EntityId);
        Assert.Equal(plan.DirectionalLight.EntityId, plan.ShadowPlan.LightEntityId);
        Assert.Equal(plan.DirectionalLight.EntityId, plan.FogPlan.DirectLightEntityId);
        Assert.True(plan.FogPlan.DirectLightAvailable);
        Assert.Equal(plan.DirectionalLight.Direction, prepared.Batch.Frame.LightDirection);
        Assert.Equal(plan.DirectionalLight.Color, prepared.Batch.Frame.LightColor);
        Assert.Equal(0, prepared.Batch.Frame.LightPosition.W);
        Assert.Equal(new Vector4(0, 2, 3, 1), prepared.Batch.Frame.AdditionalLightPosition);
    }

    [Fact]
    public void HighFidelitySelectionReportsNoDirectionalWithoutSyntheticOrPointMasquerade()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera", "Camera", "Camera3D", true, 0, 2, -6, NearClip: 0.1, FarClip: 80);
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "mesh", "Mesh", "mesh", "rekall.primitive.cube", 0, 0, 5, 1,
                Variant: "rekall.geometry.cube"),
            new RekallAgeRuntimeViewportRenderable(
                "point-only", "Point", "light", null, 0, 2, 3, 2,
                Variant: "PointLight", Intensity: 4, MaterialColor: "#00ff00")) with
        {
            ActiveCamera = camera,
            Cameras = [camera],
            PostProcessStack = new RekallAgeRuntimeViewportPostProcessStack(
                "post", "Post", true, [new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")]),
            ResolvedQualityPlan = new RekallAgeRenderQualityProfileResolver().Resolve(
                new RekallAgeRenderQualityIntent("High", Bloom: false),
                RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
                128,
                72),
            FogVolumes =
            [
                new RekallAgeRuntimeViewportFogVolume(
                    "fog", "Fog", "global", 0.1, "#ffffff", "#000000", 0, 0, 0, 0,
                    RekallAgeRuntimeViewportTransform.Identity)
            ]
        };
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var plan = Assert.IsType<RekallAgeVulkanHighFidelityFramePlan>(
            new RekallAgeVulkanHighFidelityFrameRenderer().Plan(frame, meshes));
        var prepared = RekallAgeVulkanScenePreparedFrameBuilder.Build(
            frame,
            meshes,
            RekallAgeVulkanSceneRenderTarget.OffscreenCapture(128, 72),
            directionalLight: plan.DirectionalLight,
            effectiveCamera: plan.EffectiveCamera);

        Assert.False(plan.DirectionalLight.Available);
        Assert.Null(plan.DirectionalLight.EntityId);
        Assert.Null(plan.FogPlan.DirectLightEntityId);
        Assert.False(plan.FogPlan.DirectLightAvailable);
        Assert.False(plan.ShadowPlan.Enabled);
        Assert.Equal(Vector3.Zero, prepared.Batch.Frame.LightDirection);
        Assert.Equal(Vector4.Zero, prepared.Batch.Frame.LightColor);
        Assert.Equal(Vector4.Zero, prepared.Batch.Frame.LightPosition);
        Assert.Equal(new Vector4(0, 2, 3, 1), prepared.Batch.Frame.AdditionalLightPosition);
    }

    [Fact]
    public void HighFidelitySelectionIgnoresSpotAndCustomLightsAheadOfCanonicalDirectional()
    {
        var spot = new RekallAgeRuntimeViewportRenderable(
            "spot-first", "Spot", "light", null, 0, 2, 3, 1,
            Variant: "SpotLight", Intensity: 4, MaterialColor: "#00ff00")
        {
            ShadowPriority = 300
        };
        var custom = new RekallAgeRuntimeViewportRenderable(
            "custom-first", "Custom", "light", null, 0, 2, 3, 2,
            Variant: "CustomDirectionalLight", Intensity: 4, MaterialColor: "#0000ff")
        {
            ShadowPriority = 200
        };
        var directional = new RekallAgeRuntimeViewportRenderable(
            "canonical-directional", "Directional", "light", null, 0, 0, 0, 3,
            Variant: "rEkAlL.DiReCtIoNaLlIgHt", RotationX: 45, Intensity: 2, MaterialColor: "#ff0000")
        {
            ShadowPriority = 10
        };
        var frame = CreateHighFidelityLightFrame(spot, custom, directional);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var plan = Assert.IsType<RekallAgeVulkanHighFidelityFramePlan>(
            new RekallAgeVulkanHighFidelityFrameRenderer().Plan(frame, meshes));

        Assert.True(plan.DirectionalLight.Available);
        Assert.Equal("canonical-directional", plan.DirectionalLight.EntityId);
        Assert.Equal("canonical-directional", plan.ShadowPlan.LightEntityId);
        Assert.Equal("canonical-directional", plan.FogPlan.DirectLightEntityId);
    }

    [Theory]
    [InlineData("SpotLight")]
    [InlineData("CustomLight")]
    [InlineData("Agent.DirectionalLight")]
    public void HighFidelitySelectionReportsNoneWhenCanonicalDirectionalIsAbsent(string variant)
    {
        var frame = CreateHighFidelityLightFrame(new RekallAgeRuntimeViewportRenderable(
            "unsupported-light", "Unsupported", "light", null, 0, 2, 3, 1,
            Variant: variant, Intensity: 4, MaterialColor: "#ffffff")
        {
            ShadowPriority = 300
        });
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var plan = Assert.IsType<RekallAgeVulkanHighFidelityFramePlan>(
            new RekallAgeVulkanHighFidelityFrameRenderer().Plan(frame, meshes));

        Assert.False(plan.DirectionalLight.Available);
        Assert.Null(plan.DirectionalLight.EntityId);
        Assert.False(plan.ShadowPlan.Enabled);
        Assert.Null(plan.ShadowPlan.LightEntityId);
        Assert.False(plan.FogPlan.DirectLightAvailable);
        Assert.Null(plan.FogPlan.DirectLightEntityId);
    }

    [Fact]
    public void BuildTintsLightColorFromAuthoredLightMaterialColor()
    {
        var frame = CreateFrame(
            new RekallAgeRuntimeViewportRenderable(
                "planet-1",
                "Planet",
                "mesh",
                "rekall.planet.surface",
                30,
                0,
                0,
                1,
                Variant: "rekall.planet.surface"),
            new RekallAgeRuntimeViewportRenderable(
                "sun-light",
                "Sun Light",
                "light",
                null,
                0,
                0,
                0,
                2,
                Variant: "PointLight",
                Intensity: 2,
                MaterialColor: "#ffb347"));
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);

        Assert.Equal(2, batch.Frame.LightColor.X, precision: 3);
        Assert.Equal(1.404, batch.Frame.LightColor.Y, precision: 3);
        Assert.Equal(0.557, batch.Frame.LightColor.Z, precision: 3);
        Assert.Equal(1, batch.Frame.LightColor.W);
    }

    [Fact]
    public void BuildAllowsLargeImportedModelsSplitIntoMultipleMeshChunks()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "station",
            "Station",
            "mesh",
            "asset_station",
            0,
            0,
            0,
            1));
        var first = CreateLargeMesh("station", ushort.MaxValue);
        var second = CreateLargeMesh("station", 3);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, [first, second]);

        Assert.Equal(2, batch.Draws.Count);
        Assert.Equal(0, batch.Draws[0].VertexOffset);
        Assert.Equal(ushort.MaxValue, batch.Draws[1].VertexOffset);
    }

    [Fact]
    public void BuildIndexesRenderablesInsteadOfRepeatedlyScanningPerMesh()
    {
        var renderables = new CountingRenderableList(Enumerable.Range(0, 6)
            .Select(index => new RekallAgeRuntimeViewportRenderable(
                $"entity-{index}",
                $"Entity {index}",
                "mesh",
                $"asset-{index}",
                index,
                0,
                0,
                index + 1))
            .ToArray());
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            128,
            72,
            null,
            [],
            renderables,
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
        var meshes = renderables
            .Select(renderable => new RekallAgeVulkanSceneMesh(
                renderable.EntityId,
                renderable.EntityName,
                "glb",
                [
                    new RekallAgeVulkanSceneVertex(0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 0, 0),
                    new RekallAgeVulkanSceneVertex(1, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 0),
                    new RekallAgeVulkanSceneVertex(0, 1, 0, 0, 1, 0, 1, 1, 1, 1, 0, 1)
                ],
                [0, 1, 2]))
            .ToArray();

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);

        Assert.Equal(meshes.Length, batch.Draws.Count);
        Assert.True(
            renderables.EnumerationCount <= 3,
            $"Expected renderables to be indexed once and reused, but they were enumerated {renderables.EnumerationCount} times.");
    }

    [Fact]
    public void BuildCarriesMeshTextureIdIntoDrawRanges()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "station",
            "Station",
            "mesh",
            "asset_station",
            0,
            0,
            0,
            1));
        var texture = new RekallAgeVulkanSceneTexture(
            "asset_station/texture/0",
            1,
            1,
            [255, 255, 255, 255],
            new RekallAgeVulkanSceneSampler(
                RekallAgeVulkanSceneFilter.Linear,
                RekallAgeVulkanSceneFilter.Linear,
                RekallAgeVulkanSceneWrapMode.Repeat,
                RekallAgeVulkanSceneWrapMode.Repeat));
        var mesh = new RekallAgeVulkanSceneMesh(
            "station",
            "Station Chunk",
            "glb",
            [
                new RekallAgeVulkanSceneVertex(0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 0, 0),
                new RekallAgeVulkanSceneVertex(1, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 0),
                new RekallAgeVulkanSceneVertex(0, 1, 0, 0, 1, 0, 1, 1, 1, 1, 0, 1)
            ],
            [0, 1, 2],
            texture);

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.Equal("asset_station/texture/0", draw.TextureId);
    }

    [Fact]
    public void BuildCarriesEmissiveTextureIdAndFactorsIntoDrawRanges()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "lamp",
            "Lamp",
            "mesh",
            "rekall.geometry.sphere",
            0,
            0,
            0,
            1));
        var texture = new RekallAgeVulkanSceneTexture(
            "asset_lamp/emissive",
            1,
            1,
            [255, 255, 255, 255],
            new RekallAgeVulkanSceneSampler(
                RekallAgeVulkanSceneFilter.Linear,
                RekallAgeVulkanSceneFilter.Linear,
                RekallAgeVulkanSceneWrapMode.Repeat,
                RekallAgeVulkanSceneWrapMode.Repeat));
        var mesh = new RekallAgeVulkanSceneMesh(
            "lamp",
            "Lamp Mesh",
            "sphere",
            [
                new RekallAgeVulkanSceneVertex(0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 0, 0),
                new RekallAgeVulkanSceneVertex(1, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 0),
                new RekallAgeVulkanSceneVertex(0, 1, 0, 0, 1, 0, 1, 1, 1, 1, 0, 1)
            ],
            [0, 1, 2],
            EmissiveTexture: texture,
            EmissiveFactor: new Vector4(1, 0.5f, 0.1f, 4));

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.Equal("asset_lamp/emissive", draw.EmissiveTextureId);
        Assert.Equal(new Vector4(1, 0.5f, 0.1f, 4), draw.EmissiveFactors);
    }

    [Fact]
    public void BuildPreservesSurfaceMaterialFactorsWhenAtmosphereIsBoundForLighting()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "planet",
            "Planet",
            "mesh",
            "rekall.planet.surface",
            0,
            0,
            0,
            1,
            Variant: "rekall.planet.surface",
            MetallicFactor: 0.2,
            RoughnessFactor: 0.7,
            Atmosphere: new RekallAgeRuntimeViewportAtmosphereMaterial(
                PlanetRadius: 1,
                AtmosphereRadius: 1.05,
                RayleighColor: "#3366ff",
                MieColor: "#ffe0aa",
                Density: 0.75,
                SunIntensity: 18,
                OzoneAbsorptionColor: "#ffd199",
                OzoneAbsorption: 0.012,
                AerialPerspectiveStrength: 0.65)));
        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.Equal(new Vector4(0.2f, 0.7f, 0, 0), draw.MaterialFactors);
        Assert.Equal(1, draw.AtmosphereFactors0.X);
        Assert.Equal(1.05f, draw.AtmosphereFactors0.Y);
        Assert.True(draw.AtmosphereFactors1.W < 0);
        Assert.Equal(new Vector4(0.2f, 0.4f, 1, 1), draw.AtmosphereColor0);
        Assert.Equal(1, draw.AtmosphereColor1.X);
        Assert.Equal(0.65f, draw.AtmosphereColor1.W);
        Assert.Equal(1, draw.AtmosphereColor2.X);
        Assert.Equal(0.012f, draw.AtmosphereColor2.W);
    }

    [Fact]
    public void BuildUsesAtmosphereSampleCountsOnlyForAtmosphereShellDraws()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "planet:atmosphere",
            "Planet",
            "mesh",
            "rekall.planet.atmosphere",
            0,
            0,
            0,
            1,
            Variant: "rekall.planet.atmosphere",
            Atmosphere: new RekallAgeRuntimeViewportAtmosphereMaterial(
                PlanetRadius: 1,
                AtmosphereRadius: 1.05,
                RayleighColor: "#3366ff",
                MieColor: "#ffe0aa",
                ViewSampleCount: 20,
                LightSampleCount: 10,
                SunIntensity: 18,
                OzoneAbsorptionColor: "#ffd199",
                OzoneAbsorption: 0.012,
                AerialPerspectiveStrength: 0.65)));
        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.Equal(new Vector4(20, 10, 0, 0), draw.MaterialFactors);
        Assert.True(draw.AtmosphereFactors1.W > 0);
        Assert.Equal(new Vector4(0.2f, 0.4f, 1, 1), draw.AtmosphereColor0);
        Assert.Equal(0.65f, draw.AtmosphereColor1.W);
        Assert.Equal(0.012f, draw.AtmosphereColor2.W);
    }

    [Fact]
    public void BuildPreservesCloudLayerMaterialControls()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "planet:clouds",
            "Planet",
            "mesh",
            "rekall.planet.cloud-layer",
            0,
            0,
            0,
            1,
            Variant: "rekall.planet.cloud-layer",
            MaterialColor: "#fff4ddcc",
            CloudLayer: new RekallAgeRuntimeViewportCloudLayerMaterial(
                Radius: 1.02,
                Color: "#fff4ddcc",
                AlphaFromTextureOnly: true,
                Coverage: 1.4,
                LambertianStrength: 0.35,
                AmbientStrength: 0.22)));
        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.Equal(new Vector4(1, 1.4f, 0.35f, 0.22f), draw.CloudFactors);
        Assert.Equal(1, draw.CloudColor.X);
        Assert.Equal(0.8f, draw.CloudColor.W, 2);
    }

    [Fact]
    public void BuildPreservesCloudShadowMaterialControls()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "planet",
            "Planet",
            "mesh",
            "rekall.planet.surface",
            0,
            0,
            0,
            1,
            Variant: "rekall.planet.surface",
            TextureAssetId: "earth",
            CloudShadow: new RekallAgeRuntimeViewportCloudShadowMaterial(
                "clouds",
                CloudRadius: 1.08,
                Strength: 0.42)));
        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.Equal("clouds", draw.CloudShadowTextureId);
        Assert.Equal(new Vector4(1, 1.08f, 0.42f, 0), draw.CloudShadowFactors);
    }

    [Fact]
    public void BuildPreservesSurfaceWaterMaterialControls()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "planet",
            "Planet",
            "mesh",
            "rekall.planet.surface",
            0,
            0,
            0,
            1,
            Variant: "rekall.planet.surface",
            TextureAssetId: "earth",
            SurfaceWater: new RekallAgeRuntimeViewportSurfaceWaterMaterial(
                "earth_ocean",
                Coverage: 1.35,
                SpecularStrength: 3.2,
                Roughness: 0.08)));
        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);

        Assert.Equal("earth_ocean", draw.SurfaceWaterTextureId);
        Assert.Equal(new Vector4(1, 1.35f, 3.2f, 0.08f), draw.SurfaceWaterFactors);
    }

    private static RekallAgeVulkanSceneMesh CreateLargeMesh(string entityId, int vertexCount)
    {
        var vertices = Enumerable.Range(0, vertexCount)
            .Select(index => new RekallAgeVulkanSceneVertex(index % 32, 0, index / 32, 0, 1, 0, 0.6f, 0.7f, 0.8f, 1, 0, 0))
            .ToArray();
        return new RekallAgeVulkanSceneMesh(
            entityId,
            "Chunk",
            "glb",
            vertices,
            [0, 1, 2]);
    }

    private static Vector2 ProjectToFramebufferUv(Matrix4x4 viewProjection, Vector3 worldPosition)
    {
        var clip = Vector4.Transform(new Vector4(worldPosition, 1), viewProjection);
        var inverseW = 1 / clip.W;
        return new Vector2(clip.X * inverseW, clip.Y * inverseW) * 0.5f + new Vector2(0.5f);
    }

    private static RekallAgeRuntimeViewportFrame CreateFrame(params RekallAgeRuntimeViewportRenderable[] renderables)
    {
        return new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            128,
            72,
            null,
            [],
            renderables,
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
    }

    private static RekallAgeRuntimeViewportFrame CreateHighFidelityLightFrame(
        params RekallAgeRuntimeViewportRenderable[] lights)
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera", "Camera", "Camera3D", true, 0, 2, -6, NearClip: 0.1, FarClip: 80);
        return CreateFrame([
            new RekallAgeRuntimeViewportRenderable(
                "mesh", "Mesh", "mesh", "rekall.primitive.cube", 0, 0, 5, 1,
                Variant: "rekall.geometry.cube"),
            .. lights]) with
        {
            ActiveCamera = camera,
            Cameras = [camera],
            PostProcessStack = new RekallAgeRuntimeViewportPostProcessStack(
                "post", "Post", true, [new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")]),
            ResolvedQualityPlan = new RekallAgeRenderQualityProfileResolver().Resolve(
                new RekallAgeRenderQualityIntent("High", Bloom: false),
                RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
                128,
                72),
            FogVolumes =
            [
                new RekallAgeRuntimeViewportFogVolume(
                    "fog", "Fog", "global", 0.1, "#ffffff", "#000000", 0, 0, 0, 0,
                    RekallAgeRuntimeViewportTransform.Identity)
            ]
        };
    }

    private sealed class CountingRenderableList : IReadOnlyList<RekallAgeRuntimeViewportRenderable>
    {
        private readonly IReadOnlyList<RekallAgeRuntimeViewportRenderable> _inner;

        public CountingRenderableList(IReadOnlyList<RekallAgeRuntimeViewportRenderable> inner)
        {
            _inner = inner;
        }

        public int EnumerationCount { get; private set; }

        public int Count => _inner.Count;

        public RekallAgeRuntimeViewportRenderable this[int index] => _inner[index];

        public IEnumerator<RekallAgeRuntimeViewportRenderable> GetEnumerator()
        {
            EnumerationCount++;
            return _inner.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
