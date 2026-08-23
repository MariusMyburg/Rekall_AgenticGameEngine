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
        Assert.Equal(
            "4ffb9dfa894034b93b9b3eba989f3ff628c667b9eecfbbc3e9144b036e602950",
            snapshot.Revision);
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

    [Fact]
    public async Task ConditionalPublisherCreatesOnlyFromExplicitMissingRevision()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");

        var revision = await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            path,
            "first",
            maximumBytes: 1024,
            RekallAgeDocumentRevision.Missing,
            CancellationToken.None);

        Assert.Equal(RekallAgeDocumentRevision.Compute(Encoding.UTF8.GetBytes("first")), revision);
        Assert.Equal("first", await File.ReadAllTextAsync(path));
        Assert.Empty(ControlSiblings(path));
    }

    [Fact]
    public async Task ConditionalPublisherRejectsStaleRevisionWithoutChangingDestination()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        await File.WriteAllTextAsync(path, "original");
        var expected = RekallAgeDocumentRevision.Compute(Encoding.UTF8.GetBytes("original"));
        await File.WriteAllTextAsync(path, "intervening");
        var current = RekallAgeDocumentRevision.Compute(Encoding.UTF8.GetBytes("intervening"));

        var error = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(
            () => RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
                path,
                "replacement",
                maximumBytes: 1024,
                expected,
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", error.Code);
        Assert.Equal(expected, error.ExpectedRevision);
        Assert.Equal(current, error.CurrentRevision);
        Assert.Equal("intervening", await File.ReadAllTextAsync(path));
        Assert.Empty(ControlSiblings(path));
    }

    [Fact]
    public async Task ConditionalPublisherAllowsExactlyOneWriterForOneRevision()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        await File.WriteAllTextAsync(path, "base");
        var revision = RekallAgeDocumentRevision.Compute(Encoding.UTF8.GetBytes("base"));

        var writes = new[] { "alpha", "beta" }.Select(value => Task.Run(async () =>
        {
            try
            {
                return (Succeeded: true, Revision: await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
                    path,
                    value,
                    maximumBytes: 1024,
                    revision,
                    CancellationToken.None), Error: (RekallAgeDocumentRevisionException?)null);
            }
            catch (RekallAgeDocumentRevisionException error)
            {
                return (Succeeded: false, Revision: string.Empty, Error: error);
            }
        })).ToArray();

        var results = await Task.WhenAll(writes);

        Assert.Single(results, result => result.Succeeded);
        var rejected = Assert.Single(results, result => !result.Succeeded);
        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", rejected.Error!.Code);
        Assert.Contains(await File.ReadAllTextAsync(path), new[] { "alpha", "beta" });
        Assert.Empty(ControlSiblings(path));
    }

    [Fact]
    public async Task ConditionalPublisherHonorsCancellationWhileDocumentIsBusy()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        await File.WriteAllTextAsync(path, "base");
        var lockPath = RekallAgeAtomicFile.GetLockPath(path);
        await using var held = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.Asynchronous);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
                path,
                "replacement",
                maximumBytes: 1024,
                RekallAgeDocumentRevision.Compute(Encoding.UTF8.GetBytes("base")),
                cancellation.Token).AsTask());

        Assert.Equal("base", await File.ReadAllTextAsync(path));
        Assert.Empty(TemporarySiblings(path));
    }

    [Fact]
    public async Task ConditionalPublisherFailsWithStableCodeWhenDocumentRemainsBusy()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        await File.WriteAllTextAsync(path, "base");
        await using var held = new FileStream(
            RekallAgeAtomicFile.GetLockPath(path),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.Asynchronous);

        var error = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(
            () => RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
                path,
                "replacement",
                maximumBytes: 1024,
                RekallAgeDocumentRevision.Compute(Encoding.UTF8.GetBytes("base")),
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_BUSY", error.Code);
        Assert.Equal("base", await File.ReadAllTextAsync(path));
        Assert.Empty(TemporarySiblings(path));
    }

    [Fact]
    public async Task ConditionalReplacementRetainsExactPreviousBytes()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        var previousPath = Path.Combine(root, ".rekall", "recovery", "document.json.previous");
        var original = Encoding.UTF8.GetBytes("{\"version\":1}");
        await File.WriteAllBytesAsync(path, original);

        await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            path,
            "{\"version\":2}",
            maximumBytes: 1024,
            RekallAgeDocumentRevision.Compute(original),
            previousPath,
            CancellationToken.None);

        Assert.Equal(original, await File.ReadAllBytesAsync(previousPath));
        Assert.Equal("{\"version\":2}", await File.ReadAllTextAsync(path));
        await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            path,
            "{\"version\":3}",
            maximumBytes: 1024,
            RekallAgeDocumentRevision.Compute(Encoding.UTF8.GetBytes("{\"version\":2}")),
            previousPath,
            CancellationToken.None);
        Assert.Equal("{\"version\":2}", await File.ReadAllTextAsync(previousPath));
        Assert.Equal("{\"version\":3}", await File.ReadAllTextAsync(path));
        Assert.Empty(ControlSiblings(path));
    }

    [Fact]
    public async Task StaleConditionalReplacementPreservesExistingPreviousVersion()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        var previousPath = Path.Combine(root, ".rekall", "recovery", "document.json.previous");
        Directory.CreateDirectory(Path.GetDirectoryName(previousPath)!);
        await File.WriteAllTextAsync(path, "current");
        await File.WriteAllTextAsync(previousPath, "known-good");

        await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(
            () => RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
                path,
                "stale",
                maximumBytes: 1024,
                RekallAgeDocumentRevision.Compute(Encoding.UTF8.GetBytes("other")),
                previousPath,
                CancellationToken.None).AsTask());

        Assert.Equal("current", await File.ReadAllTextAsync(path));
        Assert.Equal("known-good", await File.ReadAllTextAsync(previousPath));
    }

    [Fact]
    public async Task ConditionalCreationDoesNotFabricatePreviousVersion()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "document.json");
        var previousPath = Path.Combine(root, ".rekall", "recovery", "document.json.previous");

        await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            path,
            "created",
            maximumBytes: 1024,
            RekallAgeDocumentRevision.Missing,
            previousPath,
            CancellationToken.None);

        Assert.False(File.Exists(previousPath));
        Assert.Equal("created", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ConditionalDeleteRemovesOnlyTheExactWrittenRevision()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "created.json");
        var bytes = Encoding.UTF8.GetBytes("created");
        await File.WriteAllBytesAsync(path, bytes);

        await RekallAgeAtomicFile.DeleteIfRevisionAsync(
            path,
            maximumBytes: 1024,
            RekallAgeDocumentRevision.Compute(bytes),
            default);

        Assert.False(File.Exists(path));
        Assert.Empty(ControlSiblings(path));
    }

    [Fact]
    public async Task ConditionalDeleteRejectsStaleRevisionAndRetainsConcurrentBytes()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "created.json");
        var written = Encoding.UTF8.GetBytes("written");
        var concurrent = Encoding.UTF8.GetBytes("concurrent");
        await File.WriteAllBytesAsync(path, concurrent);

        var error = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(() =>
            RekallAgeAtomicFile.DeleteIfRevisionAsync(
                path,
                maximumBytes: 1024,
                RekallAgeDocumentRevision.Compute(written),
                default).AsTask());

        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", error.Code);
        Assert.Equal(concurrent, await File.ReadAllBytesAsync(path));
        Assert.Empty(ControlSiblings(path));
    }

    private static IReadOnlyList<string> TemporarySiblings(string destination) =>
        Directory.GetFiles(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.tmp-*");

    private static IReadOnlyList<string> ControlSiblings(string destination) =>
        Directory.GetFiles(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.*");
}
