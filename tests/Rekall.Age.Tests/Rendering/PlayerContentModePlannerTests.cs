using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class PlayerContentModePlannerTests
{
    [Theory]
    [MemberData(nameof(CanonicalRuntimeArguments))]
    public void OrdinaryAndObsoletePlayableLaunchesUseCanonicalRuntimeScene(string[] arguments)
    {
        var plan = RekallAgePlayerContentModePlanner.Plan(arguments);

        Assert.Equal(RekallAgePlayerContentMode.RuntimeScene, plan.Mode);
    }

    [Fact]
    public void LegacyProofAdapterRequiresExplicitDiagnosticFlag()
    {
        var plan = RekallAgePlayerContentModePlanner.Plan(["game", "Main", "--legacy-playable-adapter"]);

        Assert.Equal(RekallAgePlayerContentMode.LegacyProofAdapter, plan.Mode);
    }

    public static TheoryData<string[]> CanonicalRuntimeArguments => new()
    {
        new[] { "game", "Main" },
        new[] { "game", "Main", "--playable" }
    };
}
