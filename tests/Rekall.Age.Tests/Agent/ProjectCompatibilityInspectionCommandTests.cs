using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using System.Diagnostics;

namespace Rekall.Age.Tests.Agent;

public sealed class ProjectCompatibilityInspectionCommandTests
{
    [Fact]
    public async Task CurrentProjectReturnsValidationAsNextAction()
    {
        var root = TestPaths.CreateTempDirectory();
        var scenes = Path.Combine(root, "Scenes");
        Directory.CreateDirectory(scenes);
        await File.WriteAllTextAsync(
            Path.Combine(root, "rekall.project.json"),
            """{"name":"Current","schemaVersion":1,"capabilities":[]}""");
        await File.WriteAllTextAsync(
            Path.Combine(scenes, "Main.age.scene.json"),
            """{"schemaVersion":1,"id":"current","name":"Main","capabilities":[],"entities":[]}""");

        var result = await ExecuteAsync(root);

        Assert.True(result.Value.IsCurrent);
        Assert.False(result.Value.CanMigrate);
        Assert.All(result.Value.Documents, item => Assert.Equal("current", item.Status));
        Assert.Contains(result.Value.NextActions, action => action.Tool == "rekall.validation.project");
    }

    [Fact]
    public async Task LegacyProjectInspectionIsDeterministicAndReadOnly()
    {
        var root = TestPaths.CreateTempDirectory();
        var manifestPath = Path.Combine(root, "rekall.project.json");
        var scenePath = Path.Combine(root, "Scenes", "Main.age.scene.json");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
        const string manifest = """{"name":"Legacy","capabilities":["world"]}""";
        const string scene = """{"id":"scene_legacy","name":"Main","capabilities":["world"],"entities":[]}""";
        await File.WriteAllTextAsync(manifestPath, manifest);
        await File.WriteAllTextAsync(scenePath, scene);

        var result = await ExecuteAsync(root);

        Assert.True(result.Ok);
        Assert.False(result.Value.IsCurrent);
        Assert.True(result.Value.CanMigrate);
        Assert.Empty(result.Value.Blockers);
        Assert.Equal(["project", "scene"], result.Value.Documents.Select(item => item.Kind));
        Assert.All(result.Value.Documents, item =>
        {
            Assert.Equal("legacy", item.Status);
            Assert.Equal(0, item.DetectedVersion);
            Assert.Equal(1, item.CurrentVersion);
            Assert.True(item.CanMigrate);
        });
        Assert.Contains(
            result.Value.NextActions,
            action => action.Tool == "rekall.compatibility.migrate_project");
        Assert.Equal(manifest, await File.ReadAllTextAsync(manifestPath));
        Assert.Equal(scene, await File.ReadAllTextAsync(scenePath));
    }

    [Fact]
    public async Task FutureAndMalformedDocumentsAreIsolatedAsBlockers()
    {
        var root = TestPaths.CreateTempDirectory();
        var scenes = Path.Combine(root, "Scenes");
        Directory.CreateDirectory(scenes);
        await File.WriteAllTextAsync(
            Path.Combine(root, "rekall.project.json"),
            """{"name":"Future","schemaVersion":2,"capabilities":[]}""");
        await File.WriteAllTextAsync(Path.Combine(scenes, "Broken.age.scene.json"), "{broken");
        await File.WriteAllTextAsync(
            Path.Combine(scenes, "Future.age.scene.json"),
            """{"schemaVersion":3,"id":"future","name":"Future","capabilities":[],"entities":[]}""");

        var result = await ExecuteAsync(root);

        Assert.True(result.Ok);
        Assert.False(result.Value.CanMigrate);
        Assert.Equal(3, result.Value.Documents.Count);
        Assert.Equal(
            ["rekall.project.json", "Scenes/Broken.age.scene.json", "Scenes/Future.age.scene.json"],
            result.Value.Documents.Select(item => item.RelativePath));
        Assert.Contains(result.Value.Documents, item =>
            item.Status == "future" && item.Code == "REKALL_DOCUMENT_SCHEMA_FUTURE");
        Assert.Contains(result.Value.Documents, item =>
            item.Status == "malformed" && item.Code == "REKALL_DOCUMENT_JSON_MALFORMED");
        Assert.Equal(3, result.Value.Blockers.Count);
        Assert.DoesNotContain(
            result.Value.NextActions,
            action => action.Tool == "rekall.compatibility.migrate_project");
    }

    [Fact]
    public async Task MixedCurrentAndLegacyDocumentsRemainMigratable()
    {
        var root = TestPaths.CreateTempDirectory();
        var scenes = Path.Combine(root, "Scenes");
        Directory.CreateDirectory(scenes);
        await File.WriteAllTextAsync(
            Path.Combine(root, "rekall.project.json"),
            """{"name":"Mixed","schemaVersion":1,"capabilities":[]}""");
        await File.WriteAllTextAsync(
            Path.Combine(scenes, "Main.age.scene.json"),
            """{"id":"legacy","name":"Main","capabilities":[],"entities":[]}""");

        var result = await ExecuteAsync(root);

        Assert.Equal(["current", "legacy"], result.Value.Documents.Select(item => item.Status));
        Assert.True(result.Value.CanMigrate);
    }

    [Fact]
    public async Task ReparseScenesDirectoryIsRejectedWithoutTraversal()
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RekallAgeTests",
            Guid.NewGuid().ToString("N"));
        var root = Path.Combine(basePath, "project");
        var external = Path.Combine(basePath, "external-scenes");
        var scenesLink = Path.Combine(root, "Scenes");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);
        await File.WriteAllTextAsync(
            Path.Combine(root, "rekall.project.json"),
            """{"name":"Reparse","schemaVersion":1,"capabilities":[]}""");
        await File.WriteAllTextAsync(
            Path.Combine(external, "Hidden.age.scene.json"),
            """{"schemaVersion":1,"id":"hidden","name":"Hidden","capabilities":[],"entities":[]}""");

        try
        {
            CreateDirectoryLink(scenesLink, external);
            var result = await ExecuteAsync(root);

            Assert.Contains(
                result.Value.Blockers,
                item => item.Code == "REKALL_COMPATIBILITY_REPARSE_REJECTED");
            Assert.DoesNotContain(result.Value.Documents, item => item.RelativePath.Contains("Hidden", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(scenesLink))
            {
                Directory.Delete(scenesLink);
            }

            if (Directory.Exists(basePath))
            {
                Directory.Delete(basePath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MissingManifestIsAnExplicitBlocker()
    {
        var result = await ExecuteAsync(TestPaths.CreateTempDirectory());

        var document = Assert.Single(result.Value.Documents);
        Assert.Equal("missing", document.Status);
        Assert.Equal("REKALL_PROJECT_MANIFEST_MISSING", document.Code);
        Assert.False(result.Value.CanMigrate);
    }

    [Fact]
    public async Task OversizedDocumentAndDocumentLimitBecomeBoundedBlockers()
    {
        var oversizedRoot = TestPaths.CreateTempDirectory();
        var oversizedPath = Path.Combine(oversizedRoot, "rekall.project.json");
        await using (var stream = new FileStream(oversizedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(Rekall.Age.Core.Compatibility.RekallAgeDocumentSchemaProbe.MaximumDocumentBytes + 1);
        }

        var oversized = await ExecuteAsync(oversizedRoot);
        Assert.Contains(oversized.Value.Blockers, item => item.Code == "REKALL_DOCUMENT_TOO_LARGE");

        var limitedRoot = TestPaths.CreateTempDirectory();
        var scenes = Path.Combine(limitedRoot, "Scenes");
        Directory.CreateDirectory(scenes);
        await File.WriteAllTextAsync(
            Path.Combine(limitedRoot, "rekall.project.json"),
            """{"name":"Limited","schemaVersion":1,"capabilities":[]}""");
        await File.WriteAllTextAsync(
            Path.Combine(scenes, "Main.age.scene.json"),
            """{"schemaVersion":1,"id":"main","name":"Main","capabilities":[],"entities":[]}""");
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("bounded compatibility"),
            CancellationToken.None);

        var limited = await new InspectProjectCompatibilityCommand().ExecuteAsync(
            new InspectProjectCompatibilityRequest(limitedRoot, MaximumDocuments: 1),
            context);

        Assert.Contains(
            limited.Value.Blockers,
            item => item.Code == "REKALL_COMPATIBILITY_DOCUMENT_LIMIT_EXCEEDED");
    }

    private static ValueTask<RekallAgeCommandResult<InspectProjectCompatibilityResult>> ExecuteAsync(string root)
    {
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("inspect compatibility"),
            CancellationToken.None);
        return new InspectProjectCompatibilityCommand().ExecuteAsync(
            new InspectProjectCompatibilityRequest(root),
            context);
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
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
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
}
