using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class PlayerPresentationPolicyTests
{
    [Fact]
    public void PackagedPresentationKeepsAuthoredUiAndMakesDiagnosticsOptIn()
    {
        var packaged = RekallAgePlayerPresentationPolicy.Plan(["project", "Main"]);
        var diagnostic = RekallAgePlayerPresentationPolicy.Plan(["project", "Main", "--debug-hud"]);

        Assert.True(packaged.AuthoredUiEnabled);
        Assert.False(packaged.DebugHudEnabled);
        Assert.True(diagnostic.AuthoredUiEnabled);
        Assert.True(diagnostic.DebugHudEnabled);
    }
}
