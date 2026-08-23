using System.Text;
using Rekall.Age.Project;

namespace Rekall.Age.Tests.Project;

public sealed class GameContentTests
{
    [Fact]
    public async Task MemoryContentReturnsExactBytesThroughNormalizedLogicalPaths()
    {
        var expected = new byte[] { 0, 1, 2, 127, 255 };
        IRekallAgeGameContent content = new RekallAgeMemoryGameContent(new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["Scenes/Main.age.scene.json"] = expected
        });

        var loaded = await content.ReadAsync("Scenes\\Main.age.scene.json", 1024, CancellationToken.None);

        Assert.Equal("Scenes/Main.age.scene.json", loaded.LogicalPath);
        Assert.Equal(expected, loaded.Bytes.ToArray());
    }

    [Fact]
    public async Task FileContentReadsOnlyWithinItsRoot()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneDirectory = Path.Combine(root, "Scenes");
        Directory.CreateDirectory(sceneDirectory);
        var expected = Encoding.UTF8.GetBytes("ordinary AGE content");
        await File.WriteAllBytesAsync(Path.Combine(sceneDirectory, "Main.age.scene.json"), expected);
        IRekallAgeGameContent content = new RekallAgeFileGameContent(root);

        var loaded = await content.ReadAsync("Scenes/Main.age.scene.json", 1024, CancellationToken.None);

        Assert.Equal(expected, loaded.Bytes.ToArray());
        await Assert.ThrowsAsync<ArgumentException>(
            () => content.ReadAsync("../outside.txt", 1024, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => content.ReadAsync("C:/outside.txt", 1024, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ContentReadsAreBoundedAndMissingEntriesRemainExplicit()
    {
        IRekallAgeGameContent content = new RekallAgeMemoryGameContent(new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["Assets/large.bin"] = new byte[5]
        });

        var tooLarge = await Assert.ThrowsAsync<RekallAgeGameContentException>(
            () => content.ReadAsync("Assets/large.bin", 4, CancellationToken.None).AsTask());
        Assert.Equal("REKALL_GAME_CONTENT_TOO_LARGE", tooLarge.Code);

        var missing = await Assert.ThrowsAsync<RekallAgeGameContentException>(
            () => content.ReadAsync("Assets/missing.bin", 4, CancellationToken.None).AsTask());
        Assert.Equal("REKALL_GAME_CONTENT_NOT_FOUND", missing.Code);
    }

    [Fact]
    public async Task ContentReadsHonorCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        IRekallAgeGameContent content = new RekallAgeMemoryGameContent(new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["Scenes/Main.age.scene.json"] = new byte[] { 1 }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => content.ReadAsync("Scenes/Main.age.scene.json", 1024, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task MemoryContentOwnsItsInputAndEachReadResult()
    {
        var source = new byte[] { 1, 2, 3 };
        IRekallAgeGameContent content = new RekallAgeMemoryGameContent(new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["Assets/data.bin"] = source
        });
        source[0] = 99;

        var first = await content.ReadAsync("Assets/data.bin", 3, CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, first.Bytes.ToArray());
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(first.Bytes, out var exposed));
        exposed.Array![exposed.Offset] = 88;

        var second = await content.ReadAsync("Assets/data.bin", 3, CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, second.Bytes.ToArray());
    }
}
