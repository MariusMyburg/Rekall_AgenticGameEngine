using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class GlbMeshLoaderTests
{
    [Fact]
    public async Task LoadAsyncCreatesVulkanMeshesFromBinaryGlbTriangles()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "triangle.glb");
        await File.WriteAllBytesAsync(path, GlbTestMeshFactory.CreateTriangleGlb(), CancellationToken.None);

        var meshes = await new RekallAgeGlbMeshLoader().LoadAsync("asset_triangle", path, CancellationToken.None);

        var mesh = Assert.Single(meshes);
        Assert.Equal("asset_triangle", mesh.EntityId);
        Assert.Equal("glb", mesh.Primitive);
        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Equal([0, 1, 2], mesh.Indices);
        Assert.All(mesh.Vertices, vertex =>
        {
            Assert.InRange(vertex.G, 0.69f, 0.71f);
            Assert.Equal(1, vertex.A);
        });
    }

    [Fact]
    public async Task LoadAsyncPreservesEmbeddedBaseColorTextureAndSampler()
    {
        var root = TestPaths.CreateTempDirectory();
        var texturePath = Path.Combine(root, "paint.png");
        await RekallAgePngWriter.WriteRgbaAsync(texturePath, 1, 1, [230, 40, 20, 255], CancellationToken.None);
        var path = Path.Combine(root, "textured-triangle.glb");
        await File.WriteAllBytesAsync(
            path,
            GlbTestMeshFactory.CreateTexturedTriangleGlb(await File.ReadAllBytesAsync(texturePath)),
            CancellationToken.None);

        var meshes = await new RekallAgeGlbMeshLoader().LoadAsync("asset_textured", path, CancellationToken.None);

        var mesh = Assert.Single(meshes);
        Assert.NotNull(mesh.BaseColorTexture);
        Assert.Equal(1, mesh.BaseColorTexture.Width);
        Assert.Equal(1, mesh.BaseColorTexture.Height);
        Assert.Equal([230, 40, 20, 255], mesh.BaseColorTexture.Rgba);
        Assert.Equal(RekallAgeVulkanSceneFilter.Nearest, mesh.BaseColorTexture.Sampler.MinFilter);
        Assert.Equal(RekallAgeVulkanSceneFilter.Nearest, mesh.BaseColorTexture.Sampler.MagFilter);
        Assert.Equal(RekallAgeVulkanSceneWrapMode.ClampToEdge, mesh.BaseColorTexture.Sampler.WrapS);
        Assert.Equal(RekallAgeVulkanSceneWrapMode.MirroredRepeat, mesh.BaseColorTexture.Sampler.WrapT);
        Assert.All(mesh.Vertices, vertex =>
        {
            Assert.Equal(1, vertex.R);
            Assert.Equal(1, vertex.G);
            Assert.Equal(1, vertex.B);
            Assert.Equal(1, vertex.A);
            Assert.Equal(0, vertex.U);
            Assert.Equal(1, vertex.V);
        });
    }

    [Fact]
    public async Task LoadAsyncPreservesPbrMaterialTexturesAndFactors()
    {
        var root = TestPaths.CreateTempDirectory();
        var baseColorPath = Path.Combine(root, "base.png");
        var metallicRoughnessPath = Path.Combine(root, "metalrough.png");
        var normalPath = Path.Combine(root, "normal.png");
        await RekallAgePngWriter.WriteRgbaAsync(baseColorPath, 1, 1, [180, 120, 80, 255], CancellationToken.None);
        await RekallAgePngWriter.WriteRgbaAsync(metallicRoughnessPath, 1, 1, [0, 64, 220, 255], CancellationToken.None);
        await RekallAgePngWriter.WriteRgbaAsync(normalPath, 1, 1, [128, 128, 255, 255], CancellationToken.None);
        var path = Path.Combine(root, "pbr-triangle.glb");
        await File.WriteAllBytesAsync(
            path,
            GlbTestMeshFactory.CreatePbrTexturedTriangleGlb(
                await File.ReadAllBytesAsync(baseColorPath),
                await File.ReadAllBytesAsync(metallicRoughnessPath),
                await File.ReadAllBytesAsync(normalPath)),
            CancellationToken.None);

        var meshes = await new RekallAgeGlbMeshLoader().LoadAsync("asset_pbr", path, CancellationToken.None);

        var mesh = Assert.Single(meshes);
        Assert.NotNull(mesh.BaseColorTexture);
        Assert.NotNull(mesh.MetallicRoughnessTexture);
        Assert.NotNull(mesh.NormalTexture);
        Assert.Equal("asset_pbr/texture/0", mesh.BaseColorTexture.Id);
        Assert.Equal("asset_pbr/texture/1", mesh.MetallicRoughnessTexture.Id);
        Assert.Equal("asset_pbr/texture/2", mesh.NormalTexture.Id);
        Assert.Equal([0, 64, 220, 255], mesh.MetallicRoughnessTexture.Rgba);
        Assert.Equal([128, 128, 255, 255], mesh.NormalTexture.Rgba);
        Assert.InRange(mesh.MetallicFactor, 0.79f, 0.81f);
        Assert.InRange(mesh.RoughnessFactor, 0.34f, 0.36f);
        Assert.InRange(mesh.NormalScale, 0.69f, 0.71f);
    }

    [Fact]
    public async Task LoadAsyncPreservesSharedIndexedVertices()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "quad.glb");
        await File.WriteAllBytesAsync(path, GlbTestMeshFactory.CreateIndexedQuadGlb(), CancellationToken.None);

        var meshes = await new RekallAgeGlbMeshLoader().LoadAsync("asset_quad", path, CancellationToken.None);

        var mesh = Assert.Single(meshes);
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal([0, 1, 2, 0, 2, 3], mesh.Indices);
    }
}
