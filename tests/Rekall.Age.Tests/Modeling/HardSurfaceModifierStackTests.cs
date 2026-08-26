using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class HardSurfaceModifierStackTests
{
    [Fact]
    public async Task BevelModifierConsumesANamedPartialEdgeSelection()
    {
        var source = await Primitive("rekall.modeling.primitive.box");
        source = source with
        {
            SelectionSets =
            [
                new("hero-edges", RekallAgeGeometryDomain.Edge, [source.Topology.EdgeIds[0]])
            ]
        };
        var stack = Stack(
        [
            new("bevel", "rekall.modifier.bevel", 1, true, new()
            {
                ["width"] = 0.08,
                ["segments"] = 2,
                ["selection"] = "hero-edges"
            })
        ]);

        var result = await new RekallAgeModifierStackEvaluator().EvaluateAsync(
            stack, source, RekallAgeModelingEvaluationBudget.Default, default);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.True(result.Mesh!.Topology.FaceIds.Count > source.Topology.FaceIds.Count);
    }

    [Fact]
    public async Task BevelAndWeightedNormalsExecuteWithPreservedAttributes()
    {
        var source = await Primitive("rekall.modeling.primitive.box");
        var stack = Stack([
            new("bevel", "rekall.modifier.bevel", 1, true, new() { ["width"] = 0.08, ["segments"] = 2, ["profile"] = 0.5, ["clampOverlap"] = true }),
            new("normals", "rekall.modifier.weighted_normals", 1, true, new() { ["attribute"] = "normal.weighted", ["faceAreaWeight"] = 1.0 })]);

        var result = await new RekallAgeModifierStackEvaluator().EvaluateAsync(stack, source, RekallAgeModelingEvaluationBudget.Default, default);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.True(result.Mesh!.Topology.FaceIds.Count > source.Topology.FaceIds.Count);
        Assert.Contains(result.Mesh.Attributes, item => item.Name == "normal.weighted" && item.Domain == RekallAgeGeometryDomain.Corner);
    }

    [Fact]
    public async Task AutoSmoothAndWeightedNormalModifiersShareTheSemanticPolicyContract()
    {
        var source = await Primitive("rekall.modeling.primitive.box");
        var stack = Stack([
            new("smooth", "rekall.modifier.auto_smooth", 1, true, new() { ["angleDegrees"] = 89.0, ["sharpAttribute"] = "normal.sharp" }),
            new("normals", "rekall.modifier.weighted_normals", 1, true, new()
            {
                ["attribute"] = "normal.authored",
                ["faceAreaWeight"] = 1.0,
                ["cornerAngleWeight"] = 1.0,
                ["smoothAttribute"] = "normal.smooth",
                ["sharpAttribute"] = "normal.sharp"
            })]);

        var result = await new RekallAgeModifierStackEvaluator().EvaluateAsync(
            stack,
            source,
            RekallAgeModelingEvaluationBudget.Default,
            default);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        var sharp = Assert.Single(result.Mesh!.Attributes, item => item.Name == "normal.sharp");
        Assert.All(sharp.Values, value => Assert.True(value.GetBoolean()));
        var normals = Assert.Single(result.Mesh.Attributes, item => item.Name == "normal.authored");
        Assert.All(normals.Values, value =>
        {
            var length = Math.Sqrt(
                Math.Pow(value[0].GetDouble(), 2)
                + Math.Pow(value[1].GetDouble(), 2)
                + Math.Pow(value[2].GetDouble(), 2));
            Assert.InRange(length, 0.999999, 1.000001);
        });
    }

    [Fact]
    public async Task SolidifyMirrorAndArrayExecuteAsAnOrderedStack()
    {
        var source = await Primitive("rekall.modeling.primitive.plane");
        var stack = Stack([
            new("solid", "rekall.modifier.solidify", 1, true, new() { ["thickness"] = 0.1, ["rim"] = true }),
            new("mirror", "rekall.modifier.mirror", 1, true, new() { ["axis"] = "x", ["origin"] = 1.0, ["mergeDistance"] = 0.0, ["bisect"] = false }),
            new("array", "rekall.modifier.array", 1, true, new() { ["count"] = 3, ["x"] = 0.0, ["y"] = 0.0, ["z"] = 2.0, ["relativeOffset"] = false, ["instanceMode"] = false })]);

        var result = await new RekallAgeModifierStackEvaluator().EvaluateAsync(stack, source, RekallAgeModelingEvaluationBudget.Default, default);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(48, result.Mesh!.Topology.PointIds.Count);
        Assert.Equal(36, result.Mesh.Topology.FaceIds.Count);
    }

    [Theory]
    [InlineData("rekall.modifier.mirror", "bisect", "REKALL_MODELING_MIRROR_BISECT_UNSUPPORTED")]
    [InlineData("rekall.modifier.array", "instanceMode", "REKALL_MODELING_ARRAY_INSTANCE_UNSUPPORTED")]
    public async Task UnsupportedModesEmitExplicitDiagnostics(string typeId, string parameter, string code)
    {
        var source = await Primitive("rekall.modeling.primitive.box");
        var result = await new RekallAgeModifierStackEvaluator().EvaluateAsync(Stack([new("unsupported", typeId, 1, true, new() { [parameter] = true })]), source, RekallAgeModelingEvaluationBudget.Default, default);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == code);
    }

    private static RekallAgeModifierStackAsset Stack(IReadOnlyList<RekallAgeModifierInstance> modifiers) =>
        RekallAgeModifierStackAsset.Create("hard-surface-stack", "Hard Surface Stack", "source", new string('a', 64), modifiers);

    private static async ValueTask<RekallAgeMeshAsset> Primitive(string typeId)
    {
        var graph = RekallAgeModelingGraphAsset.Create("source", "Source", [new("source", typeId, 1, new())], [], [new("mesh", "source", "geometry")]);
        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "tests", "desktop"), default);
        return result.Outputs["mesh"];
    }
}
