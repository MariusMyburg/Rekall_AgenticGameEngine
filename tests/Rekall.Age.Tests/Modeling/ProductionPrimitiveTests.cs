using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class ProductionPrimitiveTests
{
    public static TheoryData<string, JsonObject, int, int, bool> Cases => new()
    {
        { "rekall.modeling.primitive.plane", new() { ["sizeX"] = 2, ["sizeY"] = 3 }, 4, 1, false },
        { "rekall.modeling.primitive.disc", new() { ["radius"] = 1, ["segments"] = 8 }, 9, 8, false },
        { "rekall.modeling.primitive.cylinder", new() { ["radius"] = 1, ["depth"] = 2, ["segments"] = 8 }, 16, 10, true },
        { "rekall.modeling.primitive.cone", new() { ["radius"] = 1, ["depth"] = 2, ["segments"] = 8 }, 9, 9, true },
        { "rekall.modeling.primitive.ico_sphere", new() { ["radius"] = 1, ["subdivisions"] = 0 }, 12, 20, true },
        { "rekall.modeling.primitive.capsule", new() { ["radius"] = 0.5, ["depth"] = 2, ["segments"] = 8, ["hemisphereRings"] = 4 }, 66, 72, true }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PrimitiveHasDeterministicProductionTopology(string typeId, JsonObject parameters, int points, int faces, bool closed)
    {
        var graph = RekallAgeModelingGraphAsset.Create("primitive-proof", "Primitive Proof",
            [new("primitive", typeId, 1, parameters), new("output", "rekall.modeling.output.mesh", 1, new())],
            [new("out", "primitive", "geometry", "output", "input")], [new("mesh", "output", "geometry")]);
        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(3, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var mesh = report.Outputs["mesh"];
        Assert.Equal(points, mesh.Topology.PointIds.Count);
        Assert.Equal(faces, mesh.Topology.FaceIds.Count);
        Assert.All(mesh.Topology.Positions, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z)));
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        Assert.True(validation.IsValid);
        if (closed) Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
        else Assert.True(validation.Summary.BoundaryEdgeCount > 0);
        Assert.Contains(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors, item => item.TypeId == typeId);
    }
}
