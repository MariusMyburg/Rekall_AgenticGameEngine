using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using System.Diagnostics;

namespace Rekall.Age.Tests.Agent;

public sealed class ProjectCompatibilityMigrationCommandTests
{
    [Fact]
    public async Task DryRunPlansMigrationWithoutChangingBytesOrCreatingBackup()
    {
        var fixture = await CreateLegacyProjectAsync();
        var transaction = RekallAgeTransaction.Begin("dry-run migration");

        var result = await ExecuteAsync(fixture.Root, apply: false, transaction);

        Assert.True(result.Ok, result.Summary);
        Assert.False(result.Value.Applied);
        Assert.False(result.Value.NoOp);
        Assert.Null(result.Value.BackupRoot);
        Assert.Equal(2, result.Value.Documents.Count);
        Assert.All(result.Value.Documents, item => Assert.Equal((0, 1), (item.FromVersion, item.ToVersion)));
        Assert.Equal(fixture.ManifestBytes, await File.ReadAllBytesAsync(fixture.ManifestPath));
        Assert.Equal(fixture.SceneBytes, await File.ReadAllBytesAsync(fixture.ScenePath));
        Assert.Empty(transaction.ChangedResources);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, ".rekall", "migrations")));
    }

    [Fact]
    public async Task ApplyCreatesExactBackupsMigratesAtomicallyAndIsIdempotent()
    {
        var fixture = await CreateLegacyProjectAsync();
        var transaction = RekallAgeTransaction.Begin("apply migration");

        var result = await ExecuteAsync(fixture.Root, apply: true, transaction);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Applied);
        Assert.False(result.Value.NoOp);
        Assert.NotNull(result.Value.BackupRoot);
        Assert.Equal(
            fixture.ManifestBytes,
            await File.ReadAllBytesAsync(Path.Combine(result.Value.BackupRoot!, "rekall.project.json")));
        Assert.Equal(
            fixture.SceneBytes,
            await File.ReadAllBytesAsync(Path.Combine(result.Value.BackupRoot!, "Scenes", "Main.age.scene.json")));
        Assert.Contains("\"schemaVersion\": 1", await File.ReadAllTextAsync(fixture.ManifestPath));
        Assert.Contains("\"schemaVersion\": 1", await File.ReadAllTextAsync(fixture.ScenePath));
        Assert.Contains("\"agentExtension\"", await File.ReadAllTextAsync(fixture.ManifestPath));
        Assert.Contains("\"agentExtension\"", await File.ReadAllTextAsync(fixture.ScenePath));
        Assert.Equal([fixture.ManifestPath, fixture.ScenePath], transaction.ChangedResources);
        Assert.Equal(2, transaction.ResourcePreimages.Count);
        Assert.Equal(fixture.ManifestBytes, transaction.ResourcePreimages[0].Content);
        Assert.Equal(fixture.SceneBytes, transaction.ResourcePreimages[1].Content);

        var backupDirectories = Directory.GetDirectories(Path.Combine(fixture.Root, ".rekall", "migrations"));
        var second = await ExecuteAsync(
            fixture.Root,
            apply: true,
            RekallAgeTransaction.Begin("idempotent migration"));

        Assert.True(second.Ok);
        Assert.False(second.Value.Applied);
        Assert.True(second.Value.NoOp);
        Assert.Equal(backupDirectories, Directory.GetDirectories(Path.Combine(fixture.Root, ".rekall", "migrations")));
    }

    [Fact]
    public async Task FutureSchemaBlocksApplyWithoutChangingBytes()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "rekall.project.json");
        var bytes = """{"name":"Future","schemaVersion":2,"capabilities":[]}"""u8.ToArray();
        await File.WriteAllBytesAsync(path, bytes);

        var result = await ExecuteAsync(root, apply: true, RekallAgeTransaction.Begin("blocked migration"));

        Assert.False(result.Ok);
        Assert.False(result.Value.Applied);
        Assert.Contains(result.Value.Blockers, item => item.Code == "REKALL_DOCUMENT_SCHEMA_FUTURE");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.False(Directory.Exists(Path.Combine(root, ".rekall", "migrations")));
    }

    [Fact]
    public async Task FailureDuringReplacementRollsBackPreviouslyReplacedDocuments()
    {
        var fixture = await CreateLegacyProjectAsync();
        var migrator = new RekallAgeProjectCompatibilityMigrator((index, _) =>
            index == 1 ? new IOException("injected second replacement failure") : null);
        var command = new MigrateProjectCompatibilityCommand(migrator);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("rollback migration"),
            CancellationToken.None);

        var result = await command.ExecuteAsync(
            new MigrateProjectCompatibilityRequest(fixture.Root, Apply: true),
            context);

        Assert.False(result.Ok);
        Assert.Equal(fixture.ManifestBytes, await File.ReadAllBytesAsync(fixture.ManifestPath));
        Assert.Equal(fixture.SceneBytes, await File.ReadAllBytesAsync(fixture.ScenePath));
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task BackupRetentionKeepsOnlyFiveNewestSets()
    {
        var fixture = await CreateLegacyProjectAsync();
        for (var index = 0; index < 6; index++)
        {
            await File.WriteAllBytesAsync(fixture.ManifestPath, fixture.ManifestBytes);
            await File.WriteAllBytesAsync(fixture.ScenePath, fixture.SceneBytes);
            var result = await ExecuteAsync(
                fixture.Root,
                apply: true,
                RekallAgeTransaction.Begin($"retention migration {index}"));
            Assert.True(result.Ok, result.Summary);
        }

        Assert.Equal(
            5,
            Directory.GetDirectories(Path.Combine(fixture.Root, ".rekall", "migrations"), "migration-*").Length);
    }

    [Fact]
    public async Task ReparseMigrationDirectoryBlocksApply()
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RekallAgeTests",
            Guid.NewGuid().ToString("N"));
        var root = Path.Combine(basePath, "project");
        var external = Path.Combine(basePath, "external-engine-state");
        var engineLink = Path.Combine(root, ".rekall");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);
        var manifestPath = Path.Combine(root, "rekall.project.json");
        var original = """{"name":"Legacy","capabilities":[]}"""u8.ToArray();
        await File.WriteAllBytesAsync(manifestPath, original);

        try
        {
            CreateDirectoryLink(engineLink, external);
            var result = await ExecuteAsync(
                root,
                apply: true,
                RekallAgeTransaction.Begin("reparse migration"));

            Assert.False(result.Ok);
            Assert.Contains(result.Errors, item => item.Code == "REKALL_COMPATIBILITY_MIGRATION_FAILED");
            Assert.Equal(original, await File.ReadAllBytesAsync(manifestPath));
            Assert.Empty(Directory.EnumerateFileSystemEntries(external));
        }
        finally
        {
            if (Directory.Exists(engineLink))
            {
                Directory.Delete(engineLink);
            }

            if (Directory.Exists(basePath))
            {
                Directory.Delete(basePath, recursive: true);
            }
        }
    }

    private static ValueTask<RekallAgeCommandResult<MigrateProjectCompatibilityResult>> ExecuteAsync(
        string root,
        bool apply,
        RekallAgeTransaction transaction)
    {
        var context = new RekallAgeCommandContext("test", transaction, CancellationToken.None);
        return new MigrateProjectCompatibilityCommand().ExecuteAsync(
            new MigrateProjectCompatibilityRequest(root, apply),
            context);
    }

    private static async Task<LegacyFixture> CreateLegacyProjectAsync()
    {
        var root = TestPaths.CreateTempDirectory();
        var manifestPath = Path.Combine(root, "rekall.project.json");
        var scenePath = Path.Combine(root, "Scenes", "Main.age.scene.json");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
        var manifestBytes = """{"name":"Legacy","capabilities":["world"],"agentExtension":{"preserve":true}}"""u8.ToArray();
        var sceneBytes = """{"id":"legacy","name":"Main","capabilities":["world"],"entities":[],"agentExtension":{"preserve":true}}"""u8.ToArray();
        await File.WriteAllBytesAsync(manifestPath, manifestBytes);
        await File.WriteAllBytesAsync(scenePath, sceneBytes);
        return new LegacyFixture(root, manifestPath, scenePath, manifestBytes, sceneBytes);
    }

    private sealed record LegacyFixture(
        string Root,
        string ManifestPath,
        string ScenePath,
        byte[] ManifestBytes,
        byte[] SceneBytes);

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
