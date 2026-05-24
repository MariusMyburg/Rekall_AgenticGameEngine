using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Build.Commands;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleSourceInspectionCommandTests
{
    [Fact]
    public async Task ListModuleSourcesReturnsAgentReadableSourceCatalog()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("list module source"), CancellationToken.None);
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.pong", "Module Pong", "ModulePong", "pong"),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);

        var list = await new ListModuleSourcesCommand().ExecuteAsync(
            new ListModuleSourcesRequest(root),
            context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(build.Ok, build.Summary);
        Assert.True(list.Ok, list.Summary);
        var source = Assert.Single(list.Value.Sources);
        Assert.Equal("ModulePong", source.ModuleName);
        Assert.Equal("ModulePongModule.cs", source.FileName);
        Assert.Equal(scaffold.Value.SourcePath, source.SourcePath);
        Assert.True(source.Bytes > 0);
    }

    [Fact]
    public async Task ReadModuleSourceReturnsSourceText()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("read module source"), CancellationToken.None);
        await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.pong", "Module Pong", "ModulePong", "pong"),
            context);

        var read = await new ReadModuleSourceCommand().ExecuteAsync(
            new ReadModuleSourceRequest(root, "ModulePong", "ModulePongModule.cs"),
            context);

        Assert.True(read.Ok, read.Summary);
        Assert.Equal("ModulePong", read.Value.ModuleName);
        Assert.Equal("ModulePongModule.cs", read.Value.FileName);
        Assert.Contains("public sealed class ModulePongModule", read.Value.Source, StringComparison.Ordinal);
        Assert.Contains("PONG", read.Value.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadModuleSourceRejectsPathsOutsideDirectModuleDirectory()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("read module source rejected"), CancellationToken.None);

        var read = await new ReadModuleSourceCommand().ExecuteAsync(
            new ReadModuleSourceRequest(root, "ModulePong", "..\\Outside.cs"),
            context);

        Assert.False(read.Ok);
        Assert.Contains(read.Errors, error => error.Code == "REKALL_MODULE_SOURCE_PATH_OUTSIDE_PROJECT");
    }
}
