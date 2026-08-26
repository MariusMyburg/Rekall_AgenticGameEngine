using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class CurveRevolveTests
{
    [Fact]
    public void CatalogPublishesTypedCurveRevolveContract()
    {
        var descriptor = Assert.Single(
            RekallAgeModelingNodeCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modeling.curve.revolve" && item.TypeVersion == 1);

        var input = Assert.Single(descriptor.Ports, port => port.PortId == "curve");
        var output = Assert.Single(descriptor.Ports, port => port.PortId == "geometry");
        Assert.Equal(RekallAgeModelingPortDirection.Input, input.Direction);
        Assert.Equal(RekallAgeModelingValueType.Curve, input.ValueType);
        Assert.True(input.Required);
        Assert.Equal(RekallAgeModelingPortDirection.Output, output.Direction);
        Assert.Equal(RekallAgeModelingValueType.Geometry, output.ValueType);

        var axis = Assert.Single(descriptor.Parameters, parameter => parameter.ParameterId == "axis");
        Assert.Equal(["x", "y", "z"], axis.EnumChoices);
        Assert.Equal(RekallAgeModelingValueType.Vector3,
            Assert.Single(descriptor.Parameters, parameter => parameter.ParameterId == "origin").ValueType);
        var angle = Assert.Single(descriptor.Parameters, parameter => parameter.ParameterId == "angleDegrees");
        Assert.Equal(360, angle.Maximum);
        Assert.Equal("degree", angle.Unit);
        var segments = Assert.Single(descriptor.Parameters, parameter => parameter.ParameterId == "segments");
        Assert.Equal(3, segments.Minimum);
        Assert.Equal(4096, segments.Maximum);
        var weld = Assert.Single(descriptor.Parameters, parameter => parameter.ParameterId == "weldDistance");
        Assert.Equal(0, weld.Minimum);
        Assert.Equal(1, weld.Maximum);
        Assert.Equal("world-unit", weld.Unit);
        Assert.Contains(descriptor.Parameters, parameter => parameter.ParameterId == "materialAssetId");
        Assert.Contains(descriptor.Parameters, parameter => parameter.ParameterId == "slotName");
    }

    [Fact]
    public async Task TypedCurveRevolveEvaluatesThroughRealGraphPorts()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "curve-revolve-proof",
            "Curve Revolve Proof",
            [
                new("profile", "rekall.modeling.curve.line", 1, new JsonObject
                {
                    ["start"] = new JsonArray(1.0, -1.0, 0.0),
                    ["end"] = new JsonArray(1.0, 1.0, 0.0)
                }),
                new("revolve", "rekall.modeling.curve.revolve", 1, new JsonObject
                {
                    ["axis"] = "y",
                    ["segments"] = 8,
                    ["materialAssetId"] = "material.aged-steel",
                    ["slotName"] = "Lathed Steel"
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("profile-revolve", "profile", "curve", "revolve", "curve"),
                new("revolve-output", "revolve", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph,
            ["mesh"],
            RekallAgeModelingEvaluationBudget.Default,
            new(0, 0, "tests", "desktop"),
            CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        var mesh = result.Outputs["mesh"];
        Assert.Equal(16, mesh.Topology.PointIds.Count);
        Assert.Equal(8, mesh.Topology.FaceIds.Count);
        Assert.Contains(mesh.MaterialSlots, slot =>
            slot.MaterialAssetId == "material.aged-steel" && slot.Name == "Lathed Steel");
    }
}
