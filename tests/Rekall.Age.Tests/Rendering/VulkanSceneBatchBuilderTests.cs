using System.Numerics;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanSceneBatchBuilderTests
{
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
