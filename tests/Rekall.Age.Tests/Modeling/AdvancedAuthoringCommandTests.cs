using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Mcp;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Modeling;

public sealed class AdvancedAuthoringCommandTests
{
    private static readonly string[] Names =
    [
        "rekall.material.node_types.search", "rekall.material.node_types.inspect",
        "rekall.material.graph.create", "rekall.material.graph.inspect", "rekall.material.graph.apply_patch",
        "rekall.material.graph.validate", "rekall.material.graph.compile",
        "rekall.material.instance.create", "rekall.material.instance.inspect",
        "rekall.modifier.types.search", "rekall.modifier.stack.create", "rekall.modifier.stack.inspect",
        "rekall.modifier.stack.apply_patch", "rekall.modifier.stack.evaluate", "rekall.modifier.stack.bake"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void RegistryAndMcpExposeAdvancedAuthoringCommandsAsModelingTools()
    {
        var registry = RekallAgeDefaultCommandRegistry.Create(); var catalog = RekallAgeMcpCatalog.FromRegistry(registry);
        Assert.All(Names, name => Assert.Contains(registry.Schemas, item => item.Name == name));
        Assert.All(Names, name => Assert.Contains(catalog.Tools, item => item.Name == name && item.Category == "modeling"));
    }

    [Fact]
    public async Task JsonMaterialLoopCreatesPatchesCompilesAndCreatesInstance()
    {
        var root = TestPaths.CreateTempDirectory(); var registry = RekallAgeDefaultCommandRegistry.Create();
        var created = await Execute(registry, "rekall.material.graph.create", $$"""
        {
          "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "json-material", "name": "JSON Material",
          "nodes": [
            { "nodeId": "pbr", "typeId": "rekall.material.surface.pbr", "typeVersion": 1, "parameters": { "roughness": 1.0 } },
            { "nodeId": "output", "typeId": "rekall.material.output", "typeVersion": 1, "parameters": {} }
          ],
          "links": [ { "linkId": "link", "fromNodeId": "pbr", "fromPortId": "surface", "toNodeId": "output", "toPortId": "surface" } ],
          "output": { "name": "surface", "nodeId": "output", "portId": "surface" },
          "exposedParameters": [ { "name": "Roughness", "nodeId": "pbr", "parameterId": "roughness", "valueType": "Float", "defaultValue": 1.0 } ]
        }
        """);
        Assert.True(created.Ok, created.Summary);
        var graphRevision = JsonSerializer.SerializeToElement(created.Value, JsonOptions).GetProperty("graph").GetProperty("fileRevision").GetString();
        var patched = await Execute(registry, "rekall.material.graph.apply_patch", $$"""
        {
          "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "json-material", "expectedRevision": {{JsonSerializer.Serialize(graphRevision)}},
          "patch": { "operations": [ { "kind": "SetParameter", "targetId": "pbr", "parameterId": "roughness", "value": 0.5 } ] }
        }
        """);
        Assert.True(patched.Ok, patched.Summary);
        var patchedRevision = JsonSerializer.SerializeToElement(patched.Value, JsonOptions).GetProperty("graph").GetProperty("fileRevision").GetString();
        var compiled = await Execute(registry, "rekall.material.graph.compile", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "json-material", "maximumSourceCharacters": 1024 }
        """);
        Assert.True(compiled.Ok, compiled.Summary);
        Assert.NotEmpty(JsonSerializer.SerializeToElement(compiled.Value, JsonOptions).GetProperty("contentHash").GetString()!);
        var instance = await Execute(registry, "rekall.material.instance.create", $$"""
        {
          "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "json-instance", "name": "JSON Instance",
          "graphAssetId": "json-material", "graphFileRevision": {{JsonSerializer.Serialize(patchedRevision)}}, "overrides": { "Roughness": 0.25 }
        }
        """);
        Assert.True(instance.Ok, instance.Summary);
    }

    private static ValueTask<RekallAgeDynamicCommandResult> Execute(RekallAgeCommandRegistry registry, string name, string json) =>
        registry.ExecuteJsonAsync(name, json, new("test", RekallAgeTransaction.Begin(name), CancellationToken.None));
}
