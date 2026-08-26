using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class TaperDeformAuthoringTests
{
    [Fact]
    public async Task TaperScalesOnlyThePlanesPerpendicularToItsAuthoredAxis()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "taper-box", "Taper Box", CancellationToken.None);

        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("taper_points", RekallAgeGeometryDomain.Point, source.Topology.PointIds, new JsonObject
            {
                ["axis"] = "y", ["minimum"] = -0.5, ["maximum"] = 0.5,
                ["startScale"] = 1.0, ["endScale"] = 0.5,
                ["centerX"] = 0.0, ["centerY"] = 0.0, ["centerZ"] = 0.0
            }));

        Assert.True(result.Validation.IsValid);
        for (var index = 0; index < source.Topology.PointIds.Count; index++)
        {
            var before = source.Topology.Positions[index];
            var after = result.Mesh.Topology.Positions[index];
            var expectedScale = before.Y > 0 ? 0.5 : 1.0;
            Assert.Equal(before.Y, after.Y, 8);
            Assert.Equal(before.X * expectedScale, after.X, 8);
            Assert.Equal(before.Z * expectedScale, after.Z, 8);
        }
    }

    [Fact]
    public async Task TaperNodeAndModifierExposeTheSameInspectableContract()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "taper-graph", "Taper Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("taper", "rekall.modeling.deform.taper", 1, new JsonObject
                {
                    ["axis"] = "y", ["minimum"] = -0.5, ["maximum"] = 0.5,
                    ["startScale"] = 1.0, ["endScale"] = 0.6
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-taper", "box", "geometry", "taper", "geometry"),
                new("taper-output", "taper", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(17, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        Assert.Contains(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modeling.deform.taper");
        Assert.Contains(RekallAgeModifierCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modifier.deform.taper");

        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "taper-modifier-box", "Taper Modifier Box", CancellationToken.None);
        var stack = RekallAgeModifierStackAsset.Create(
            "taper-stack", "Taper Stack", source.AssetId, new string('a', 64),
            [new("taper", "rekall.modifier.deform.taper", 1, true, new JsonObject
            {
                ["axis"] = "y", ["minimum"] = -0.5, ["maximum"] = 0.5,
                ["startScale"] = 1.0, ["endScale"] = 0.6
            })]);
        var modifierReport = await new RekallAgeModifierStackEvaluator().EvaluateAsync(
            stack, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);

        Assert.True(modifierReport.Succeeded,
            string.Join(Environment.NewLine, modifierReport.Diagnostics.Select(item => item.Message)));
        Assert.Equal(0.3, modifierReport.Mesh!.Topology.Positions
            .Where(point => point.Y > 0).Max(point => Math.Abs(point.X)), 8);
    }
}
