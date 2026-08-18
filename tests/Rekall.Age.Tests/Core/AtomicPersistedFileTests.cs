using System.Text;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Tests.Core;

public sealed class AtomicPersistedFileTests
{
    [Fact]
    public async Task BoundedSnapshotReadsOneExactImmutableByteSequence()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        var expected = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"name\":\"snapshot\"}");
        await File.WriteAllBytesAsync(path, expected);

        var snapshot = await RekallAgeBoundedFileSnapshot.ReadAsync(path, expected.Length, CancellationToken.None);
        await File.WriteAllTextAsync(path, "changed");

        Assert.Equal(Path.GetFullPath(path), snapshot.Path);
        Assert.Equal(expected, snapshot.Bytes);
    }

    [Fact]
    public async Task BoundedSnapshotRejectsInputBeforeAllocatingBeyondLimit()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "oversized.json");
        await File.WriteAllBytesAsync(path, new byte[17]);

        var error = await Assert.ThrowsAsync<RekallAgeBoundedFileSnapshotException>(
            () => RekallAgeBoundedFileSnapshot.ReadAsync(path, 16, CancellationToken.None).AsTask());

        Assert.Equal("REKALL_FILE_SNAPSHOT_TOO_LARGE", error.Code);
        Assert.Contains("17", error.Message, StringComparison.Ordinal);
        Assert.Contains("16", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AtomicPublisherReplacesExistingFileWithCompleteBomlessUtf8()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        await File.WriteAllTextAsync(path, "old");

        await RekallAgeAtomicFile.WriteAllTextAsync(
            path,
            "{\"name\":\"Möbius\"}\n",
            maximumBytes: 1024,
            CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal("{\"name\":\"Möbius\"}\n", Encoding.UTF8.GetString(bytes));
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Empty(TemporarySiblings(path));
    }

    [Fact]
    public async Task AtomicPublisherCancellationPreservesExistingDestinationAndLeavesNoTemporaryFile()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        await File.WriteAllTextAsync(path, "original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RekallAgeAtomicFile.WriteAllTextAsync(
                path,
                "replacement",
                maximumBytes: 1024,
                cancellation.Token).AsTask());

        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Empty(TemporarySiblings(path));
    }

    [Fact]
    public async Task AtomicPublisherCleansStagingFileWhenPublicationFails()
    {
        var root = TestPaths.CreateTempDirectory();
        var destinationDirectory = Path.Combine(root, "occupied.json");
        Directory.CreateDirectory(destinationDirectory);

        var error = await Record.ExceptionAsync(
            () => RekallAgeAtomicFile.WriteAllTextAsync(
                destinationDirectory,
                "replacement",
                maximumBytes: 1024,
                CancellationToken.None).AsTask());

        Assert.NotNull(error);
        Assert.True(Directory.Exists(destinationDirectory));
        Assert.Empty(TemporarySiblings(destinationDirectory));
    }

    private static IReadOnlyList<string> TemporarySiblings(string destination) =>
        Directory.GetFiles(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.tmp-*");
}
