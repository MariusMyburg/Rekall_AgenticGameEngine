using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Agent;

public sealed class AgentTaskComposerTests
{
    [Fact]
    public void ComposerPreservesShortOrdinaryRequestAndAddsEngineOwnedExecutionEnvelope()
    {
        var root = TestPaths.CreateTempDirectory();
        const string request = "Create a nature scene viewed through moving raindrops on glass.";

        var composed = RekallAgeAgentTaskComposer.Compose(root, "Main", request);

        Assert.Equal(1, Count(composed, request));
        Assert.Contains("<user-request>", composed, StringComparison.Ordinal);
        Assert.Contains("authoritative product intent", composed, StringComparison.Ordinal);
        Assert.Contains("rights/provenance", composed, StringComparison.Ordinal);
        Assert.Contains("runtime testing", composed, StringComparison.Ordinal);
        Assert.Contains("without expecting the user to name engine tools", composed, StringComparison.Ordinal);
        Assert.Contains("final and approved for immediate execution", composed, StringComparison.Ordinal);
        Assert.Contains("Do not run a brainstorming or approval-gathering step", composed, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(root), composed, StringComparison.Ordinal);
        Assert.Contains("Active scene: Main", composed, StringComparison.Ordinal);
    }

    private static int Count(string value, string term)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(term, index, StringComparison.Ordinal)) >= 0; index += term.Length)
        {
            count++;
        }
        return count;
    }
}
