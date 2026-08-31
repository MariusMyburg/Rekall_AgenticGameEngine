using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioAgentLiveRefreshPolicyTests
{
    [Theory]
    [InlineData("rekall.entity.create")]
    [InlineData("rekall.component.add")]
    [InlineData("rekall.scene.apply_blueprint")]
    [InlineData("rekall.geometry.create_recipe")]
    [InlineData("rekall.level.camera.aim_at")]
    public void SuccessfulAuthoringMutationsRefreshTheLiveScene(string toolName) =>
        Assert.True(RekallAgeStudioAgentLiveRefreshPolicy.ShouldRefresh(toolName, succeeded: true));

    [Theory]
    [InlineData("rekall.context.scene_summary", true)]
    [InlineData("rekall.validation.scene", true)]
    [InlineData("rekall.entity.create", false)]
    public void ReadsAndFailuresDoNotRefreshTheLiveScene(string toolName, bool succeeded) =>
        Assert.False(RekallAgeStudioAgentLiveRefreshPolicy.ShouldRefresh(toolName, succeeded));
}
