using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class ScaffoldRuntimeSystemModuleCommandTests
{
    [Fact]
    public async Task ScaffoldRuntimeSystemModuleCreatesCompilableEditableRuntimeSystemSkeleton()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("scaffold runtime system"),
            CancellationToken.None);

        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root,
                "game.motion",
                "Game Motion",
                "GameMotion",
                "OrbitMotion",
                "OrbitMotionSystem"),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        var schemas = await new ListComponentSchemasCommand().ExecuteAsync(
            new ListComponentSchemasRequest(ProjectRoot: root),
            context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(File.Exists(scaffold.Value.SourcePath));
        Assert.True(File.Exists(scaffold.Value.ProjectPath));
        Assert.Contains(scaffold.Value.SourcePath, context.Transaction.ChangedResources);
        Assert.Contains(scaffold.Value.ProjectPath, context.Transaction.ChangedResources);
        Assert.Equal("OrbitMotion", scaffold.Value.ComponentClass);
        Assert.Equal("OrbitMotionSystem", scaffold.Value.SystemClass);

        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        Assert.Contains("[RekallAgeModule(\"game.motion\", \"Game Motion\")]", source);
        Assert.Contains("builder.RegisterComponent<OrbitMotion>();", source);
        Assert.Contains("builder.RegisterRuntimeSystem<OrbitMotionSystem>();", source);
        Assert.Contains("public sealed class OrbitMotion : RekallAgeComponent", source);
        Assert.Contains("public sealed class OrbitMotionSystem : IRekallAgeRuntimeModuleSystem", source);
        Assert.Contains("ValueTask<RekallAgeRuntimeWorld> UpdateAsync", source);

        Assert.True(build.Ok, build.Summary);
        Assert.True(schemas.Ok, schemas.Summary);
        Assert.Contains(
            schemas.Value.Components,
            component => component.TypeName == $"{scaffold.Value.Namespace}.OrbitMotion");
    }
}
