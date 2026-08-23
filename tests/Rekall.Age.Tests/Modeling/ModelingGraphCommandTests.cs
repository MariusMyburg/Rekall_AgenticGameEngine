using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Mcp;
using Rekall.Age.Modeling;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModelingGraphCommandTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] CommandNames =
    [
        "rekall.modeling.node_types.search",
        "rekall.modeling.node_types.inspect",
        "rekall.modeling.graph.create",
        "rekall.modeling.graph.inspect",
        "rekall.modeling.graph.apply_patch",
        "rekall.modeling.graph.validate",
        "rekall.modeling.graph.evaluate",
        "rekall.modeling.graph.bake",
        "rekall.modeling.inspect_evaluation"
    ];

    [Fact]
    public void DefaultRegistryPublishesBoundedModelingGraphToolsToMcp()
    {
        var registry = RekallAgeDefaultCommandRegistry.Create();
        var catalog = RekallAgeMcpCatalog.FromRegistry(registry);

        Assert.All(CommandNames, name => Assert.Contains(registry.Schemas, schema => schema.Name == name));
        Assert.All(CommandNames, name => Assert.Contains(catalog.Tools, tool => tool.Name == name && tool.Category == "modeling"));
    }

    [Fact]
    public async Task JsonCommandsCreateEvaluatePatchReevaluateAndBakeWithoutDirectDocumentEditing()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = RekallAgeDefaultCommandRegistry.Create();

        var created = await Execute(registry, "rekall.modeling.graph.create", $$"""
        {
          "projectRoot": {{JsonSerializer.Serialize(root)}},
          "assetId": "command-graph",
          "name": "Command Graph",
          "nodes": [
            { "nodeId": "box", "typeId": "rekall.modeling.primitive.box", "typeVersion": 1, "parameters": { "sizeX": 2 } },
            { "nodeId": "output", "typeId": "rekall.modeling.output.mesh", "typeVersion": 1, "parameters": {} }
          ],
          "links": [
            { "linkId": "box-output", "fromNodeId": "box", "fromPortId": "geometry", "toNodeId": "output", "toPortId": "input" }
          ],
          "outputs": [ { "name": "mesh", "nodeId": "output", "portId": "geometry" } ]
        }
        """);
        Assert.True(created.Ok, created.Summary);
        var createdJson = JsonSerializer.SerializeToElement(created.Value, JsonOptions);
        var revision = createdJson.GetProperty("graph").GetProperty("fileRevision").GetString();

        var searched = await Execute(registry, "rekall.modeling.node_types.search", """
        { "query": "primitive.box", "maximumResults": 8 }
        """);
        Assert.True(searched.Ok, searched.Summary);
        Assert.Single(JsonSerializer.SerializeToElement(searched.Value, JsonOptions).GetProperty("nodeTypes").EnumerateArray());

        var nodeType = await Execute(registry, "rekall.modeling.node_types.inspect", """
        { "typeId": "rekall.modeling.primitive.box", "typeVersion": 1 }
        """);
        Assert.True(nodeType.Ok, nodeType.Summary);

        var graphInspection = await Execute(registry, "rekall.modeling.graph.inspect", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "command-graph", "maximumSamples": 8 }
        """);
        Assert.True(graphInspection.Ok, graphInspection.Summary);

        var validation = await Execute(registry, "rekall.modeling.graph.validate", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "command-graph" }
        """);
        Assert.True(validation.Ok, validation.Summary);

        var first = await Execute(registry, "rekall.modeling.graph.evaluate", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "command-graph", "outputNames": ["mesh"] }
        """);
        Assert.True(first.Ok, first.Summary);
        var firstJson = JsonSerializer.SerializeToElement(first.Value, JsonOptions).GetProperty("evaluation");
        Assert.Equal(0, firstJson.GetProperty("cacheHitCount").GetInt32());
        Assert.Equal(-1, firstJson.GetProperty("outputs")[0].GetProperty("bounds").GetProperty("min").GetProperty("x").GetDouble());

        var second = await Execute(registry, "rekall.modeling.graph.evaluate", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "command-graph", "outputNames": ["mesh"] }
        """);
        Assert.Equal(2, JsonSerializer.SerializeToElement(second.Value, JsonOptions).GetProperty("evaluation").GetProperty("cacheHitCount").GetInt32());

        var patched = await Execute(registry, "rekall.modeling.graph.apply_patch", $$"""
        {
          "projectRoot": {{JsonSerializer.Serialize(root)}},
          "assetId": "command-graph",
          "expectedRevision": {{JsonSerializer.Serialize(revision)}},
          "patch": { "operations": [
            { "kind": "SetParameter", "targetId": "box", "parameterId": "sizeX", "value": 6 }
          ] }
        }
        """);
        Assert.True(patched.Ok, patched.Summary);
        Assert.NotEmpty(patched.Transaction.ChangedResources);

        var third = await Execute(registry, "rekall.modeling.graph.evaluate", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "command-graph", "outputNames": ["mesh"] }
        """);
        var thirdJson = JsonSerializer.SerializeToElement(third.Value, JsonOptions).GetProperty("evaluation");
        Assert.Equal(2, thirdJson.GetProperty("invalidatedNodeCount").GetInt32());
        Assert.Equal(-3, thirdJson.GetProperty("outputs")[0].GetProperty("bounds").GetProperty("min").GetProperty("x").GetDouble());

        var baked = await Execute(registry, "rekall.modeling.graph.bake", $$"""
        {
          "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "command-graph", "outputName": "mesh",
          "targetMeshAssetId": "command-baked", "expectedTargetRevision": "missing"
        }
        """);
        Assert.True(baked.Ok, baked.Summary);
        var compiled = new RekallAgeMeshCompiler().Compile(
            await new RekallAgeMeshAssetStore().LoadAsync(root, "command-baked", CancellationToken.None));
        Assert.Equal(12, compiled.Triangles.Count);
        Assert.Equal(-3, compiled.Bounds.Min.X);

        var inspected = await Execute(registry, "rekall.modeling.inspect_evaluation", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "command-graph" }
        """);
        Assert.True(inspected.Ok, inspected.Summary);
        Assert.Equal(-3, JsonSerializer.SerializeToElement(inspected.Value, JsonOptions).GetProperty("evaluation").GetProperty("outputs")[0]
            .GetProperty("bounds").GetProperty("min").GetProperty("x").GetDouble());
    }

    private static ValueTask<RekallAgeDynamicCommandResult> Execute(
        RekallAgeCommandRegistry registry,
        string name,
        string arguments) =>
        registry.ExecuteJsonAsync(
            name,
            arguments,
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin(name), CancellationToken.None));
}
