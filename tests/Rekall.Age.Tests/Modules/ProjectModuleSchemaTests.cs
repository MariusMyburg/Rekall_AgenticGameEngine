using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class ProjectModuleSchemaTests
{
    [Fact]
    public async Task ComponentSchemasCanIncludeBuiltProjectModuleAssemblies()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("project schemas"), CancellationToken.None);
        var scaffoldResult = await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, "crystal.mining", "Crystal Mining", "CrystalMining", "MiningController"),
            context);
        var buildResult = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(buildResult.Ok, buildResult.Summary);
        var command = new ListComponentSchemasCommand();

        var result = await command.ExecuteAsync(new ListComponentSchemasRequest(ProjectRoot: root), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Contains(
            result.Value.Components,
            component => component.TypeName == $"{scaffoldResult.Value.Namespace}.MiningController");
    }
}
