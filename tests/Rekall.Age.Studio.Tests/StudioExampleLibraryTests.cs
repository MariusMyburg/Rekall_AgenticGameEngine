using System.IO;
using System.Text.Json;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioExampleLibraryTests
{
    [Fact]
    public void CatalogDiscoversValidManifestProjectsInDisplayNameOrderAndHonorsRootPrecedence()
    {
        var root = CreateTempDirectory();
        var installed = Path.Combine(root, "installed");
        var repository = Path.Combine(root, "repository");
        WriteProject(installed, "summit", "Summit Run", ["world", "physics2d"]);
        WriteProject(installed, "citadel", "Aetherfall Citadel", ["world", "rendering3d"]);
        WriteProject(repository, "summit", "Wrong duplicate", ["world"]);
        Directory.CreateDirectory(Path.Combine(installed, "not-a-project"));
        Directory.CreateDirectory(Path.Combine(installed, "broken"));
        File.WriteAllText(Path.Combine(installed, "broken", "rekall.project.json"), "{ no");
        Directory.CreateDirectory(Path.Combine(installed, "missing-name"));
        File.WriteAllText(Path.Combine(installed, "missing-name", "rekall.project.json"), "{\"schemaVersion\":1}");
        Directory.CreateDirectory(Path.Combine(installed, "non-string-name"));
        File.WriteAllText(Path.Combine(installed, "non-string-name", "rekall.project.json"), "{\"name\":42}");
        Directory.CreateDirectory(Path.Combine(installed, "empty-name"));
        File.WriteAllText(Path.Combine(installed, "empty-name", "rekall.project.json"), "{\"name\":\"  \"}");

        var result = new RekallAgeStudioExampleCatalog([installed, repository]).Discover();

        Assert.Equal(["Aetherfall Citadel", "Summit Run"], result.Examples.Select(example => example.DisplayName));
        var summit = Assert.Single(result.Examples, example => example.FolderName == "summit");
        Assert.Equal(Path.GetFullPath(Path.Combine(installed, "summit")), summit.SourceRoot);
        Assert.Equal(["world", "physics2d"], summit.Capabilities);
        Assert.Equal(
            ["broken", "empty-name", "missing-name", "non-string-name"],
            result.Issues.Select(issue => issue.FolderName).Order());
    }

    [Fact]
    public async Task LibraryCreatesAtomicWritableCopyWithoutTransientDevelopmentStateOrSourceMutation()
    {
        var root = CreateTempDirectory();
        var source = WriteProject(Path.Combine(root, "sources"), "summit", "Summit Run", ["world"]);
        Directory.CreateDirectory(Path.Combine(source, "Scenes"));
        await File.WriteAllTextAsync(Path.Combine(source, "Scenes", "Main.age.scene.json"), "{\"entities\":[]}");
        foreach (var transient in new[] { ".rekall", ".git", ".vs", "bin", "Builds", "Captures", "obj", "TestResults" })
        {
            var transientRoot = Path.Combine(source, "Modules", transient);
            Directory.CreateDirectory(transientRoot);
            await File.WriteAllTextAsync(Path.Combine(transientRoot, "ignored.bin"), transient);
        }

        var sourceManifestBefore = await File.ReadAllTextAsync(Path.Combine(source, "rekall.project.json"));
        var example = Assert.Single(new RekallAgeStudioExampleCatalog([Path.GetDirectoryName(source)!]).Discover().Examples);
        var destination = Path.Combine(root, "library", "summit");

        await new RekallAgeStudioExampleLibrary().CopyAsync(example, destination, CancellationToken.None);

        Assert.Equal(sourceManifestBefore, await File.ReadAllTextAsync(Path.Combine(source, "rekall.project.json")));
        Assert.True(File.Exists(Path.Combine(destination, "Scenes", "Main.age.scene.json")));
        Assert.All(new[] { ".rekall", ".git", ".vs", "bin", "Builds", "Captures", "obj", "TestResults" }, transient =>
            Assert.False(Directory.Exists(Path.Combine(destination, "Modules", transient))));
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(Path.GetDirectoryName(destination)!),
            path => Path.GetFileName(path).Contains("rekall-import", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibraryNeverOverwritesExistingDestinationAndSelectsFreshSuffix()
    {
        var root = CreateTempDirectory();
        var source = WriteProject(Path.Combine(root, "sources"), "pong", "Pong 3D", ["world"]);
        var example = Assert.Single(new RekallAgeStudioExampleCatalog([Path.GetDirectoryName(source)!]).Discover().Examples);
        var libraryRoot = Path.Combine(root, "library");
        var original = Path.Combine(libraryRoot, "pong");
        var second = Path.Combine(libraryRoot, "pong-2");
        Directory.CreateDirectory(original);
        Directory.CreateDirectory(second);
        await File.WriteAllTextAsync(Path.Combine(original, "keep.txt"), "mine");

        var fresh = RekallAgeStudioExampleLibrary.FindFreshDestination(libraryRoot, example.FolderName);
        var error = await Assert.ThrowsAsync<IOException>(async () =>
            await new RekallAgeStudioExampleLibrary().CopyAsync(example, original, CancellationToken.None));

        Assert.Equal(Path.Combine(libraryRoot, "pong-3"), fresh);
        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mine", await File.ReadAllTextAsync(Path.Combine(original, "keep.txt")));
    }

    [Fact]
    public void LibraryTreatsAnExistingFileAsAnOccupiedExampleDestination()
    {
        var root = CreateTempDirectory();
        var libraryRoot = Path.Combine(root, "library");
        Directory.CreateDirectory(libraryRoot);
        var collision = Path.Combine(libraryRoot, "pong");
        File.WriteAllText(collision, "user-owned");

        Assert.True(RekallAgeStudioExampleLibrary.IsOccupied(collision));
        Assert.Equal(
            Path.Combine(libraryRoot, "pong-2"),
            RekallAgeStudioExampleLibrary.FindFreshDestination(libraryRoot, "pong"));
        Assert.Equal("user-owned", File.ReadAllText(collision));
    }

    [Fact]
    public async Task ProjectTransitionExcludesConflictingWorkAndShutdownCancelsThenWaits()
    {
        var coordinator = new RekallAgeStudioProjectTransitionCoordinator();
        var transition = coordinator.TryBegin();

        Assert.NotNull(transition);
        Assert.Null(coordinator.TryBegin());
        var shutdown = coordinator.CancelAndWaitAsync().AsTask();
        Assert.True(transition.CancellationToken.IsCancellationRequested);
        Assert.False(shutdown.IsCompleted);

        transition.Dispose();
        await shutdown;
        using var nextTransition = coordinator.TryBegin();
        Assert.NotNull(nextTransition);
    }

    [Fact]
    public async Task LibraryRejectsDestinationInsidePackagedExample()
    {
        var root = CreateTempDirectory();
        var source = WriteProject(root, "source", "Source", ["world"]);
        var example = Assert.Single(new RekallAgeStudioExampleCatalog([root]).Discover().Examples);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new RekallAgeStudioExampleLibrary().CopyAsync(
                example,
                Path.Combine(source, "copy"),
                CancellationToken.None));

        Assert.Contains("inside", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string WriteProject(string parent, string folderName, string displayName, string[] capabilities)
    {
        var projectRoot = Path.Combine(parent, folderName);
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(
            Path.Combine(projectRoot, "rekall.project.json"),
            JsonSerializer.Serialize(new { name = displayName, schemaVersion = 1, capabilities }));
        return projectRoot;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "rekall-studio-example-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
