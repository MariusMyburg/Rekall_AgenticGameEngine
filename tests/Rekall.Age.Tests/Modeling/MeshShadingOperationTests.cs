using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshShadingOperationTests
{
    [Fact]
    public void DefaultRegistryExposesDiscoverableShadingOperations()
    {
        var registry = RekallAgeDefaultCommandRegistry.Create();
        Assert.Contains(registry.Schemas, item => item.Name == "rekall.mesh.operation_types.search");
        Assert.Contains(new RekallAgeMeshOperationExecutor().Descriptors, item => item.OperationId == "generate_normals");
        Assert.Contains(new RekallAgeMeshOperationExecutor().Descriptors, item => item.OperationId == "project_uv");
    }

    [Fact]
    public async Task GenerateCornerNormalsUsesStableFaceSelectionAndFiniteNewellNormals()
    {
        var mesh = await Grid(); var executor = new RekallAgeMeshOperationExecutor();

        var result = executor.Execute(mesh, new("generate_normals", RekallAgeGeometryDomain.Face,
            mesh.Topology.FaceIds, new JsonObject { ["attribute"] = "normal.authored" }));

        var normal = Assert.Single(result.Mesh.Attributes, item => item.Name == "normal.authored");
        Assert.Equal(RekallAgeGeometryDomain.Corner, normal.Domain);
        Assert.Equal(RekallAgeGeometryValueType.Float3, normal.ValueType);
        Assert.Equal("normal", normal.Semantic);
        Assert.All(normal.Values, item => Assert.Equal([0d, 0d, 1d], item.EnumerateArray().Select(value => value.GetDouble())));
        Assert.Equal(mesh.Topology.CornerIds, result.Changes.ModifiedCornerIds);
    }

    [Fact]
    public async Task ProjectUvWritesCornerDomainAttributeWithoutChangingTopology()
    {
        var mesh = await Grid(); var executor = new RekallAgeMeshOperationExecutor();

        var result = executor.Execute(mesh, new("project_uv", RekallAgeGeometryDomain.Face,
            mesh.Topology.FaceIds, new JsonObject
            {
                ["attribute"] = "uv.lightmap", ["axis"] = "xy",
                ["scaleU"] = 0.5, ["scaleV"] = 0.25, ["offsetU"] = 0.5, ["offsetV"] = 0.5
            }));

        var uv = Assert.Single(result.Mesh.Attributes, item => item.Name == "uv.lightmap");
        Assert.Equal(RekallAgeGeometryDomain.Corner, uv.Domain);
        Assert.Equal("texcoord", uv.Semantic);
        Assert.Contains(uv.Values, item => item[0].GetDouble() == 0.25 && item[1].GetDouble() == 0.375);
        Assert.Equal(mesh.Topology, result.Mesh.Topology);
        Assert.True(result.Validation.IsValid);
    }

    private static async ValueTask<RekallAgeMeshAsset> Grid()
    {
        var graph = RekallAgeModelingGraphAsset.Create("grid", "Grid",
            [new("grid", "rekall.modeling.primitive.grid", 1, new JsonObject { ["sizeX"] = 2, ["sizeY"] = 2 }), new("output", "rekall.modeling.output.mesh", 1, new JsonObject())],
            [new("link", "grid", "geometry", "output", "input")], [new("mesh", "output", "geometry")]);
        return (await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None)).Outputs["mesh"];
    }
}
