using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshSubdivisionOperationTests
{
    [Fact]
    public async Task ProceduralSubdivideNodeUsesTheSameStrictOperation()
    {
        var graph = RekallAgeModelingGraphAsset.Create("subdivide", "Subdivide",
            [
                new("grid", "rekall.modeling.primitive.grid", 1, new JsonObject()),
                new("subdivide", "rekall.modeling.subdivide", 1, new JsonObject()),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("grid-subdivide", "grid", "geometry", "subdivide", "geometry"),
                new("subdivide-output", "subdivide", "geometry", "output", "input")
            ], [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"],
            RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded);
        Assert.Equal(5, report.Outputs["mesh"].Topology.PointIds.Count);
        Assert.Equal(4, report.Outputs["mesh"].Topology.FaceIds.Count);
    }

    [Fact]
    public async Task SubdivideFacesCreatesCentroidFanWithStableProvenanceAndCornerUvPropagation()
    {
        var source = await Grid();
        source = source with
        {
            Attributes =
            [
                new("uv.main", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2,
                [
                    JsonSerializer.SerializeToElement(new[] { 0d, 0d }), JsonSerializer.SerializeToElement(new[] { 1d, 0d }),
                    JsonSerializer.SerializeToElement(new[] { 1d, 1d }), JsonSerializer.SerializeToElement(new[] { 0d, 1d })
                ], "texcoord")
            ]
        };

        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("subdivide_faces", RekallAgeGeometryDomain.Face, source.Topology.FaceIds, new JsonObject()));

        Assert.Equal(5, result.Mesh.Topology.PointIds.Count);
        Assert.Equal(8, result.Mesh.Topology.EdgeIds.Count);
        Assert.Equal(4, result.Mesh.Topology.FaceIds.Count);
        Assert.Equal(12, result.Mesh.Topology.CornerIds.Count);
        Assert.Single(result.Changes.CreatedPointIds);
        Assert.Equal(4, result.Changes.CreatedEdgeIds.Count);
        Assert.Equal(3, result.Changes.CreatedFaceIds.Count);
        Assert.Equal(8, result.Changes.CreatedCornerIds.Count);
        Assert.Equal(4, Assert.Single(result.Provenance, item => item.Domain == RekallAgeGeometryDomain.Face).OutputElementIds.Count);
        var uv = Assert.Single(result.Mesh.Attributes);
        Assert.Equal(12, uv.Values.Count);
        Assert.Contains(uv.Values, item => item[0].GetDouble() == 0.5 && item[1].GetDouble() == 0.5);
        Assert.True(result.Validation.IsValid, string.Join(",", result.Validation.Diagnostics.Select(item => item.Code)));
    }

    private static async ValueTask<RekallAgeMeshAsset> Grid()
    {
        var graph = RekallAgeModelingGraphAsset.Create("grid", "Grid",
            [new("grid", "rekall.modeling.primitive.grid", 1, new JsonObject()), new("output", "rekall.modeling.output.mesh", 1, new JsonObject())],
            [new("link", "grid", "geometry", "output", "input")], [new("mesh", "output", "geometry")]);
        return (await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None)).Outputs["mesh"];
    }
}
