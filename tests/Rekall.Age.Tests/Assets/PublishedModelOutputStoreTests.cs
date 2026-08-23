using Rekall.Age.AssetPipeline;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Assets;

public sealed class PublishedModelOutputStoreTests
{
    [Fact]
    public async Task StagingWritesCanonicalEquivalentBytesWithoutReplacingThePublishedOutput()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgePublishedModelOutputStore();
        var snapshot = CompiledBox();

        var first = await store.WriteStagedAsync(root, "hero-model", snapshot, default);
        var second = await store.WriteStagedAsync(root, "hero-model", snapshot, default);

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Matches("^[0-9a-f]{64}$", first.ContentHash);
        Assert.Equal(await File.ReadAllBytesAsync(first.Path), await File.ReadAllBytesAsync(second.Path));
        Assert.Equal("Assets/Models/Compiled/hero-model.age.compiled-mesh.json", first.RelativeFinalPath);
        Assert.StartsWith(
            Path.Combine(root, "Assets", "Models", ".staging"),
            first.Path,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(store.GetFinalPath(root, "hero-model")));

        await store.CommitStagedAsync(root, first, default);

        var finalPath = store.GetFinalPath(root, "hero-model");
        var publishedBytes = await File.ReadAllBytesAsync(finalPath);
        Assert.Equal(await File.ReadAllBytesAsync(first.Path), publishedBytes);
        Assert.Equal(first.ContentHash, await store.HashAsync(root, "hero-model", default));
        Assert.Equal(snapshot.SourceAssetId, (await store.LoadAsync(root, "hero-model", default)).SourceAssetId);

        var changed = snapshot with { SourceLogicalRevision = snapshot.SourceLogicalRevision + 1 };
        var replacement = await store.WriteStagedAsync(root, "hero-model", changed, default);
        Assert.Equal(publishedBytes, await File.ReadAllBytesAsync(finalPath));

        await store.CommitStagedAsync(root, replacement, default);

        Assert.NotEqual(publishedBytes, await File.ReadAllBytesAsync(finalPath));
    }

    [Fact]
    public async Task DeleteStagedAsyncRemovesOnlyTheValidatedStagedOutput()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgePublishedModelOutputStore();
        var staged = await store.WriteStagedAsync(root, "hero-model", CompiledBox(), default);

        await store.DeleteStagedAsync(root, staged, default);

        Assert.False(File.Exists(staged.Path));
        Assert.False(File.Exists(store.GetFinalPath(root, "hero-model")));
    }

    [Theory]
    [InlineData("../hero-model")]
    [InlineData("hero/model")]
    [InlineData("C:\\hero-model")]
    public async Task WriteStagedAsyncRejectsUnsafeAssetIds(string assetId)
    {
        var store = new RekallAgePublishedModelOutputStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), assetId, CompiledBox(), default).AsTask());
    }

    [Fact]
    public async Task WriteStagedAsyncRejectsNonfiniteVertexData()
    {
        var store = new RekallAgePublishedModelOutputStore();
        var snapshot = CompiledBox() with
        {
            Vertices =
            [
                new(1, 1, new(double.NaN, 0, 0), new(0, 0, 1), new(1, 0, 0, 1), new(0, 0), new(1, 1, 1, 1))
            ]
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), "hero-model", snapshot, default).AsTask());
    }

    [Fact]
    public async Task WriteStagedAsyncRejectsIndicesOutsideTheVertexBuffer()
    {
        var store = new RekallAgePublishedModelOutputStore();
        var snapshot = CompiledBox() with { Indices = [0, 1, 99] };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), "hero-model", snapshot, default).AsTask());
    }

    private static RekallAgeCompiledMeshSnapshot CompiledBox()
    {
        var positions = new[]
        {
            new RekallAgeGeometryVector3(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
            new RekallAgeGeometryVector3(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)
        };
        var vertices = positions.Select((position, index) => new RekallAgeCompiledMeshVertex(
            (ulong)(index + 1),
            (ulong)(index + 1),
            position,
            new(0, 0, 1),
            new(1, 0, 0, 1),
            new(0, 0),
            new(1, 1, 1, 1))).ToArray();
        uint[] indices =
        [
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6,
            0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5
        ];
        var triangles = Enumerable.Range(0, indices.Length / 3)
            .Select(index => new RekallAgeCompiledMeshTriangle(
                index,
                (ulong)(index + 1),
                [1, 2, 3],
                [1, 2, 3],
                0))
            .ToArray();

        return new RekallAgeCompiledMeshSnapshot(
            "hero-mesh",
            7,
            vertices,
            indices,
            triangles,
            [new(0, 0, null, 0, indices.Length, Enumerable.Range(1, triangles.Length).Select(value => (ulong)value).ToArray())],
            new(new(-1, -1, -1), new(1, 1, 1)),
            HasVertexColors: true);
    }
}
