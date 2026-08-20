using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class ScaffoldRuntimeSystemModuleCommandTests
{
    [Fact]
    public void SchemaGivesAgentsTheExactCompactCallAndEditingContract()
    {
        var description = new ScaffoldRuntimeSystemModuleCommand().Schema.Description;

        Assert.Contains("\"projectRoot\"", description, StringComparison.Ordinal);
        Assert.Contains("\"moduleId\"", description, StringComparison.Ordinal);
        Assert.Contains("IRekallAgeRuntimeModuleSystem", description, StringComparison.Ordinal);
        Assert.Contains("read_source", description, StringComparison.Ordinal);
        Assert.Contains("build.modules", description, StringComparison.Ordinal);
    }

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
        var projectFile = await File.ReadAllTextAsync(scaffold.Value.ProjectPath);
        Assert.DoesNotContain("ProjectReference", projectFile);
        Assert.DoesNotContain(Path.GetFullPath("."), projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".rekall\\sdk\\1\\Rekall.Age.Sdk.props", projectFile);
        Assert.True(File.Exists(Path.Combine(root, ".rekall", "sdk", "1", "rekall.sdk.json")));

        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        Assert.Contains("[RekallAgeModule(\"game.motion\", \"Game Motion\")]", source);
        Assert.Contains("builder.RegisterComponent<OrbitMotion>();", source);
        Assert.Contains("builder.RegisterRuntimeSystem<OrbitMotionSystem>();", source);
        Assert.Contains("public sealed class OrbitMotion : RekallAgeComponent", source);
        Assert.Contains("public sealed class OrbitMotionSystem : IRekallAgeRuntimeModuleSystem", source);
        Assert.Contains("ValueTask<RekallAgeRuntimeWorld> UpdateAsync", source);
        Assert.Contains("entity.FindComponent(componentType)", source);
        Assert.Contains("component.Properties.ReadNumber(\"valuePerSecond\", 1)", source);
        Assert.Contains("entity.WithPosition3D", source);
        Assert.Contains("world.InputActionValue", source);
        Assert.Contains("world.IsInputActionDown", source);
        Assert.Contains("world.WasInputActionPressed", source);
        Assert.Contains("new RekallAgeRuntimeVector3", source);
        Assert.Contains("world.UpdateEntity", source);
        Assert.Contains("entity.UpdateComponent", source);

        Assert.True(build.Ok, build.Summary);
        Assert.True(schemas.Ok, schemas.Summary);
        Assert.Contains(
            schemas.Value.Components,
            component => component.TypeName == $"{scaffold.Value.Namespace}.OrbitMotion");
    }
}
