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
        var projectFile = await File.ReadAllTextAsync(scaffold.Value.ProjectPath);
        Assert.DoesNotContain("ProjectReference", projectFile);
        Assert.DoesNotContain(Path.GetFullPath("."), projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".rekall\\sdk\\1\\Rekall.Age.Sdk.props", projectFile);
        Assert.True(File.Exists(Path.Combine(root, ".rekall", "sdk", "1", "rekall.sdk.json")));
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
    public async Task ScaffoldPlayableModuleRefusesToOverwriteExistingAgentSource()
    {
        var root = TestPaths.CreateTempDirectory();
        var command = new ScaffoldPlayableModuleCommand();
        var request = new ScaffoldPlayableModuleRequest(root, "module.agent", "Agent Module", "AgentModule");
        var first = await command.ExecuteAsync(
            request,
            new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("first playable scaffold"),
                CancellationToken.None));
        const string authoredSource = "// irreplaceable agent-authored playable";
        await File.WriteAllTextAsync(first.Value.SourcePath, authoredSource);
        var secondTransaction = RekallAgeTransaction.Begin("duplicate playable scaffold");

        var second = await command.ExecuteAsync(
            request,
            new RekallAgeCommandContext("agent", secondTransaction, CancellationToken.None));

        Assert.False(second.Ok);
        var error = Assert.Single(second.Errors);
        Assert.Equal("REKALL_MODULE_SCAFFOLD_ALREADY_EXISTS", error.Code);
        Assert.Contains("rekall.module.read_source", error.SuggestedCommands!.Select(item => item.Tool));
        Assert.Equal(authoredSource, await File.ReadAllTextAsync(first.Value.SourcePath));
        Assert.Empty(secondTransaction.ChangedResources);
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

    [Fact]
    public async Task LoadedProjectModuleDoesNotLockItsAuthoringBuildOutput()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("rebuild loaded module"),
            CancellationToken.None);
        await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.agent", "Agent Module", "ReloadableModule"),
            context);
        var command = new BuildModulesCommand();
        var firstBuild = await command.ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(firstBuild.Ok, firstBuild.Summary);

        var assemblies = RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(root);
        Assert.Single(assemblies);
        Assert.Contains(assemblies[0].GetTypes(), type => typeof(IRekallAgePlayableModule).IsAssignableFrom(type));
        await File.AppendAllTextAsync(
            Path.Combine(root, "Modules", "ReloadableModule", "ReloadableModuleModule.cs"),
            Environment.NewLine + "// agent-authored rebuild" + Environment.NewLine);

        var secondBuild = await command.ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.True(
            secondBuild.Ok,
            string.Join(Environment.NewLine, secondBuild.Value.Modules.Select(module => module.Output)));
    }
}
