using Rekall.Age.Core.Transactions;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class AgentAuthoringGauntletTests
{
    [Fact]
    public async Task GauntletAuthorsPackagesAuditsAndCapturesGenericPlayableProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(root, "Package");
        var context = new Rekall.Age.Core.Commands.RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("agent authoring gauntlet"),
            CancellationToken.None);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(root, "Gauntlet Proof", "Main", output),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Ready);
        Assert.Equal(root, result.Value.ProjectRoot);
        Assert.Equal("Main", result.Value.SceneName);
        Assert.NotNull(result.Value.Package);
        Assert.NotNull(result.Value.Audit);
        Assert.True(File.Exists(result.Value.Package!.ArchivePath));
        Assert.True(File.Exists(result.Value.Audit!.Capture.OutputPath));
        Assert.All(result.Value.Checks, check => Assert.True(check.Passed, check.Summary));
        Assert.Contains(result.Value.Checks, check => check.Name == "project-created");
        Assert.Contains(result.Value.Checks, check => check.Name == "scene-blueprint-authored");
        Assert.Contains(result.Value.Checks, check => check.Name == "module-source-authored");
        Assert.Contains(result.Value.Checks, check => check.Name == "package-audited");
        Assert.Contains(result.Value.NextActions, action => action == "rekall.workflow.inspect_playable_package");
        Assert.Contains(result.Value.NextActions, action => action == "rekall.workflow.run_playable_package");
        Assert.Contains(result.Value.NextActions, action => action == "rekall.workflow.capture_playable_package_frame");
        Assert.DoesNotContain("template", result.Value.AuthoringMode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.Value.NextActions, action => action.Contains("template", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GauntletStopsWithStructuredChecksWhenProjectRootIsUnsafe()
    {
        var context = new Rekall.Age.Core.Commands.RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("agent authoring gauntlet failure"),
            CancellationToken.None);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(string.Empty, "Gauntlet Proof"),
            context);

        Assert.False(result.Ok);
        Assert.False(result.Value.Ready);
        Assert.Contains(result.Value.Checks, check => check is { Name: "project-root", Passed: false });
        Assert.Contains(result.Value.NextActions, action => action == "rekall.project.create");
        Assert.Contains(result.Errors, error => error.Code == "REKALL_AGENT_GAUNTLET_PROJECT_ROOT_INVALID");
    }
}
