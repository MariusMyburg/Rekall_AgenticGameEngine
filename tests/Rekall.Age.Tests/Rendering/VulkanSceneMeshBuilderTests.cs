using System.Numerics;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanSceneMeshBuilderTests
{
    [Theory]
    [InlineData("cube", 24, 36)]
    [InlineData("plane", 4, 6)]
    [InlineData("sphere", 117, 576)]
    [InlineData("cylinder", 70, 192)]
    [InlineData("cone", 52, 96)]
    public void BuildMeshesCreatesPrimitiveVertexAndIndexData(string primitive, int expectedVertices, int expectedIndices)
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "entity-1",
            "Primitive",
            "mesh",
            $"rekall.primitive.{primitive}",
            0,
            0,
            0,
            1,
            Variant: $"rekall.geometry.{primitive}",
            MaterialColor: "#33ff66"));

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        Assert.Equal(primitive, mesh.Primitive);
        Assert.Equal(expectedVertices, mesh.Vertices.Count);
        Assert.Equal(expectedIndices, mesh.Indices.Count);
        Assert.All(mesh.Vertices, vertex =>
        {
            Assert.InRange(vertex.R, 0.19f, 0.21f);
            Assert.InRange(vertex.G, 0.99f, 1.0f);
            Assert.InRange(vertex.B, 0.39f, 0.41f);
            Assert.Equal(1, vertex.A);
        });
    }

    [Fact]
    public void BuildMeshesCreatesHighResolutionPlanetSurfaceMesh()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "planet-1",
            "Gaia",
            "mesh",
            "rekall.planet.surface",
            0,
            0,
            0,
            1,
            Variant: "rekall.planet.surface",
            MaterialColor: "#4b86d8"));

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        Assert.Equal("planet", mesh.Primitive);
        Assert.True(mesh.Vertices.Count > 1500);
        Assert.True(mesh.Indices.Count > 9000);
        Assert.All(mesh.Vertices, vertex =>
        {
            Assert.InRange(MathF.Sqrt(vertex.NormalX * vertex.NormalX + vertex.NormalY * vertex.NormalY + vertex.NormalZ * vertex.NormalZ), 0.99f, 1.01f);
            Assert.InRange(vertex.U, 0, 1);
            Assert.InRange(vertex.V, 0, 1);
        });
    }

    [Fact]
    public void BuildMeshesExpandsViewportLineSegmentsToThinDebugGeometry()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "debug-lines",
            "Debug Lines",
            "mesh",
            null,
            0,
            0,
            0,
            900,
            Variant: "rekall.debug.lines",
            MaterialColor: "#33ddff66",
            LineSegments: new RekallAgeRuntimeViewportLineSegments(
            [
                new RekallAgeRuntimeViewportLineSegment(-1, 0, 0, 1, 0, 0),
                new RekallAgeRuntimeViewportLineSegment(0, -1, 0, 0, 1, 0)
            ],
            0.05)));

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        Assert.Equal("line-segments", mesh.Primitive);
        Assert.Equal(16, mesh.Vertices.Count);
        Assert.Equal(24, mesh.Indices.Count);
        Assert.All(mesh.Vertices, vertex =>
        {
            Assert.Equal(51f / 255f, vertex.R, 6);
            Assert.Equal(221f / 255f, vertex.G, 6);
            Assert.Equal(1f, vertex.B, 6);
            Assert.Equal(102f / 255f, vertex.A, 6);
        });
    }

    [Theory]
    [InlineData("sphere")]
    [InlineData("surface")]
    public void BuildMeshesCreatesOutwardFacingSphereWinding(string primitive)
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "entity-1",
            "Round Thing",
            "mesh",
            $"rekall.geometry.{primitive}",
            0,
            0,
            0,
            1,
            Variant: primitive == "surface" ? "rekall.planet.surface" : "rekall.geometry.sphere"));

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));
        var checkedTriangles = 0;

        for (var index = 0; index < mesh.Indices.Count; index += 3)
        {
            var a = mesh.Vertices[(int)mesh.Indices[index]];
            var b = mesh.Vertices[(int)mesh.Indices[index + 1]];
            var c = mesh.Vertices[(int)mesh.Indices[index + 2]];
            var pa = new Vector3(a.X, a.Y, a.Z);
            var pb = new Vector3(b.X, b.Y, b.Z);
            var pc = new Vector3(c.X, c.Y, c.Z);
            var normal = Vector3.Cross(pb - pa, pc - pa);
            if (normal.LengthSquared() < 0.000001f)
            {
                continue;
            }

            var centerDirection = Vector3.Normalize((pa + pb + pc) / 3);
            Assert.True(
                Vector3.Dot(Vector3.Normalize(normal), centerDirection) > 0,
                $"Triangle at index {index} should face outward for '{primitive}'.");
            checkedTriangles++;
        }

        Assert.True(checkedTriangles > 0);
    }

    [Theory]
    [InlineData("cube")]
    [InlineData("plane")]
    [InlineData("sphere")]
    [InlineData("surface")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    public void BuildMeshesCreatesPrimitiveTriangleWindingThatMatchesNormals(string primitive)
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "entity-1",
            "Primitive",
            "mesh",
            $"rekall.geometry.{primitive}",
            0,
            0,
            0,
            1,
            Variant: primitive == "surface" ? "rekall.planet.surface" : $"rekall.geometry.{primitive}"));

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));
        var checkedTriangles = 0;

        for (var index = 0; index < mesh.Indices.Count; index += 3)
        {
            var a = mesh.Vertices[(int)mesh.Indices[index]];
            var b = mesh.Vertices[(int)mesh.Indices[index + 1]];
            var c = mesh.Vertices[(int)mesh.Indices[index + 2]];
            var pa = new Vector3(a.X, a.Y, a.Z);
            var pb = new Vector3(b.X, b.Y, b.Z);
            var pc = new Vector3(c.X, c.Y, c.Z);
            var faceNormal = Vector3.Cross(pb - pa, pc - pa);
            if (faceNormal.LengthSquared() < 0.000001f)
            {
                continue;
            }

            var vertexNormal = new Vector3(
                a.NormalX + b.NormalX + c.NormalX,
                a.NormalY + b.NormalY + c.NormalY,
                a.NormalZ + b.NormalZ + c.NormalZ);
            if (vertexNormal.LengthSquared() < 0.000001f)
            {
                continue;
            }

            Assert.True(
                Vector3.Dot(Vector3.Normalize(faceNormal), Vector3.Normalize(vertexNormal)) > 0,
                $"Triangle at index {index} should match vertex normal winding for '{primitive}'.");
            checkedTriangles++;
        }

        Assert.True(checkedTriangles > 0);
    }

    [Fact]
    public void BuildMeshesBindsResolvedTextureAssetToGeneratedPlanetSurface()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "planet-1",
            "Gaia",
            "mesh",
            "rekall.planet.surface",
            0,
            0,
            0,
            1,
            Variant: "rekall.planet.surface",
            MaterialColor: "#ffffff",
            TextureAssetId: "asset_earth"));
        var assets = CreateAssetsWithTexture("asset_earth", 2, 1, [20, 40, 80, 255, 120, 140, 180, 255]);

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets));

        Assert.NotNull(mesh.BaseColorTexture);
        Assert.Equal("asset_earth", mesh.BaseColorTexture.Id);
        Assert.Equal(2, mesh.BaseColorTexture.Width);
        Assert.Equal(1, mesh.BaseColorTexture.Height);
        Assert.Equal([20, 40, 80, 255, 120, 140, 180, 255], mesh.BaseColorTexture.Rgba);
        Assert.Equal(RekallAgeVulkanSceneFilter.Linear, mesh.BaseColorTexture.Sampler.MinFilter);
    }

    [Fact]
    public void BuildMeshesBindsEmissiveTextureAndFactorsToGeneratedPrimitive()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "lamp-1",
            "Lamp",
            "mesh",
            "rekall.geometry.sphere",
            0,
            0,
            0,
            1,
            Variant: "rekall.geometry.sphere",
            MaterialColor: "#202020",
            EmissiveColor: "#ff8000",
            EmissiveStrength: 3.5,
            EmissiveTextureAssetId: "asset_glow"));
        var assets = CreateAssetsWithTexture("asset_glow", 1, 1, [255, 128, 0, 255]);

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets));

        Assert.NotNull(mesh.EmissiveTexture);
        Assert.Equal("asset_glow", mesh.EmissiveTexture.Id);
        Assert.Equal(new Vector4(1f, 128 / 255f, 0f, 3.5f), mesh.EmissiveFactor);
    }

    [Fact]
    public void BuildMeshesBindsGpuReadyTexturePayloadToGeneratedPlanetSurface()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "planet-1",
            "Gaia",
            "mesh",
            "rekall.planet.surface",
            0,
            0,
            0,
            1,
            Variant: "rekall.planet.surface",
            MaterialColor: "#ffffff",
            TextureAssetId: "asset_earth"));
        var gpuTexture = new RekallAgeRuntimeTextureAsset(
            "asset_earth",
            "ktx2",
            8,
            8,
            1,
            "VK_FORMAT_BC7_UNORM_BLOCK",
            null,
            true,
            [new RekallAgeRuntimeTextureMipLevel(0, 8, 8, Enumerable.Range(0, 64).Select(value => (byte)value).ToArray())]);
        var assets = new RekallAgeRuntimeViewportAssetSet(
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal),
            Array.Empty<RekallAgeRuntimeViewportAssetIssue>())
        {
            Textures = new Dictionary<string, RekallAgeRuntimeTextureAsset>(StringComparer.Ordinal)
            {
                ["asset_earth"] = gpuTexture
            }
        };

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets));

        Assert.NotNull(mesh.BaseColorTexture);
        Assert.Empty(mesh.BaseColorTexture.Rgba);
        Assert.Same(gpuTexture, mesh.BaseColorTexture.RuntimeTexture);
        Assert.Equal("asset_earth", mesh.BaseColorTexture.Id);
    }

    [Fact]
    public void BuildMeshesBindsResolvedTextureAssetToAuthoredGeometryMesh()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "entity-1",
            "Textured Triangle",
            "mesh",
            "rekall.geometry.mesh",
            0,
            0,
            0,
            1,
            Variant: "rekall.geometry.mesh",
            TextureAssetId: "asset_paint",
            GeometryMesh: new RekallAgeRuntimeViewportGeometryMesh(
                [
                    new RekallAgeRuntimeViewportGeometryVertex(0, 0, 0, U: 0, V: 1),
                    new RekallAgeRuntimeViewportGeometryVertex(1, 0, 0, U: 1, V: 1),
                    new RekallAgeRuntimeViewportGeometryVertex(0, 1, 0, U: 0, V: 0)
                ],
                [0, 1, 2])));
        var assets = CreateAssetsWithTexture("asset_paint", 1, 1, [255, 128, 64, 255]);

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets));

        Assert.NotNull(mesh.BaseColorTexture);
        Assert.Equal("asset_paint", mesh.BaseColorTexture.Id);
    }

    [Fact]
    public void BuildMeshesSkipsUnsupportedMeshAssets()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "entity-1",
            "Imported Mesh",
            "mesh",
            "robot.glb",
            0,
            0,
            0,
            1));

        Assert.Empty(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));
    }

    [Fact]
    public void BuildMeshesCreatesModelAssetMeshDataWhenResolvedAssetExists()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "entity-1",
            "Imported Mesh",
            "mesh",
            "asset_station",
            0,
            0,
            0,
            1));
        var assetMesh = new RekallAgeVulkanSceneMesh(
            "asset_station",
            "Station Asset Chunk",
            "glb",
            [
                new RekallAgeVulkanSceneVertex(0, 0, 0, 0, 1, 0, 0.2f, 0.7f, 1, 1, 0, 0),
                new RekallAgeVulkanSceneVertex(1, 0, 0, 0, 1, 0, 0.2f, 0.7f, 1, 1, 1, 0),
                new RekallAgeVulkanSceneVertex(0, 1, 0, 0, 1, 0, 0.2f, 0.7f, 1, 1, 0, 1)
            ],
            [0, 1, 2]);
        var assets = new RekallAgeRuntimeViewportAssetSet(
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal)
            {
                ["asset_station"] = [assetMesh]
            },
            Array.Empty<RekallAgeRuntimeViewportAssetIssue>());

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets));

        Assert.Equal("entity-1", mesh.EntityId);
        Assert.Equal("Imported Mesh", mesh.EntityName);
        Assert.Equal("glb", mesh.Primitive);
        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Equal([0, 1, 2], mesh.Indices);
    }

    [Fact]
    public void BuildMeshesCreatesAuthoredGeometryMeshData()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "entity-1",
            "Authored Triangle",
            "mesh",
            "rekall.geometry.mesh",
            0,
            0,
            0,
            1,
            Variant: "rekall.geometry.mesh",
            MaterialColor: "#ff6633",
            GeometryMesh: new RekallAgeRuntimeViewportGeometryMesh(
                [
                    new RekallAgeRuntimeViewportGeometryVertex(0, 0, 0),
                    new RekallAgeRuntimeViewportGeometryVertex(1, 0, 0, NormalX: 0, NormalY: 0, NormalZ: 1),
                    new RekallAgeRuntimeViewportGeometryVertex(0, 1, 0, R: 0, G: 1, B: 0, A: 0.75)
                ],
                [0, 1, 2])));

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame));

        Assert.Equal("mesh", mesh.Primitive);
        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Equal([0, 1, 2], mesh.Indices);
        Assert.Equal(1, mesh.Vertices[0].R);
        Assert.InRange(mesh.Vertices[0].G, 0.39f, 0.41f);
        Assert.InRange(mesh.Vertices[0].B, 0.19f, 0.21f);
        Assert.Equal(1, mesh.Vertices[1].NormalZ);
        Assert.Equal(0, mesh.Vertices[2].R);
        Assert.Equal(1, mesh.Vertices[2].G);
        Assert.Equal(0.75f, mesh.Vertices[2].A);
    }

    private static RekallAgeRuntimeViewportFrame CreateFrame(params RekallAgeRuntimeViewportRenderable[] renderables)
    {
        return new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            64,
            64,
            null,
            [],
            renderables,
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []); 
    }

    private static RekallAgeRuntimeViewportAssetSet CreateAssetsWithTexture(
        string assetId,
        int width,
        int height,
        byte[] rgba)
    {
        return new RekallAgeRuntimeViewportAssetSet(
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal)
            {
                [assetId] = new(width, height, rgba)
            },
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal),
            Array.Empty<RekallAgeRuntimeViewportAssetIssue>());
    }
}
