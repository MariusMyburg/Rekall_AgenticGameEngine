using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Tests.Workflows;

/// <summary>
/// Exercises the real, end-to-end <see cref="PublishWebGameCommand"/>/<see cref="AuditWebGameCommand"/> pipeline:
/// a real static-module build, a real declarative content-closure export, and a real trimmed browser-wasm
/// `dotnet publish` of the generic Rekall.Age.Player.Web project. These are slow (multi-minute, real subprocess)
/// tests by design, the same convention already established by
/// <see cref="Rekall.Age.Tests.Workflows.WebGameExporterTests"/>.
/// </summary>
public sealed class WebGamePublishingTests
{
    [Fact]
    public async Task PublishesAndAuditsARealWebGameEndToEnd()
    {
        var root = TestPaths.CreateTempDirectory();
        var outputDirectory = TestPaths.CreateTempDirectory();
        Directory.Delete(outputDirectory);
        await WriteProjectAsync(root);
        await BuildRuntimeModuleAsync(root);

        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("web game publishing"),
            CancellationToken.None);

        var publish = await new PublishWebGameCommand().ExecuteAsync(
            new PublishWebGameRequest(root, "Main", outputDirectory),
            context);
        Assert.True(publish.Ok, $"{publish.Summary}{Environment.NewLine}{publish.Value.PublishOutput}");
        Assert.True(Directory.Exists(publish.Value.OutputDirectory));
        Assert.True(File.Exists(publish.Value.ManifestPath));
        Assert.NotNull(publish.Value.Manifest);
        Assert.Equal("Scenes/Main.age.scene.json", publish.Value.Manifest!.EntryScenePath);

        var audit = await new AuditWebGameCommand().ExecuteAsync(
            new AuditWebGameRequest(root, "Main", outputDirectory + ".audit"),
            context);
        Assert.True(
            audit.Ok,
            string.Join(
                Environment.NewLine,
                audit.Value.Checks.Where(check => !check.Passed).Select(check => $"{check.Name}: {check.Summary}")));
        Assert.Contains(audit.Value.Checks, check => check.Name == "manifest-integrity" && check.Passed);
        Assert.Contains(audit.Value.Checks, check => check.Name == "module-registry-coverage" && check.Passed);
        Assert.Contains(audit.Value.Checks, check => check.Name == "content-relocation" && check.Passed);
        Assert.Contains(audit.Value.Checks, check => check.Name == "runtime-identity" && check.Passed);
        Assert.Contains(audit.Value.Checks, check => check.Name == "static-server-boot" && check.Passed);

        // The audit must never claim tier-4/5 evidence (a real browser session) that it did not gather.
        Assert.False(audit.Value.BrowserSmokeFrameVerified);
        Assert.Contains("browser", audit.Value.BrowserSmokeFrameSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsAnOutputDirectoryThatOverlapsTheAuthoredProjectRoot()
    {
        var root = TestPaths.CreateTempDirectory();
        await WriteProjectAsync(root);
        await BuildRuntimeModuleAsync(root);
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("web game publishing overlap"),
            CancellationToken.None);

        var publish = await new PublishWebGameCommand().ExecuteAsync(
            new PublishWebGameRequest(root, "Main", Path.Combine(root, "Builds", "Web")),
            context);

        Assert.False(publish.Ok);
        Assert.Contains(publish.Errors, error => error.Code == "REKALL_WEB_GAME_PUBLISH_STAGING_FAILED");
    }

    private static async Task WriteProjectAsync(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "rekall.project.json"),
            """
            {"name":"Publishing Game","schemaVersion":1,"capabilities":["world","rendering2d","ui"]}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Scenes", "Main.age.scene.json"),
            """
            {"id":"scene_main","name":"Main","schemaVersion":1,"capabilities":["world","rendering2d","ui"],"entities":[]}
            """);
    }

    private static async Task BuildRuntimeModuleAsync(string root)
    {
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("web publishing module fixture"),
            CancellationToken.None);
        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root,
                "game.publish-rules",
                "Publish Rules",
                "PublishRules",
                "PublishState",
                "PublishRulesSystem"),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(build.Ok, string.Join(Environment.NewLine, build.Value.Modules.Select(module => module.Output)));
    }
}
