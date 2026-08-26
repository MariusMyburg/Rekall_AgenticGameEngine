using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class HardSurfaceModifierTests
{
    [Fact]
    public async Task SolidifyMirrorAndArrayComposeThroughCanonicalGraphNodes()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "hard-surface-proof", "Hard Surface Proof",
            [
                new("grid", "rekall.modeling.primitive.grid", 1, new JsonObject()),
                new("solidify", "rekall.modeling.solidify", 1, new JsonObject { ["thickness"] = 0.12, ["offset"] = 0.0, ["rim"] = true }),
                new("mirror", "rekall.modeling.mirror", 1, new JsonObject { ["axis"] = "x", ["origin"] = 1.0, ["mergeDistance"] = 0.0, ["bisect"] = false }),
                new("array", "rekall.modeling.array", 1, new JsonObject { ["count"] = 3, ["offset"] = new JsonArray(0.0, 0.0, 2.0), ["relativeOffset"] = false, ["instanceMode"] = false }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("grid-solid", "grid", "geometry", "solidify", "geometry"),
                new("solid-mirror", "solidify", "geometry", "mirror", "geometry"),
                new("mirror-array", "mirror", "geometry", "array", "geometry"),
                new("array-output", "array", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(17, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var mesh = report.Outputs["mesh"];
        Assert.Equal(48, mesh.Topology.PointIds.Count);
        Assert.Equal(36, mesh.Topology.FaceIds.Count);
        Assert.Equal(6, mesh.Topology.Positions.Select(point => Math.Round(point.Z, 6)).Distinct().Count());
        var catalog = RekallAgeModelingNodeCatalog.CreateDefault().Descriptors;
        Assert.Contains(catalog, item => item.TypeId == "rekall.modeling.solidify");
        Assert.Contains(catalog, item => item.TypeId == "rekall.modeling.mirror");
        Assert.Contains(catalog, item => item.TypeId == "rekall.modeling.array");
    }
}
