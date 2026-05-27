using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class ScaffoldPlayableModuleCommandTests
{
    [Fact]
    public async Task ScaffoldPlayableModuleCreatesBuildableAgentEditableShell()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("playable scaffold"), CancellationToken.None);

        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.agent", "Agent Module", "AgentModule"),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(File.Exists(scaffold.Value.SourcePath));
        Assert.True(build.Ok, build.Summary);
        Assert.Contains(build.Value.Modules, module => module.ModuleName == "AgentModule");
    }

    [Fact]
    public async Task ScaffoldPlayableModuleDoesNotCreateGenreStarterLoops()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("playable scaffold neutral"), CancellationToken.None);

        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.agent", "Agent Module", "AgentModule"),
            context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        Assert.Contains("agent-authored", source, StringComparison.Ordinal);
        Assert.Contains("AGENT PLAYABLE MODULE", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PONG", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("left-paddle", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("platformer", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tower", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScaffoldPlayableModuleGuidesAgentsToUseDeltaSeconds()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("playable scaffold delta"), CancellationToken.None);

        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.agent", "Agent Module", "AgentModule"),
            context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        Assert.Contains("input.DeltaSeconds", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectModuleLoaderCanLoadSameModuleNameFromDifferentProjects()
    {
        var firstRoot = TestPaths.CreateTempDirectory();
        var secondRoot = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("duplicate module names"), CancellationToken.None);

        foreach (var root in new[] { firstRoot, secondRoot })
        {
            await new ScaffoldPlayableModuleCommand().ExecuteAsync(
                new ScaffoldPlayableModuleRequest(root, "module.agent", "Agent Module", "AgentPlayable"),
                context);
            var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
            Assert.True(build.Ok, build.Summary);
        }

        var firstAssemblies = RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(firstRoot);
        var secondAssemblies = RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(secondRoot);

        Assert.Single(firstAssemblies);
        Assert.Single(secondAssemblies);
        Assert.Contains(firstAssemblies[0].GetTypes(), type => typeof(IRekallAgePlayableModule).IsAssignableFrom(type));
        Assert.Contains(secondAssemblies[0].GetTypes(), type => typeof(IRekallAgePlayableModule).IsAssignableFrom(type));
    }
}
