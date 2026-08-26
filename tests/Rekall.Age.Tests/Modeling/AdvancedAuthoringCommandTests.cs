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
        "rekall.modifier.stack.apply_patch", "rekall.modifier.stack.evaluate", "rekall.modifier.stack.bake",
        "rekall.modeling.curve.create", "rekall.modeling.curve.replace", "rekall.modeling.curve.inspect",
        "rekall.modeling.curve.list", "rekall.modeling.curve.evaluate",
        "rekall.modeling.rig.create", "rekall.modeling.rig.replace", "rekall.modeling.rig.inspect",
        "rekall.modeling.rig.list"
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

    [Fact]
    public async Task JsonCurveLoopCreatesInspectsEvaluatesAndRevisionReplacesResource()
    {
        var root = TestPaths.CreateTempDirectory(); var registry = RekallAgeDefaultCommandRegistry.Create();
        var splines = """
          [ { "splineId": 7, "kind": "CubicBezier", "cyclic": false, "controlPoints": [
            { "controlPointId": 11, "position": { "x": 0, "y": 0, "z": 0 }, "handleIn": { "x": 0, "y": 0, "z": 0 }, "handleOut": { "x": 0.5, "y": 1, "z": 0 }, "radius": 1, "tiltRadians": 0 },
            { "controlPointId": 12, "position": { "x": 2, "y": 1, "z": 0 }, "handleIn": { "x": 1.5, "y": 0, "z": 0 }, "handleOut": { "x": 2, "y": 1, "z": 0 }, "radius": 1, "tiltRadians": 0 }
          ] } ]
        """;
        var created = await Execute(registry, "rekall.modeling.curve.create", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "curve.command", "name": "Command Curve", "splines": {{splines}} }
        """);
        Assert.True(created.Ok, created.Summary);
        var fileRevision = JsonSerializer.SerializeToElement(created.Value, JsonOptions).GetProperty("curve").GetProperty("fileRevision").GetString();

        var evaluated = await Execute(registry, "rekall.modeling.curve.evaluate", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "curve.command", "resolutionPerSegment": 4, "maximumSamples": 3 }
        """);
        Assert.True(evaluated.Ok, evaluated.Summary);
        var evaluation = JsonSerializer.SerializeToElement(evaluated.Value, JsonOptions).GetProperty("evaluation");
        Assert.Equal(5, evaluation.GetProperty("pointCount").GetInt32());
        Assert.True(evaluation.GetProperty("samplesTruncated").GetBoolean());

        var replaced = await Execute(registry, "rekall.modeling.curve.replace", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "curve.command", "expectedRevision": {{JsonSerializer.Serialize(fileRevision)}}, "name": "Command Curve 2", "splines": {{splines}} }
        """);
        Assert.True(replaced.Ok, replaced.Summary);
        Assert.Equal(2, JsonSerializer.SerializeToElement(replaced.Value, JsonOptions).GetProperty("curve").GetProperty("logicalRevision").GetInt64());

        var listed = await Execute(registry, "rekall.modeling.curve.list", $$"""{ "projectRoot": {{JsonSerializer.Serialize(root)}} }""");
        var inspected = await Execute(registry, "rekall.modeling.curve.inspect", $$"""{ "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "curve.command" }""");
        Assert.True(listed.Ok, listed.Summary);
        Assert.True(inspected.Ok, inspected.Summary);
    }

    [Fact]
    public async Task JsonRigLoopCreatesInspectsListsAndRevisionReplacesNamedHierarchy()
    {
        var root = TestPaths.CreateTempDirectory(); var registry = RekallAgeDefaultCommandRegistry.Create();
        var joints = """
        [
          { "jointId": "root", "name": "Root", "parentIndex": null, "bindLocalMatrix": [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1] },
          { "jointId": "chest", "name": "Chest", "parentIndex": 0, "bindLocalMatrix": [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,1,0,1] }
        ]
        """;
        var created = await Execute(registry, "rekall.modeling.rig.create", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "rig.command", "name": "Command Rig", "joints": {{joints}} }
        """);
        Assert.True(created.Ok, created.Summary);
        var fileRevision = JsonSerializer.SerializeToElement(created.Value, JsonOptions).GetProperty("rig").GetProperty("fileRevision").GetString();

        var replaced = await Execute(registry, "rekall.modeling.rig.replace", $$"""
        { "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "rig.command", "expectedRevision": {{JsonSerializer.Serialize(fileRevision)}}, "name": "Command Rig 2", "joints": {{joints}} }
        """);
        var inspected = await Execute(registry, "rekall.modeling.rig.inspect", $$"""{ "projectRoot": {{JsonSerializer.Serialize(root)}}, "assetId": "rig.command" }""");
        var listed = await Execute(registry, "rekall.modeling.rig.list", $$"""{ "projectRoot": {{JsonSerializer.Serialize(root)}} }""");

        Assert.True(replaced.Ok, replaced.Summary);
        Assert.Equal(2, JsonSerializer.SerializeToElement(replaced.Value, JsonOptions).GetProperty("rig").GetProperty("logicalRevision").GetInt64());
        Assert.Equal(2, JsonSerializer.SerializeToElement(inspected.Value, JsonOptions).GetProperty("rig").GetProperty("jointCount").GetInt32());
        Assert.Equal("rig.command", Assert.Single(JsonSerializer.SerializeToElement(listed.Value, JsonOptions).GetProperty("assetIds").EnumerateArray()).GetString());
    }

    private static ValueTask<RekallAgeDynamicCommandResult> Execute(RekallAgeCommandRegistry registry, string name, string json) =>
        registry.ExecuteJsonAsync(name, json, new("test", RekallAgeTransaction.Begin(name), CancellationToken.None));
}
