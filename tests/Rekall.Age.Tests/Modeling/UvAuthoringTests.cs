using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class UvAuthoringTests
{
    [Fact]
    public void SeamMarkingSplitsFaceIslandsWithoutChangingTopology()
    {
        var mesh = TwoTriangleSheet();
        var marked = new RekallAgeMeshOperationExecutor().Execute(mesh, new(
            "mark_uv_seams", RekallAgeGeometryDomain.Edge, [103],
            new JsonObject { ["attribute"] = "uv.seam", ["marked"] = true }));

        Assert.Equal(mesh.Topology, marked.Mesh.Topology);
        var seam = Assert.Single(marked.Mesh.Attributes, item => item.Name == "uv.seam");
        Assert.Equal(RekallAgeGeometryDomain.Edge, seam.Domain);
        Assert.True(seam.Values[2].GetBoolean());
        var islands = new RekallAgeUvIslandInspector().Inspect(marked.Mesh, "uv.seam");
        Assert.Equal(2, islands.Count);
        Assert.Equal([201UL], islands[0].FaceIds);
        Assert.Equal([202UL], islands[1].FaceIds);
    }

    [Theory]
    [InlineData("planar")]
    [InlineData("box")]
    [InlineData("cylindrical")]
    [InlineData("spherical")]
    public void ProjectionModesWriteFiniteCornerDomainMapsDeterministically(string projection)
    {
        var mesh = TwoTriangleSheet();
        var request = new RekallAgeMeshOperationRequest(
            "project_uv", RekallAgeGeometryDomain.Face, mesh.Topology.FaceIds,
            new JsonObject { ["attribute"] = "uv.detail", ["projection"] = projection, ["axis"] = "xy" });
        var executor = new RekallAgeMeshOperationExecutor();

        var first = executor.Execute(mesh, request).Mesh;
        var second = executor.Execute(mesh, request).Mesh;

        var uv = Assert.Single(first.Attributes, item => item.Name == "uv.detail");
        Assert.Equal(RekallAgeGeometryDomain.Corner, uv.Domain);
        Assert.All(uv.Values, value => Assert.All(value.EnumerateArray(), component => Assert.True(double.IsFinite(component.GetDouble()))));
        Assert.Equal(uv.Values.Select(value => value.GetRawText()), Assert.Single(second.Attributes, item => item.Name == "uv.detail").Values.Select(value => value.GetRawText()));
    }

    [Fact]
    public void UnwrapPackCreatesBoundedNamedUvAndPreservesExistingMap()
    {
        var mesh = TwoTriangleSheet() with
        {
            Attributes =
            [
                new("uv.base", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2,
                    Enumerable.Repeat(JsonSerializer.SerializeToElement(new[] { 0.25, 0.75 }), 6).ToArray(), "texcoord-0")
            ]
        };
        var result = new RekallAgeMeshOperationExecutor().Execute(mesh, new(
            "unwrap_pack_uv", RekallAgeGeometryDomain.Face, mesh.Topology.FaceIds,
            new JsonObject { ["attribute"] = "uv.lightmap", ["seamAttribute"] = "uv.seam", ["margin"] = 0.02 }));

        Assert.Contains(result.Mesh.Attributes, item => item.Name == "uv.base");
        var packed = Assert.Single(result.Mesh.Attributes, item => item.Name == "uv.lightmap");
        Assert.Equal("texcoord-1", packed.Semantic);
        Assert.All(packed.Values, value => Assert.All(value.EnumerateArray(), component => Assert.InRange(component.GetDouble(), 0.02, 0.98)));
        Assert.True(result.Validation.IsValid);
    }

    [Fact]
    public void CatalogPublishesUvAuthoringNodesAndOperations()
    {
        var operations = new RekallAgeMeshOperationExecutor().Descriptors.Select(item => item.OperationId).ToHashSet();
        Assert.Contains("mark_uv_seams", operations);
        Assert.Contains("unwrap_pack_uv", operations);
        Assert.NotNull(RekallAgeModelingNodeCatalog.CreateDefault().Find("rekall.modeling.uv.unwrap_pack", 1));
        Assert.NotNull(RekallAgeModelingNodeCatalog.CreateDefault().Find("rekall.modeling.uv.lightmap", 1));
    }

    [Fact]
    public async Task LightmapNodeEvaluatesEndToEndWithoutReplacingMaterialUv()
    {
        var graph = RekallAgeModelingGraphAsset.Create("uv-graph", "UV Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("base", "rekall.modeling.project_uv", 1, new JsonObject { ["attribute"] = "uv.base", ["projection"] = "box" }),
                new("lightmap", "rekall.modeling.uv.lightmap", 1, new JsonObject { ["margin"] = 0.02 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("a", "box", "geometry", "base", "geometry"),
                new("b", "base", "geometry", "lightmap", "geometry"),
                new("c", "lightmap", "geometry", "output", "input")
            ], [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None);

        Assert.Contains(result.Outputs["mesh"].Attributes, item => item.Name == "uv.base" && item.Semantic == "texcoord-0");
        Assert.Contains(result.Outputs["mesh"].Attributes, item => item.Name == "uv.lightmap" && item.Semantic == "texcoord-1");
    }

    private static RekallAgeMeshAsset TwoTriangleSheet() => RekallAgeMeshAsset.Create(
        "uv-sheet", "UV Sheet",
        new(
            [1, 2, 3, 4],
            [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
            [101, 102, 103, 104, 105],
            [new(0, 1), new(1, 2), new(2, 0), new(2, 3), new(3, 0)],
            [201, 202], [0, 3, 6],
            [301, 302, 303, 304, 305, 306],
            [0, 1, 2, 0, 2, 3],
            [0, 1, 2, 2, 3, 4]));
}
