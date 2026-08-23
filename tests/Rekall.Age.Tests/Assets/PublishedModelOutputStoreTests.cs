using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    [Fact]
    public async Task WriteStagedAsyncRejectsIncompleteTriangleMetadata()
    {
        var store = new RekallAgePublishedModelOutputStore();
        var snapshot = CompiledBox() with
        {
            Triangles =
            [
                CompiledBox().Triangles[0] with { SourceCornerIds = [1, 2] }
            ]
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), "hero-model", snapshot, default).AsTask());
    }

    [Fact]
    public async Task WriteStagedAsyncRejectsUnalignedSurfaceRanges()
    {
        var store = new RekallAgePublishedModelOutputStore();
        var snapshot = CompiledBox() with
        {
            Surfaces = [CompiledBox().Surfaces[0] with { FirstIndex = 1, IndexCount = 3 }]
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), "hero-model", snapshot, default).AsTask());
    }

    [Fact]
    public async Task WriteStagedAsyncRejectsNonSequentialTriangleIndices()
    {
        var store = new RekallAgePublishedModelOutputStore();
        var baseline = CompiledBox();
        var snapshot = baseline with
        {
            Triangles = baseline.Triangles.Select((triangle, index) =>
                index == 0 ? triangle with { TriangleIndex = 4 } : triangle).ToArray()
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), "hero-model", snapshot, default).AsTask());
    }

    [Fact]
    public async Task WriteStagedAsyncRejectsNonSequentialSurfaceIndices()
    {
        var store = new RekallAgePublishedModelOutputStore();
        var baseline = CompiledBox();
        var snapshot = baseline with
        {
            Surfaces = [baseline.Surfaces[0] with { SurfaceIndex = 2 }]
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), "hero-model", snapshot, default).AsTask());
    }

    [Fact]
    public async Task WriteStagedAsyncRejectsTriangleSourcePointIdsThatDisagreeWithItsVertices()
    {
        var store = new RekallAgePublishedModelOutputStore();
        var baseline = CompiledBox();
        var snapshot = baseline with
        {
            Triangles = baseline.Triangles.Select((triangle, index) =>
                index == 0 ? triangle with { SourcePointIds = [1, 2, 3] } : triangle).ToArray()
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), "hero-model", snapshot, default).AsTask());
    }

    [Fact]
    public async Task WriteStagedAsyncRejectsTriangleSourceCornerIdsThatDisagreeWithItsVertices()
    {
        var store = new RekallAgePublishedModelOutputStore();
        var baseline = CompiledBox();
        var snapshot = baseline with
        {
            Triangles = baseline.Triangles.Select((triangle, index) =>
                index == 0 ? triangle with { SourceCornerIds = [1, 2, 3] } : triangle).ToArray()
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), "hero-model", snapshot, default).AsTask());
    }

    [Fact]
    public async Task WriteStagedAsyncRejectsTriangleSourceFaceAbsentFromItsSurface()
    {
        var store = new RekallAgePublishedModelOutputStore();
        var baseline = CompiledBox();
        var snapshot = baseline with
        {
            Triangles = baseline.Triangles.Select((triangle, index) =>
                index == 0 ? triangle with { SourceFaceId = 999 } : triangle).ToArray()
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(TestPaths.CreateTempDirectory(), "hero-model", snapshot, default).AsTask());
    }

    [Fact]
    public async Task WriteStagedAsyncRejectsStagingDirectoryReparsePoint()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        var stagingPath = Path.Combine(root, "Assets", "Models", ".staging");
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
        if (!TryCreateDirectoryLink(stagingPath, outside))
        {
            return;
        }

        var store = new RekallAgePublishedModelOutputStore();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.WriteStagedAsync(root, "hero-model", CompiledBox(), default).AsTask());

        Assert.Empty(Directory.EnumerateFiles(outside, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CommitStagedAsyncRejectsCompiledDirectoryReparsePoint()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        var store = new RekallAgePublishedModelOutputStore();
        var staged = await store.WriteStagedAsync(root, "hero-model", CompiledBox(), default);
        var compiledPath = Path.Combine(root, "Assets", "Models", "Compiled");
        if (!TryCreateDirectoryLink(compiledPath, outside))
        {
            return;
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.CommitStagedAsync(root, staged, default).AsTask());

        Assert.Empty(Directory.EnumerateFiles(outside, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CommitStagedAsyncRejectsForgedMalformedOutputAndRetainsPublishedBytes()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgePublishedModelOutputStore();
        var initial = await store.WriteStagedAsync(root, "hero-model", CompiledBox(), default);
        await store.CommitStagedAsync(root, initial, default);
        var finalPath = store.GetFinalPath(root, "hero-model");
        var priorBytes = await File.ReadAllBytesAsync(finalPath);
        var priorHash = await store.HashAsync(root, "hero-model", default);

        var staged = await store.WriteStagedAsync(root, "hero-model", CompiledBox() with { SourceLogicalRevision = 8 }, default);
        var malformedSnapshot = staged.Snapshot with
        {
            Triangles = staged.Snapshot.Triangles.Take(staged.Snapshot.Triangles.Count - 1).ToArray()
        };
        var malformedBytes = CanonicalBytes(malformedSnapshot);
        await File.WriteAllBytesAsync(staged.Path, malformedBytes);
        var forged = staged with
        {
            ContentHash = Convert.ToHexString(SHA256.HashData(malformedBytes)).ToLowerInvariant(),
            Snapshot = malformedSnapshot
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.CommitStagedAsync(root, forged, default).AsTask());

        Assert.Equal(priorBytes, await File.ReadAllBytesAsync(finalPath));
        Assert.Equal(priorHash, await store.HashAsync(root, "hero-model", default));
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
            .Select(triangleIndex =>
            {
                var firstIndex = triangleIndex * 3;
                var triangleVertices = indices
                    .Skip(firstIndex)
                    .Take(3)
                    .Select(vertexIndex => vertices[checked((int)vertexIndex)])
                    .ToArray();
                return new RekallAgeCompiledMeshTriangle(
                    triangleIndex,
                    (ulong)(triangleIndex + 1),
                    triangleVertices.Select(vertex => vertex.SourceCornerId).ToArray(),
                    triangleVertices.Select(vertex => vertex.SourcePointId).ToArray(),
                    0);
            })
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

    private static byte[] CanonicalBytes(RekallAgeCompiledMeshSnapshot snapshot) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = 128
        }) + "\n");

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                return true;
            }

            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(linkPath);
            startInfo.ArgumentList.Add(targetPath);
            using var process = Process.Start(startInfo)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
