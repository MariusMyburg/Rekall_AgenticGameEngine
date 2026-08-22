using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class PresentPassCommandPlannerTests
{
    [Fact]
    public void PlansValidatedFullscreenPresentDrawAndReusesStableResources()
    {
        using var planner = new RekallAgePresentPassCommandPlanner("veldrid-vulkan");

        var first = planner.Plan(1280, 720, new(0.08f, 0.10f, 0.14f, 1f));
        var resourceCount = planner.InspectResources().Count;
        var second = planner.Plan(1280, 720, new(0.02f, 0.04f, 0.08f, 1f));

        Assert.True(first.Finished);
        Assert.Equal(resourceCount, planner.InspectResources().Count);
        Assert.Equal(2, planner.SubmissionCount);
        Assert.Collection(second.Commands,
            item => Assert.IsType<RekallAgeBeginRenderPassCommand>(item),
            item => Assert.IsType<RekallAgeSetRenderPipelineCommand>(item),
            item => Assert.IsType<RekallAgeSetBindingSetCommand>(item),
            item => Assert.IsType<RekallAgeSetBindingSetCommand>(item),
            item => Assert.IsType<RekallAgeDrawCommand>(item),
            item => Assert.IsType<RekallAgeEndRenderPassCommand>(item));
        Assert.Equal(3U, Assert.IsType<RekallAgeDrawCommand>(second.Commands[4]).VertexCount);
    }

    [Fact]
    public void RecreatesOnlySizeDependentPresentResourcesAfterResize()
    {
        using var planner = new RekallAgePresentPassCommandPlanner("veldrid-vulkan");
        var first = planner.Plan(640, 360, new(0, 0, 0, 1));
        var firstTarget = Assert.IsType<RekallAgeBeginRenderPassCommand>(first.Commands[0]).Descriptor.RenderTarget;

        var resized = planner.Plan(1920, 1080, new(0, 0, 0, 1));
        var resizedTarget = Assert.IsType<RekallAgeBeginRenderPassCommand>(resized.Commands[0]).Descriptor.RenderTarget;

        Assert.NotEqual(firstTarget, resizedTarget);
        Assert.Equal(firstTarget.Slot, resizedTarget.Slot);
        Assert.True(resizedTarget.Generation > firstTarget.Generation);
        Assert.Equal(2, planner.SubmissionCount);
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1280, 0)]
    public void RejectsInvalidPresentExtent(int width, int height)
    {
        using var planner = new RekallAgePresentPassCommandPlanner("test");

        Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan(width, height, new(0, 0, 0, 1)));
    }
}
