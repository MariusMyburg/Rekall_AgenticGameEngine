using System.Numerics;
using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class InteractiveFogFramePlannerTests
{
    [Fact]
    public void FroxelIntentIsExplicitlyExecutedAsBoundedAnalyticVolumes()
    {
        var volumes = Enumerable.Range(0, 10).Select(Volume).ToArray();
        var source = new RekallAgeVulkanFogPlan(
            "froxel", true, new RekallAgeVulkanFogGrid(160, 90, 48), volumes, [], true, false,
            new RekallAgeVulkanFogDispatch(40, 23, 12),
            new RekallAgeVulkanFogHistory(0, null, Vector3.Zero, Vector3.Zero, "froxel", new RekallAgeVulkanFogGrid(160, 90, 48)),
            []);

        var result = new RekallAgeInteractiveFogFramePlanner().Plan(source);

        Assert.True(result.Enabled);
        Assert.Equal("froxel", result.RequestedMode);
        Assert.Equal("analytic-ray", result.ExecutedMode);
        Assert.Equal(8, result.Volumes.Count);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_INTERACTIVE_FOG_ANALYTIC_EXECUTION");
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_INTERACTIVE_FOG_VOLUME_LIMIT");
    }

    private static RekallAgeVulkanFogVolume Volume(int index) => new(
        $"fog-{index}", $"Fog {index}", index == 0 ? "global" : "sphere", 0.01f,
        Vector3.One, Vector3.Zero, 0, 0.2f, 1, 10 - index, Vector3.Zero, Vector3.One,
        new Vector3(0.01f), Matrix4x4.Identity, Matrix4x4.Identity);
}
