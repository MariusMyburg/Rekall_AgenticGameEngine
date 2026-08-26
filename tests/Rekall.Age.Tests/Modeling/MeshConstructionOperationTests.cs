using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshConstructionOperationTests
{
    [Theory]
    [InlineData("rekall.modeling.poke_faces")]
    [InlineData("rekall.modeling.bisect_plane")]
    public async Task ConstructionGraphNodesExecuteThroughCanonicalMeshOperations(string operationType)
    {
        var parameters = operationType.EndsWith("bisect_plane", StringComparison.Ordinal)
            ? new JsonObject { ["planeNormal"] = new JsonArray(1.0, 0.0, 0.0), ["clearPositive"] = true }
            : new JsonObject();
        var graph = RekallAgeModelingGraphAsset.Create("construction-graph", "Construction Graph",
            [new("source", "rekall.modeling.primitive.box", 1, new()), new("operation", operationType, 1, parameters),
             new("output", "rekall.modeling.output.mesh", 1, new())],
            [new("source-operation", "source", "geometry", "operation", "geometry"), new("operation-output", "operation", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"],
            RekallAgeModelingEvaluationBudget.Default, new(0, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        Assert.True(result.Outputs["mesh"].Revision > 0);
    }

    [Fact]
    public void FillHolesClosesASimpleBoundaryAndPreservesFaceMaterialDefaultsDeterministically()
    {
        var source = OpenBox();
        var request = new RekallAgeMeshOperationRequest("fill_holes", RekallAgeGeometryDomain.Edge,
            BoundaryEdges(source), new JsonObject { ["materialIndex"] = 1 });

        var first = Execute(source, request);
        var second = Execute(source, request);

        Assert.Equal(0, first.Validation.Summary.BoundaryEdgeCount);
        Assert.Equal(6, first.Mesh.Topology.FaceIds.Count);
        var material = Assert.Single(first.Mesh.Attributes, item => item.Semantic == "material-index");
        Assert.Equal(1, material.Values[^1].GetInt32());
        Assert.Equal(Canonical(first.Mesh), Canonical(second.Mesh));
        Assert.NotEmpty(first.Changes.CreatedFaceIds);
    }

    [Fact]
    public void BridgeEdgeLoopsBuildsDeterministicQuadsWithMaterialAndProvenance()
    {
        var source = TwoDiscs();
        var request = new RekallAgeMeshOperationRequest("bridge_edge_loops", RekallAgeGeometryDomain.Edge,
            BoundaryEdges(source), new JsonObject { ["materialIndex"] = 1 });

        var first = Execute(source, request);
        var second = Execute(source, request);

        Assert.Equal(0, first.Validation.Summary.BoundaryEdgeCount);
        Assert.Equal(6, first.Mesh.Topology.FaceIds.Count);
        Assert.Equal(4, first.Changes.CreatedFaceIds.Count);
        Assert.All(request.ElementIds, id => Assert.Contains(first.Provenance,
            p => p.Domain == RekallAgeGeometryDomain.Edge && p.InputElementId == id && p.OutputElementIds.SequenceEqual([id])));
        Assert.Equal(Canonical(first.Mesh), Canonical(second.Mesh));
    }

    [Fact]
    public void PokeFacesCreatesCentroidFansAndPreservesCornerAttributes()
    {
        var source = Quad();
        var result = Execute(source, new("poke_faces", RekallAgeGeometryDomain.Face, source.Topology.FaceIds, new()));

        Assert.Equal(5, result.Mesh.Topology.PointIds.Count);
        Assert.Equal(4, result.Mesh.Topology.FaceIds.Count);
        Assert.Equal(12, Assert.Single(result.Mesh.Attributes, item => item.Domain == RekallAgeGeometryDomain.Corner).Values.Count);
        Assert.Contains(result.Provenance, item => item.InputElementId == source.Topology.FaceIds[0] && item.OutputElementIds.Count == 4);
    }

    [Fact]
    public void DissolveEdgeMergesTwoFacesWithoutDiscardingSharedMaterial()
    {
        var source = TwoQuads();
        var shared = source.Topology.EdgeIds[1];
        var result = Execute(source, new("dissolve_edges", RekallAgeGeometryDomain.Edge, [shared], new()));

        Assert.Single(result.Mesh.Topology.FaceIds);
        Assert.Equal(6, result.Mesh.Topology.CornerIds.Count);
        Assert.Equal(6, result.Mesh.Topology.EdgeIds.Count);
        Assert.Equal(3, Assert.Single(result.Mesh.Attributes, item => item.Semantic == "material-index").Values[0].GetInt32());
        Assert.Contains(shared, result.Changes.DeletedEdgeIds);
        Assert.Equal(2, result.Provenance.Count(item => item.Domain == RekallAgeGeometryDomain.Face));
    }

    [Fact]
    public void BisectPlaneClipsCompleteMeshAndInterpolatesPointAttributesDeterministically()
    {
        var source = ClosedBox();
        var request = new RekallAgeMeshOperationRequest("bisect_plane", RekallAgeGeometryDomain.Face,
            source.Topology.FaceIds, new JsonObject
            {
                ["planeX"] = 0.5, ["planeY"] = 0, ["planeZ"] = 0,
                ["normalX"] = 1, ["normalY"] = 0, ["normalZ"] = 0,
                ["clearPositive"] = true, ["clearNegative"] = false, ["fill"] = false
            });

        var first = Execute(source, request);
        var second = Execute(source, request);

        Assert.All(first.Mesh.Topology.Positions, point => Assert.True(point.X <= 0.5 + 1e-9));
        Assert.Contains(first.Mesh.Topology.Positions, point => Math.Abs(point.X - 0.5) <= 1e-9);
        var weights = Assert.Single(first.Mesh.Attributes, item => item.Domain == RekallAgeGeometryDomain.Point).Values;
        Assert.Equal(first.Mesh.Topology.PointIds.Count, weights.Count);
        Assert.Contains(weights, value =>
        {
            var fraction = Math.Abs(value.GetDouble() - Math.Truncate(value.GetDouble()));
            return Math.Abs(fraction - 0.25) <= 1e-9 || Math.Abs(fraction - 0.75) <= 1e-9;
        });
        var uvs = Assert.Single(first.Mesh.Attributes, item => item.Domain == RekallAgeGeometryDomain.Corner).Values;
        Assert.Contains(uvs, value => Math.Abs(value[0].GetDouble() - 0.5) <= 1e-9);
        Assert.Equal(Canonical(first.Mesh), Canonical(second.Mesh));
    }

    [Theory]
    [InlineData("fill_holes", "REKALL_MESH_FILL_HOLES_SELECTION_INVALID")]
    [InlineData("bridge_edge_loops", "REKALL_MESH_BRIDGE_LOOPS_SELECTION_INVALID")]
    [InlineData("dissolve_edges", "REKALL_MESH_DISSOLVE_SELECTION_INVALID")]
    public void ConstructionOperationsRejectInvalidEdgeSelectionsExplicitly(string operation, string expectedCode)
    {
        var source = operation == "dissolve_edges" ? Quad() : ClosedBox();
        var error = Assert.Throws<RekallAgeMeshOperationException>(() => Execute(source,
            new(operation, RekallAgeGeometryDomain.Edge, [source.Topology.EdgeIds[0]], new())));
        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void BisectRejectsPartialFaceSelectionAndUnsupportedCapModeExplicitly()
    {
        var source = ClosedBox();
        var partial = Assert.Throws<RekallAgeMeshOperationException>(() => Execute(source,
            new("bisect_plane", RekallAgeGeometryDomain.Face, [source.Topology.FaceIds[0]], BisectParameters())));
        Assert.Equal("REKALL_MESH_BISECT_PARTIAL_SELECTION_UNSUPPORTED", partial.Code);

        var fillParameters = BisectParameters();
        fillParameters["fill"] = true;
        var fill = Assert.Throws<RekallAgeMeshOperationException>(() => Execute(source,
            new("bisect_plane", RekallAgeGeometryDomain.Face, source.Topology.FaceIds, fillParameters)));
        Assert.Equal("REKALL_MESH_BISECT_FILL_UNSUPPORTED", fill.Code);
    }

    private static RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
        new RekallAgeMeshOperationExecutor().Execute(source, request);

    private static JsonObject BisectParameters() => new()
    {
        ["planeX"] = 0, ["planeY"] = 0, ["planeZ"] = 0,
        ["normalX"] = 1, ["normalY"] = 0, ["normalZ"] = 0,
        ["clearPositive"] = true, ["clearNegative"] = false, ["fill"] = false
    };

    private static string Canonical(RekallAgeMeshAsset mesh) => JsonSerializer.Serialize(mesh, RekallAgeModelingJson.Options);

    private static IReadOnlyList<ulong> BoundaryEdges(RekallAgeMeshAsset mesh)
    {
        var use = new int[mesh.Topology.EdgeIds.Count];
        foreach (var edge in mesh.Topology.CornerEdgeIndices) use[edge]++;
        return use.Select((count, index) => (count, index)).Where(item => item.count == 1)
            .Select(item => mesh.Topology.EdgeIds[item.index]).ToArray();
    }

    private static RekallAgeMeshAsset Quad() => CreateMesh(
        [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)], [[0, 1, 2, 3]],
        cornerUv: true);

    private static RekallAgeMeshAsset TwoQuads() => CreateMesh(
        [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(0, 1, 0), new(1, 1, 0), new(2, 1, 0)],
        [[0, 1, 4, 3], [1, 2, 5, 4]], materialIndex: 3, materialSlots: 4);

    private static RekallAgeMeshAsset TwoDiscs() => CreateMesh(
        [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0),
         new(-1, -1, 2), new(-1, 1, 2), new(1, 1, 2), new(1, -1, 2)],
        [[0, 1, 2, 3], [4, 5, 6, 7]], materialIndex: 0, materialSlots: 2);

    private static RekallAgeMeshAsset ClosedBox() => CreateMesh(
        [new(-1,-1,-1), new(1,-1,-1), new(1,1,-1), new(-1,1,-1), new(-1,-1,1), new(1,-1,1), new(1,1,1), new(-1,1,1)],
        [[0,3,2,1], [4,5,6,7], [0,1,5,4], [1,2,6,5], [2,3,7,6], [3,0,4,7]],
        cornerUv: true, materialIndex: 0, materialSlots: 2, pointWeight: true);

    private static RekallAgeMeshAsset OpenBox()
    {
        var box = ClosedBox();
        return Execute(box, new("delete", RekallAgeGeometryDomain.Face, [box.Topology.FaceIds[1]], new())).Mesh with
        {
            MaterialSlots = [new("stone", "material.stone"), new("cap", "material.cap")]
        };
    }

    private static RekallAgeMeshAsset CreateMesh(
        IReadOnlyList<RekallAgeGeometryVector3> points,
        IReadOnlyList<IReadOnlyList<int>> faces,
        bool cornerUv = false,
        int? materialIndex = null,
        int materialSlots = 1,
        bool pointWeight = false)
    {
        var edgeMap = new Dictionary<(int, int), int>();
        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var cornerPoints = new List<int>(); var cornerEdges = new List<int>(); var offsets = new List<int> { 0 };
        foreach (var face in faces)
        {
            for (var i = 0; i < face.Count; i++)
            {
                var a = face[i]; var b = face[(i + 1) % face.Count]; var key = a < b ? (a, b) : (b, a);
                if (!edgeMap.TryGetValue(key, out var edge)) { edge = edges.Count; edgeMap[key] = edge; edges.Add(new(a, b)); }
                cornerPoints.Add(a); cornerEdges.Add(edge);
            }
            offsets.Add(cornerPoints.Count);
        }
        var attributes = new List<RekallAgeGeometryAttribute>();
        if (cornerUv) attributes.Add(new("uv", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2,
            cornerPoints.Select(index => JsonSerializer.SerializeToElement(new[] { points[index].X, points[index].Y })).ToArray(), "texcoord-0"));
        if (materialIndex.HasValue) attributes.Add(new("material", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.Int32,
            faces.Select(_ => JsonSerializer.SerializeToElement(materialIndex.Value)).ToArray(), "material-index", RekallAgeGeometryInterpolation.Constant));
        if (pointWeight) attributes.Add(new("weight", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float,
            points.Select((_, i) => JsonSerializer.SerializeToElement((double)i)).ToArray()));
        return RekallAgeMeshAsset.Create("construction", "Construction",
            new(Enumerable.Range(1, points.Count).Select(i => (ulong)i).ToArray(), points,
                Enumerable.Range(101, edges.Count).Select(i => (ulong)i).ToArray(), edges,
                Enumerable.Range(201, faces.Count).Select(i => (ulong)i).ToArray(), offsets,
                Enumerable.Range(301, cornerPoints.Count).Select(i => (ulong)i).ToArray(), cornerPoints, cornerEdges),
            attributes,
            Enumerable.Range(0, materialSlots).Select(i => new RekallAgeMaterialSlot($"slot-{i}", $"material.{i}")).ToArray());
    }
}
