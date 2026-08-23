using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModelingGraphContractTests
{
    [Fact]
    public void DefaultNodeCatalogPublishesCanonicalInitialInventory()
    {
        var catalog = RekallAgeModelingNodeCatalog.CreateDefault();

        Assert.Equal(15, catalog.Descriptors.Count);
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.primitive.box");
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.transform");
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.join");
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.extrude");
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.triangulate");
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.attribute.capture");
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.attribute.named");
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.field.math");
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.material.assign");
        Assert.Contains(catalog.Descriptors, descriptor => descriptor.TypeId == "rekall.modeling.output.mesh");
        Assert.All(catalog.Descriptors, descriptor =>
        {
            Assert.StartsWith("rekall.modeling.", descriptor.TypeId, StringComparison.Ordinal);
            Assert.Equal(1, descriptor.TypeVersion);
            Assert.NotEmpty(descriptor.Description);
            Assert.Equal(
                descriptor.Ports.Count,
                descriptor.Ports.Select(port => $"{port.Direction}:{port.PortId}").Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(descriptor.Parameters.Count, descriptor.Parameters.Select(parameter => parameter.ParameterId).Distinct(StringComparer.Ordinal).Count());
        });
    }

    [Fact]
    public void GraphCreateProducesVersionedStableSourceDocument()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "procedural-room",
            "Procedural Room",
            [
                new("source", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = 4.0 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("source-to-output", "source", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        Assert.Equal(RekallAgeModelingGraphAsset.CurrentSchemaVersion, graph.SchemaVersion);
        Assert.Equal(1, graph.Revision);
        Assert.Equal("procedural-room", graph.AssetId);
        Assert.Equal(["source", "output"], graph.Nodes.Select(node => node.NodeId));
        Assert.Equal("source-to-output", Assert.Single(graph.Links).LinkId);
        Assert.Equal("mesh", Assert.Single(graph.Outputs).Name);
    }

    [Fact]
    public void ValidatorCompilesOnlyReachableNodesInDeterministicDependencyOrder()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "reachable",
            "Reachable",
            [
                Node("box", "rekall.modeling.primitive.box"),
                Node("transform", "rekall.modeling.transform"),
                Node("output", "rekall.modeling.output.mesh"),
                Node("unused", "rekall.modeling.primitive.sphere")
            ],
            [
                new("box-transform", "box", "geometry", "transform", "geometry"),
                new("transform-output", "transform", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var report = new RekallAgeModelingGraphValidator(RekallAgeModelingNodeCatalog.CreateDefault()).Validate(graph);

        Assert.True(report.IsValid);
        Assert.Equal(["box", "transform", "output"], report.ExecutionPlan!.OrderedNodeIds);
        Assert.Equal(["unused"], report.UnreachableNodeIds);
        Assert.DoesNotContain(report.Diagnostics, item => item.Severity == RekallAgeModelingDiagnosticSeverity.Error);
    }

    [Fact]
    public void ValidatorRejectsUnknownPortsTypeMismatchesMissingInputsAndCyclesWithStableCodes()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "invalid",
            "Invalid",
            [
                Node("box", "rekall.modeling.primitive.box"),
                Node("math", "rekall.modeling.field.math"),
                Node("first", "rekall.modeling.transform"),
                Node("second", "rekall.modeling.transform"),
                Node("output", "rekall.modeling.output.mesh")
            ],
            [
                new("unknown-port", "box", "missing", "output", "input"),
                new("type-mismatch", "box", "geometry", "math", "a"),
                new("cycle-a", "first", "geometry", "second", "geometry"),
                new("cycle-b", "second", "geometry", "first", "geometry")
            ],
            [new("mesh", "output", "geometry")]);

        var report = new RekallAgeModelingGraphValidator(RekallAgeModelingNodeCatalog.CreateDefault()).Validate(graph);

        Assert.False(report.IsValid);
        Assert.Null(report.ExecutionPlan);
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MODELING_GRAPH_PORT_UNKNOWN");
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MODELING_GRAPH_LINK_TYPE_MISMATCH");
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MODELING_GRAPH_REQUIRED_INPUT_MISSING");
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MODELING_GRAPH_CYCLE");
    }

    private static RekallAgeModelingGraphNode Node(string nodeId, string typeId) =>
        new(nodeId, typeId, 1, new JsonObject());
}
