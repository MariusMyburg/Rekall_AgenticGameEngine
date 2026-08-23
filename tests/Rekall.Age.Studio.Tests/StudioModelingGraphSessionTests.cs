using System.Text.Json.Nodes;
using System.IO;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioModelingGraphSessionTests
{
    [Fact]
    public async Task OpensPersistedGraphAndExposesCanonicalNodeContracts()
    {
        var root = TemporaryRoot();
        try
        {
            await new RekallAgeModelingGraphAssetStore().SaveAsync(root, BoxGraph(), CancellationToken.None);
            var session = new RekallAgeStudioModelingGraphSession();

            Assert.Equal(["box-graph"], session.ListAssets(root));
            await session.OpenAsync(root, "box-graph", CancellationToken.None);

            Assert.Equal("Box Graph", session.Graph!.Name);
            Assert.False(string.IsNullOrWhiteSpace(session.FileRevision));
            Assert.Equal(["mesh"], session.OutputNames);
            var box = Assert.Single(session.Nodes, item => item.NodeId == "box");
            Assert.Equal("Box", box.DisplayName);
            Assert.Contains(box.Parameters, item => item.ParameterId == "sizeX" && item.DisplayName == "Size X");
            Assert.Equal(0, box.IncomingLinkCount);
            Assert.Equal(1, box.OutgoingLinkCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReusesEvaluatorCacheAndPublishesOutputAndNodeEvidence()
    {
        var root = TemporaryRoot();
        try
        {
            await new RekallAgeModelingGraphAssetStore().SaveAsync(root, BoxGraph(), CancellationToken.None);
            var session = new RekallAgeStudioModelingGraphSession();
            await session.OpenAsync(root, "box-graph", CancellationToken.None);

            var first = await session.EvaluateAsync("mesh", CancellationToken.None);
            var cached = await session.EvaluateAsync("mesh", CancellationToken.None);

            Assert.True(first.Succeeded);
            Assert.Equal(0, first.CacheHitCount);
            Assert.True(cached.Succeeded);
            Assert.Equal(2, cached.CacheHitCount);
            Assert.Equal("mesh", session.SelectedOutputName);
            Assert.NotNull(session.OutputMesh);
            Assert.Equal(8, session.OutputMesh!.Topology.PointIds.Count);
            Assert.All(session.Nodes, item => Assert.True(item.LastEvaluation?.CacheHit));
            Assert.Contains("2 cache hit", session.EvaluationSummary, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsEvaluationBeforeOpeningAProjectGraph()
    {
        var session = new RekallAgeStudioModelingGraphSession();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.EvaluateAsync("mesh", CancellationToken.None));
        Assert.Contains("Open a procedural graph", exception.Message, StringComparison.Ordinal);
    }

    private static string TemporaryRoot() =>
        Path.Combine(Path.GetTempPath(), "rekall-age-studio-graph-" + Guid.NewGuid().ToString("N"));

    private static RekallAgeModelingGraphAsset BoxGraph() => RekallAgeModelingGraphAsset.Create(
        "box-graph",
        "Box Graph",
        [
            new("box", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = 2.0 }),
            new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
        ],
        [new("box-output", "box", "geometry", "output", "input")],
        [new("mesh", "output", "geometry")]);
}
