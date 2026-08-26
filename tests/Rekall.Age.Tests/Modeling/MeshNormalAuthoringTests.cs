using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshNormalAuthoringTests
{
    [Fact]
    public async Task PolicyShadeFacesPreservesUnselectedSmoothValuesAndTopology()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "shade-policy", "Shade Policy", CancellationToken.None);
        var executor = new RekallAgeMeshOperationExecutor();
        var firstFace = source.Topology.FaceIds[0];
        var secondFace = source.Topology.FaceIds[1];

        var first = executor.Execute(source, new(
            "shade_faces",
            RekallAgeGeometryDomain.Face,
            [firstFace],
            new JsonObject { ["smooth"] = false }));
        var second = executor.Execute(first.Mesh, new(
            "shade_faces",
            RekallAgeGeometryDomain.Face,
            [secondFace],
            new JsonObject { ["smooth"] = false }));
        var restored = executor.Execute(second.Mesh, new(
            "shade_faces",
            RekallAgeGeometryDomain.Face,
            [firstFace],
            new JsonObject { ["smooth"] = true }));

        var policy = Assert.Single(restored.Mesh.Attributes, item => item.Name == "normal.smooth");
        Assert.Equal(RekallAgeGeometryDomain.Face, policy.Domain);
        Assert.Equal(RekallAgeGeometryValueType.Bool, policy.ValueType);
        Assert.True(policy.Values[0].GetBoolean());
        Assert.False(policy.Values[1].GetBoolean());
        Assert.All(policy.Values.Skip(2), value => Assert.True(value.GetBoolean()));
        Assert.Equal(source.Topology, restored.Mesh.Topology);
        Assert.Equal(source.Revision + 3, restored.Mesh.Revision);
        Assert.Equal([firstFace], restored.Changes.ModifiedFaceIds);
        Assert.Equal(["normal.smooth"], restored.Changes.ChangedAttributes);
    }

    [Fact]
    public async Task PolicyMarkSharpPreservesUnselectedEdgeValuesAndSupportsUnmarking()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "sharp-policy", "Sharp Policy", CancellationToken.None);
        var executor = new RekallAgeMeshOperationExecutor();
        var firstEdge = source.Topology.EdgeIds[0];
        var secondEdge = source.Topology.EdgeIds[1];

        var first = executor.Execute(source, new(
            "mark_sharp",
            RekallAgeGeometryDomain.Edge,
            [firstEdge],
            new JsonObject { ["sharp"] = true }));
        var second = executor.Execute(first.Mesh, new(
            "mark_sharp",
            RekallAgeGeometryDomain.Edge,
            [secondEdge],
            new JsonObject { ["sharp"] = true }));
        var restored = executor.Execute(second.Mesh, new(
            "mark_sharp",
            RekallAgeGeometryDomain.Edge,
            [firstEdge],
            new JsonObject { ["sharp"] = false }));

        var policy = Assert.Single(restored.Mesh.Attributes, item => item.Name == "normal.sharp");
        Assert.Equal(RekallAgeGeometryDomain.Edge, policy.Domain);
        Assert.Equal(RekallAgeGeometryValueType.Bool, policy.ValueType);
        Assert.False(policy.Values[0].GetBoolean());
        Assert.True(policy.Values[1].GetBoolean());
        Assert.All(policy.Values.Skip(2), value => Assert.False(value.GetBoolean()));
        Assert.Equal(source.Topology, restored.Mesh.Topology);
        Assert.Equal(source.Revision + 3, restored.Mesh.Revision);
        Assert.Equal([firstEdge], restored.Changes.ModifiedEdgeIds);
        Assert.Equal(["normal.sharp"], restored.Changes.ChangedAttributes);
    }

    [Fact]
    public async Task WeightedNormalsShadeSegmentedBevelWithFiniteUnitCornerVectors()
    {
        var graph = RekallAgeModelingGraphAsset.Create("weighted-normal-proof", "Weighted Normal Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("bevel", "rekall.modeling.bevel", 1, new JsonObject { ["width"] = 0.08, ["segments"] = 3 }),
                new("normals", "rekall.modeling.weighted_normals", 1, new JsonObject { ["attribute"] = "normal.weighted", ["faceAreaWeight"] = 1.0 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("box-bevel", "box", "geometry", "bevel", "geometry"), new("bevel-normal", "bevel", "geometry", "normals", "geometry"), new("normal-output", "normals", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(1, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var mesh = report.Outputs["mesh"];
        var normals = Assert.Single(mesh.Attributes, item => item.Name == "normal.weighted");
        Assert.Equal(RekallAgeGeometryDomain.Corner, normals.Domain);
        Assert.Equal(mesh.Topology.CornerIds.Count, normals.Values.Count);
        Assert.All(normals.Values, value => Assert.InRange(Math.Sqrt(value[0].GetDouble() * value[0].GetDouble() + value[1].GetDouble() * value[1].GetDouble() + value[2].GetDouble() * value[2].GetDouble()), 0.999999, 1.000001));
    }
}
