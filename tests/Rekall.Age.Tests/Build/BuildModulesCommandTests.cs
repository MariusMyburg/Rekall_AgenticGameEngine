using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Build;

public sealed class BuildModulesCommandTests
{
    [Fact]
    public async Task MissingModuleProjectSuggestsExecutablePlayableScaffold()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("missing modules"),
            CancellationToken.None);

        var result = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors, item => item.Code == "REKALL_MODULE_PROJECTS_MISSING");
        var suggestion = Assert.Single(error.SuggestedCommands!);
        Assert.Equal("rekall.module.scaffold_playable", suggestion.Tool);
        Assert.Equal(root, suggestion.Arguments["projectRoot"]);
        Assert.False(string.IsNullOrWhiteSpace((string)suggestion.Arguments["moduleId"]!));
        Assert.False(string.IsNullOrWhiteSpace((string)suggestion.Arguments["displayName"]!));
        Assert.False(string.IsNullOrWhiteSpace((string)suggestion.Arguments["moduleName"]!));
    }

    [Fact]
    public async Task PortableModulesBuildConcurrentlyWithoutSharingEngineOutputs()
    {
        var roots = Enumerable.Range(0, 6).Select(_ => TestPaths.CreateTempDirectory()).ToArray();
        var tasks = roots.Select(async (root, index) =>
        {
            var context = new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("parallel module"),
                CancellationToken.None);
            await new ScaffoldPlayableModuleCommand().ExecuteAsync(
                new ScaffoldPlayableModuleRequest(
                    root,
                    $"parallel.{index}",
                    $"Parallel {index}",
                    $"Parallel{index}"),
                context);
            return await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        });

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.True(
            result.Ok,
            string.Join(Environment.NewLine, result.Value.Modules.Select(module => module.Output))));
        Assert.All(results.SelectMany(result => result.Value.Modules), module =>
        {
            Assert.Equal(0, module.ExitCode);
            Assert.Equal("1", module.SdkVersion);
            Assert.True(File.Exists(module.AssemblyPath));
        });
    }

    [Fact]
    public async Task BuildModulesCompilesScaffoldedModuleProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("build modules"), CancellationToken.None);
        await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, "crystal.mining", "Crystal Mining", "CrystalMining", "MiningController"),
            context);
        var command = new BuildModulesCommand();

        var result = await command.ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.True(result.Ok, result.Summary);
        var module = Assert.Single(result.Value.Modules);
        Assert.Equal("CrystalMining", module.ModuleName);
        Assert.True(File.Exists(module.AssemblyPath));
        Assert.EndsWith("CrystalMining.dll", module.AssemblyPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildModulesCompilesWhenProjectRootIsRelative()
    {
        var parent = TestPaths.CreateTempDirectory();
        var projectRoot = Path.Combine(parent, "relative-game");
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("build modules relative"), CancellationToken.None);
        await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(projectRoot, "relative.flight", "Relative Flight", "RelativeFlight", "FlightController"),
            context);
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = parent;

            var result = await new BuildModulesCommand().ExecuteAsync(
                new BuildModulesRequest("relative-game"),
                context);

            Assert.True(result.Ok, result.Summary);
            var module = Assert.Single(result.Value.Modules);
            Assert.True(Path.IsPathFullyQualified(module.ProjectPath));
            Assert.True(File.Exists(module.AssemblyPath));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }
}
