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
        Assert.Contains("rekall.mesh.validate", names);
        Assert.Contains("rekall.mesh.query_elements", names);
        Assert.Contains("rekall.mesh.operation.preview", names);
        Assert.Contains("rekall.mesh.operation.apply", names);
        Assert.Contains("rekall.mesh.operation.batch", names);
        Assert.Contains("rekall.mesh.assert", names);

        var tools = RekallAgeMcpCatalog.FromRegistry(registry).Tools
            .Where(tool => tool.Name.StartsWith("rekall.mesh.", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(8, tools.Length);
        Assert.All(tools, tool => Assert.Equal("modeling", tool.Category));
        Assert.True(tools.Single(tool => tool.Name == "rekall.mesh.inspect").Recommended);
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
        Assert.Single(createContext.Transaction.ChangedResources);
        var revision = created.Value.Mesh!.FileRevision;

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
