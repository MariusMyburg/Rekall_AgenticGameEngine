using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Mcp;
using Rekall.Age.Modeling.Commands;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshCommandTests
{
    [Fact]
    public void DefaultRegistryAndMcpExposeTypedModelingSurface()
    {
        var registry = RekallAgeDefaultCommandRegistry.Create();
        var names = registry.Schemas.Select(schema => schema.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("rekall.mesh.create_asset", names);
        Assert.Contains("rekall.mesh.inspect", names);
        Assert.Contains("rekall.mesh.inspect_compiled", names);
        Assert.Contains("rekall.mesh.pick_compiled", names);
        Assert.Contains("rekall.mesh.validate", names);
        Assert.Contains("rekall.mesh.query_elements", names);
        Assert.Contains("rekall.mesh.operation.preview", names);
        Assert.Contains("rekall.mesh.operation.apply", names);
        Assert.Contains("rekall.mesh.operation.batch", names);
        Assert.Contains("rekall.mesh.assert", names);
        Assert.Contains("rekall.mesh.operation_types.search", names);
        Assert.Contains("rekall.mesh.operation_types.inspect", names);
        Assert.Contains("rekall.mesh.fracture", names);

        var tools = RekallAgeMcpCatalog.FromRegistry(registry).Tools
            .Where(tool => tool.Name.StartsWith("rekall.mesh.", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(13, tools.Length);
        Assert.All(tools, tool => Assert.Equal("modeling", tool.Category));
        Assert.True(tools.Single(tool => tool.Name == "rekall.mesh.inspect").Recommended);
        Assert.True(tools.Single(tool => tool.Name == "rekall.mesh.inspect_compiled").Recommended);
        Assert.True(tools.Single(tool => tool.Name == "rekall.mesh.pick_compiled").Recommended);
        Assert.True(tools.Single(tool => tool.Name == "rekall.mesh.operation_types.search").Recommended);
        Assert.False(tools.Single(tool => tool.Name == "rekall.mesh.operation.apply").Recommended);
    }

    [Fact]
    public async Task CommandsCreateQueryPreviewApplyAndAssertWithExactRevisions()
    {
        var root = TestPaths.CreateTempDirectory();
        var createContext = Context("create");
        var created = await new CreateMeshAssetCommand().ExecuteAsync(
            new(root, "triangle", "Triangle", Triangle()),
            createContext);

        Assert.True(created.Ok);
        Assert.NotNull(created.Value.Mesh);
        Assert.Contains("rekall.mesh.operation.preview", created.Value.Mesh!.NextActions);
        Assert.Single(createContext.Transaction.ChangedResources);
        var revision = created.Value.Mesh.FileRevision;

        var queried = await new QueryMeshElementsCommand().ExecuteAsync(
            new(root, "triangle", new(RekallAgeGeometryDomain.Point, ExplicitElementIds: [1, 2]), 8),
            Context("query"));
        Assert.True(queried.Ok);
        Assert.Equal([1UL, 2UL], queried.Value.Query.ElementIds);

        var operation = new RekallAgeMeshOperationRequest(
            "transform",
            RekallAgeGeometryDomain.Point,
            [1],
            new JsonObject { ["x"] = 4 });
        var previewContext = Context("preview");
        var preview = await new PreviewMeshOperationCommand().ExecuteAsync(
            new(root, "triangle", revision, operation),
            previewContext);
        Assert.True(preview.Ok);
        Assert.False(preview.Value.Evidence!.Persisted);
        Assert.Contains("rekall.mesh.operation.apply", preview.Value.Evidence.NextActions);
        Assert.Empty(previewContext.Transaction.ChangedResources);

        var applyContext = Context("apply");
        var applied = await new ApplyMeshOperationCommand().ExecuteAsync(
            new(root, "triangle", revision, operation),
            applyContext);
        Assert.True(applied.Ok);
        Assert.True(applied.Value.Evidence!.Persisted);
        Assert.NotEqual(revision, applied.Value.Evidence.AfterFileRevision);
        Assert.Single(applyContext.Transaction.ResourcePreimages);

        var stale = await new ApplyMeshOperationCommand().ExecuteAsync(
            new(root, "triangle", revision, operation),
            Context("stale"));
        Assert.False(stale.Ok);
        Assert.Contains(stale.Errors, error => error.Code == "REKALL_DOCUMENT_REVISION_CONFLICT");

        var assertion = await new AssertMeshAssetCommand().ExecuteAsync(
            new(root, "triangle", ExpectedLogicalRevision: 2, MinimumPointCount: 3, MinimumFaceCount: 1),
            Context("assert"));
        Assert.True(assertion.Ok);
        Assert.True(assertion.Value.Passed);
    }

    [Fact]
    public async Task JsonRpcInspectsMeshWithBoundedStructuredEvidence()
    {
        var root = TestPaths.CreateTempDirectory();
        var created = await new CreateMeshAssetCommand().ExecuteAsync(
            new(root, "triangle", "Triangle", Triangle()),
            Context("create"));
        Assert.True(created.Ok);

        var registry = new RekallAgeCommandRegistry();
        registry.Register(new InspectMeshAssetCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "rekall.mesh.inspect",
                arguments = new { projectRoot = root, assetId = "triangle", maximumSamples = 2 }
            }
        });

        var response = await server.HandleJsonLineAsync(request, Context("mcp"));

        using var document = JsonDocument.Parse(response!);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        var mesh = result.GetProperty("structuredContent").GetProperty("value").GetProperty("mesh");
        Assert.Equal(3, mesh.GetProperty("topology").GetProperty("pointCount").GetInt32());
        Assert.Equal(2, mesh.GetProperty("pointIdSample").GetArrayLength());
        Assert.True(mesh.GetProperty("samplesTruncated").GetBoolean());
        Assert.Contains(
            mesh.GetProperty("nextActions").EnumerateArray(),
            action => action.GetString() == "rekall.mesh.operation.preview");
    }

    [Fact]
    public async Task CompiledInspectionExposesBoundedTrianglePickingProvenanceThroughRegistryJson()
    {
        var root = TestPaths.CreateTempDirectory();
        var created = await new CreateMeshAssetCommand().ExecuteAsync(
            new(root, "triangle", "Triangle", Triangle()),
            Context("create"));
        Assert.True(created.Ok);
        var registry = RekallAgeDefaultCommandRegistry.Create();

        var result = await registry.ExecuteJsonAsync(
            "rekall.mesh.inspect_compiled",
            JsonSerializer.Serialize(new { projectRoot = root, assetId = "triangle", maximumTriangles = 1 }),
            Context("compiled inspect"));

        Assert.True(result.Ok, result.Summary);
        var json = JsonSerializer.SerializeToElement(
            result.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Equal(3, json.GetProperty("vertexCount").GetInt32());
        Assert.False(json.GetProperty("hasVertexColors").GetBoolean());
        Assert.Equal(3, json.GetProperty("indexCount").GetInt32());
        var triangle = json.GetProperty("triangles")[0];
        Assert.Equal(21UL, triangle.GetProperty("sourceFaceId").GetUInt64());
        Assert.Equal([31UL, 32UL, 33UL], triangle.GetProperty("sourceCornerIds").EnumerateArray().Select(item => item.GetUInt64()));
        Assert.Equal([1UL, 2UL, 3UL], triangle.GetProperty("sourcePointIds").EnumerateArray().Select(item => item.GetUInt64()));
        Assert.False(json.GetProperty("trianglesTruncated").GetBoolean());
    }

    [Fact]
    public async Task CompiledPickReturnsNearestHitAndSourceProvenanceThroughRegistryJson()
    {
        var root = TestPaths.CreateTempDirectory();
        var created = await new CreateMeshAssetCommand().ExecuteAsync(
            new(root, "triangle", "Triangle", Triangle()),
            Context("create"));
        Assert.True(created.Ok);
        var registry = RekallAgeDefaultCommandRegistry.Create();

        var result = await registry.ExecuteJsonAsync(
            "rekall.mesh.pick_compiled",
            JsonSerializer.Serialize(new
            {
                projectRoot = root,
                assetId = "triangle",
                origin = new { x = 0.25, y = 0.25, z = -2.0 },
                direction = new { x = 0.0, y = 0.0, z = 1.0 },
                maximumDistance = 10.0,
                maximumHits = 4
            }),
            Context("compiled pick"));

        Assert.True(result.Ok, result.Summary);
        var json = JsonSerializer.SerializeToElement(
            result.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Equal(1, json.GetProperty("totalHitCount").GetInt32());
        var hit = json.GetProperty("hits")[0];
        Assert.Equal(21UL, hit.GetProperty("sourceFaceId").GetUInt64());
        Assert.Equal([31UL, 32UL, 33UL], hit.GetProperty("sourceCornerIds").EnumerateArray().Select(item => item.GetUInt64()));
        Assert.Equal([1UL, 2UL, 3UL], hit.GetProperty("sourcePointIds").EnumerateArray().Select(item => item.GetUInt64()));
        Assert.Equal(2.0, hit.GetProperty("distance").GetDouble(), 6);
        Assert.False(json.GetProperty("hitsTruncated").GetBoolean());
    }

    [Fact]
    public async Task FractureCommandReachableThroughTheRegistryPersistsChunkAssets()
    {
        var root = TestPaths.CreateTempDirectory();
        var box = await BoxPrimitive();
        await new CreateMeshAssetCommand().ExecuteAsync(new(root, "crate", "Crate", box.Topology, box.Attributes, box.MaterialSlots), Context("create-source"));

        var registry = RekallAgeDefaultCommandRegistry.Create();
        var response = await registry.ExecuteJsonAsync(
            "rekall.mesh.fracture",
            JsonSerializer.Serialize(new { projectRoot = root, sourceAssetId = "crate", chunkAssetIdPrefix = "crate-chunk", chunkCount = 4, seed = 7 }),
            Context("fracture"));

        Assert.True(response.Ok, response.Summary);
        var fractured = Assert.IsType<FractureMeshResult>(response.Value);
        Assert.Equal(4, fractured.Chunks.Count);
        Assert.Equal(["crate-chunk-0", "crate-chunk-1", "crate-chunk-2", "crate-chunk-3"], fractured.Chunks.Select(chunk => chunk.AssetId));
        Assert.All(fractured.Chunks, chunk => Assert.True(chunk.Topology.FaceCount > 0));

        var reloaded = await new InspectMeshAssetCommand().ExecuteAsync(new(root, "crate-chunk-0"), Context("inspect-chunk"));
        Assert.True(reloaded.Ok);
    }

    private static async ValueTask<RekallAgeMeshAsset> BoxPrimitive()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "source", "Source", [new("source", "rekall.modeling.primitive.box", 1, new())], [], [new("mesh", "source", "geometry")]);
        var result = await new Rekall.Age.Modeling.RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "tests", "desktop"), default);
        return result.Outputs["mesh"];
    }

    private static RekallAgeCommandContext Context(string name) =>
        new("mesh-tests", RekallAgeTransaction.Begin(name), CancellationToken.None);

    private static RekallAgeMeshTopology Triangle() => new(
        PointIds: [1, 2, 3],
        Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        EdgeIds: [11, 12, 13],
        EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0)],
        FaceIds: [21],
        FaceOffsets: [0, 3],
        CornerIds: [31, 32, 33],
        CornerPointIndices: [0, 1, 2],
        CornerEdgeIndices: [0, 1, 2]);
}
