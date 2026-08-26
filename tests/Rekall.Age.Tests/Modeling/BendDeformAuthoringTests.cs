using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class BendDeformAuthoringTests
{
    [Fact]
    public async Task BendCurvesAxisAndCrossSectionAroundAuthoredOrigin()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "bend-box", "Bend Box", CancellationToken.None);

        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("bend_points", RekallAgeGeometryDomain.Point, source.Topology.PointIds, new JsonObject
            {
                ["axis"] = "y",
                ["bendAxis"] = "z",
                ["minimum"] = -0.5,
                ["maximum"] = 0.5,
                ["angleDegrees"] = 90.0,
                ["centerX"] = 0.0,
                ["centerY"] = 0.0,
                ["centerZ"] = 0.0
            }));

        Assert.True(result.Validation.IsValid);
        var topFrontIndex = source.Topology.Positions.ToList().FindIndex(point =>
            point.X == 0.5 && point.Y == 0.5 && point.Z == 0.5);
        Assert.True(topFrontIndex >= 0);
        var bent = result.Mesh.Topology.Positions[topFrontIndex];
        var radius = 1.0 / (Math.PI / 2.0);
        Assert.Equal(0.5, bent.X, 8);
        Assert.Equal(-0.5 + radius - 0.5, bent.Y, 8);
        Assert.Equal(radius, bent.Z, 8);
    }

    [Fact]
    public async Task BendNodeAndModifierExposeTheSameInspectableContract()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "bend-graph", "Bend Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("bend", "rekall.modeling.deform.bend", 1, new JsonObject
                {
                    ["axis"] = "y", ["bendAxis"] = "z",
                    ["minimum"] = -0.5, ["maximum"] = 0.5, ["angleDegrees"] = 35.0
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-bend", "box", "geometry", "bend", "geometry"),
                new("bend-output", "bend", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(19, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        Assert.Contains(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modeling.deform.bend");
        Assert.Contains(RekallAgeModifierCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modifier.deform.bend");

        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "bend-modifier-box", "Bend Modifier Box", CancellationToken.None);
        var stack = RekallAgeModifierStackAsset.Create(
            "bend-stack", "Bend Stack", source.AssetId, new string('b', 64),
            [new("bend", "rekall.modifier.deform.bend", 1, true, new JsonObject
            {
                ["axis"] = "y", ["bendAxis"] = "z",
                ["minimum"] = -0.5, ["maximum"] = 0.5, ["angleDegrees"] = 35.0
            })]);
        var modifierReport = await new RekallAgeModifierStackEvaluator().EvaluateAsync(
            stack, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);

        Assert.True(modifierReport.Succeeded,
            string.Join(Environment.NewLine, modifierReport.Diagnostics.Select(item => item.Message)));
        Assert.NotEqual(source.Topology.Positions, modifierReport.Mesh!.Topology.Positions);
    }
}
