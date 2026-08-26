using System.Numerics;
using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class InteractiveShadowFramePlannerTests
{
    [Fact]
    public void PlanPreservesAuthoredCascadeMatricesSplitsAndBiasesForInteractiveBackends()
    {
        var first = Matrix4x4.CreateTranslation(1, 2, 3);
        var second = Matrix4x4.CreateScale(2);
        var source = new RekallAgeVulkanShadowPlan(
            true,
            2048,
            9,
            0.0015f,
            0.018f,
            80,
            4,
            Vector3.UnitZ,
            uint.MaxValue,
            [
                Cascade(0, 0.1f, 18, first),
                Cascade(1, 18, 80, second)
            ],
            3,
            0,
            []);

        var result = new RekallAgeInteractiveShadowFramePlanner().Plan(source);

        Assert.True(result.Enabled);
        Assert.Equal(2048, result.Resolution);
        Assert.Equal(2, result.CascadeCount);
        Assert.Equal(first, result.ViewProjections[0]);
        Assert.Equal(second, result.ViewProjections[1]);
        Assert.Equal(new Vector4(18, 80, 80, 80), result.SplitDepths);
        Assert.Equal(0.0015f, result.DepthBias);
        Assert.Equal(0.018f, result.NormalBias);
        Assert.Equal(9, result.FilterTapCount);
    }

    [Fact]
    public void DisabledPlanProducesSafeUnshadowedInteractiveFrame()
    {
        var result = new RekallAgeInteractiveShadowFramePlanner().Plan(null);

        Assert.False(result.Enabled);
        Assert.Equal(0, result.CascadeCount);
        Assert.All(result.ViewProjections, matrix => Assert.Equal(Matrix4x4.Identity, matrix));
    }

    private static RekallAgeVulkanShadowCascade Cascade(int index, float near, float far, Matrix4x4 matrix) =>
        new(index, near, far, matrix, new RekallAgeVulkanShadowAtlasViewport(0, 0, 2048, 2048, index), [], 0, 0);
}
