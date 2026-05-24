using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class ScaffoldModuleCommandTests
{
    [Fact]
    public async Task ScaffoldModuleCreatesHumanEditableCSharpModuleSkeleton()
    {
        var root = TestPaths.CreateTempDirectory();
        var command = new ScaffoldModuleCommand();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("scaffold"), CancellationToken.None);

        var result = await command.ExecuteAsync(
            new ScaffoldModuleRequest(
                root,
                "crystal.mining",
                "Crystal Mining",
                "CrystalMining",
                "MiningController"),
            context);

        Assert.True(result.Ok);
        Assert.True(File.Exists(result.Value.SourcePath));
        Assert.True(File.Exists(result.Value.ProjectPath));
        Assert.Contains(result.Value.SourcePath, context.Transaction.ChangedResources);
        Assert.Contains(result.Value.ProjectPath, context.Transaction.ChangedResources);

        var source = await File.ReadAllTextAsync(result.Value.SourcePath);
        Assert.Contains("[RekallAgeModule(\"crystal.mining\", \"Crystal Mining\")]", source);
        Assert.Contains("public sealed class CrystalMiningModule : RekallAgeModule", source);
        Assert.Contains("builder.RegisterComponent<MiningController>();", source);
        Assert.Contains("public sealed class MiningController : RekallAgeComponent", source);

        var project = await File.ReadAllTextAsync(result.Value.ProjectPath);
        Assert.Contains("<Project Sdk=\"Microsoft.NET.Sdk\">", project);
        Assert.Contains("Rekall.Age.Modules.csproj", project);
    }
}
