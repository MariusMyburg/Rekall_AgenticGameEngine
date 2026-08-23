using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Workflows;

public sealed class DefaultCommandRegistryTests
{
    [Fact]
    public void DefaultRegistryComposesHumanStudioAndAgentEngineSurfaceOnce()
    {
        var registry = RekallAgeDefaultCommandRegistry.Create();
        var names = registry.Schemas.Select(schema => schema.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("rekall.project.create", names);
        Assert.Contains("rekall.entity.create", names);
        Assert.Contains("rekall.component.set_property", names);
        Assert.Contains("rekall.validation.scene", names);
        Assert.Contains("rekall.render.capture_runtime_viewport", names);
        Assert.Contains("rekall.play.scene", names);
        Assert.Contains("rekall.workflow.verify_playable_game", names);
        Assert.Contains("rekall.context.engine_status", names);
        Assert.Contains("rekall.module.scaffold_playable", names);
        Assert.Contains("rekall.mesh.operation.apply", names);
        Assert.Contains("rekall.mesh.assert", names);
        Assert.Equal(names.Count, registry.Schemas.Count);
        Assert.True(names.Count >= 100, $"Expected the complete engine command surface, found {names.Count} commands.");
    }
}
