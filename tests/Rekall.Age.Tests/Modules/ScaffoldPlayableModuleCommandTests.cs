using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class ScaffoldPlayableModuleCommandTests
{
    [Fact]
    public async Task ScaffoldPlayableModuleCreatesBuildableGameplayModule()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("playable scaffold"), CancellationToken.None);

        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.pong", "Module Pong", "ModulePong", "module-pong"),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(File.Exists(scaffold.Value.SourcePath));
        Assert.True(build.Ok, build.Summary);
        Assert.Contains(build.Value.Modules, module => module.ModuleName == "ModulePong");
    }
}
