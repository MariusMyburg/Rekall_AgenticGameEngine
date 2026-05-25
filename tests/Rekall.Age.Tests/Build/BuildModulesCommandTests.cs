using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Build;

public sealed class BuildModulesCommandTests
{
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
