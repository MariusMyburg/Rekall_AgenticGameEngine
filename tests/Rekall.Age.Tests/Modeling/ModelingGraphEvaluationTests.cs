using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModelingGraphEvaluationTests
{
    [Fact]
    public async Task DemandEvaluationCachesNodeHashesAndParameterEditInvalidatesReachableChain()
    {
        var evaluator = new RekallAgeModelingGraphEvaluator();
        var firstGraph = Graph(sizeX: 4, revision: 1);

        var first = await evaluator.EvaluateAsync(firstGraph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);
        var cached = await evaluator.EvaluateAsync(firstGraph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);
        var changed = await evaluator.EvaluateAsync(Graph(sizeX: 8, revision: 2), ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal(2, first.EvaluatedNodeCount);
        Assert.Equal(0, first.CacheHitCount);
        Assert.Equal(0, first.InvalidatedNodeCount);
        Assert.DoesNotContain(first.Nodes, node => node.NodeId == "unused");
        Assert.Equal(-2, first.Outputs["mesh"].Topology.Positions.Min(position => position.X));
        Assert.Equal(2, first.Outputs["mesh"].Topology.Positions.Max(position => position.X));
        Assert.Equal(2, cached.CacheHitCount);
        Assert.Equal(0, cached.InvalidatedNodeCount);
        Assert.Equal(2, changed.InvalidatedNodeCount);
        Assert.Equal(-4, changed.Outputs["mesh"].Topology.Positions.Min(position => position.X));
        Assert.Equal(4, changed.Outputs["mesh"].Topology.Positions.Max(position => position.X));
        Assert.All(changed.Nodes, node => Assert.Matches("^[0-9a-f]{64}$", node.CacheKey));
    }

    [Fact]
    public async Task BudgetFailureReturnsDiagnosticsAndPreservesLastGoodOutput()
    {
        var evaluator = new RekallAgeModelingGraphEvaluator();
        var graph = Graph(sizeX: 4, revision: 1);
        var good = await evaluator.EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        var failed = await evaluator.EvaluateAsync(
            graph,
            ["mesh"],
            RekallAgeModelingEvaluationBudget.Default with { MaximumPoints = 4 },
            EvaluationContext() with { TargetProfile = "budget-proof" },
            CancellationToken.None);

        Assert.True(good.Succeeded);
        Assert.False(failed.Succeeded);
        Assert.True(failed.RetainedLastGoodOutputs);
        Assert.Equal(good.Outputs["mesh"].Topology.Positions, failed.Outputs["mesh"].Topology.Positions);
        Assert.Contains(failed.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODELING_EVALUATION_POINT_BUDGET_EXCEEDED");
    }

    [Fact]
    public async Task TransformNodeAppliesScaleRotationAndTranslationWithoutMutatingInput()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "transform-graph",
            "Transform Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = 2.0, ["sizeY"] = 2.0, ["sizeZ"] = 2.0 }),
                new("move", "rekall.modeling.transform", 1, new JsonObject
                {
                    ["translation"] = new JsonArray(3.0, 0.0, 0.0),
                    ["rotation"] = new JsonArray(0.0, 0.0, 90.0),
                    ["scale"] = new JsonArray(2.0, 1.0, 1.0)
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-move", "box", "geometry", "move", "geometry"),
                new("move-output", "move", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var positions = result.Outputs["mesh"].Topology.Positions;
        Assert.Equal(2, positions.Min(position => position.X), 6);
        Assert.Equal(4, positions.Max(position => position.X), 6);
        Assert.Equal(-2, positions.Min(position => position.Y), 6);
        Assert.Equal(2, positions.Max(position => position.Y), 6);
    }

    [Fact]
    public async Task GridExtrudeAndTriangulateReuseSemanticMeshOperations()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "operation-graph",
            "Operation Graph",
            [
                new("grid", "rekall.modeling.primitive.grid", 1, new JsonObject
                {
                    ["sizeX"] = 4.0, ["sizeY"] = 2.0, ["segmentsX"] = 1, ["segmentsY"] = 1
                }),
                new("extrude", "rekall.modeling.extrude", 1, new JsonObject { ["offset"] = new JsonArray(0.0, 0.0, 2.0) }),
                new("triangulate", "rekall.modeling.triangulate", 1, new JsonObject()),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("grid-extrude", "grid", "geometry", "extrude", "geometry"),
                new("extrude-triangulate", "extrude", "geometry", "triangulate", "geometry"),
                new("triangulate-output", "triangulate", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var mesh = result.Outputs["mesh"];
        Assert.Equal(8, mesh.Topology.PointIds.Count);
        Assert.Equal(10, mesh.Topology.FaceIds.Count);
        Assert.All(Enumerable.Range(0, mesh.Topology.FaceIds.Count), faceIndex =>
            Assert.Equal(3, mesh.Topology.FaceOffsets[faceIndex + 1] - mesh.Topology.FaceOffsets[faceIndex]));
        Assert.Equal(0, mesh.Topology.Positions.Min(position => position.Z));
        Assert.Equal(2, mesh.Topology.Positions.Max(position => position.Z));
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
    }

    [Fact]
    public async Task SphereAndJoinProduceValidatedCombinedGeometry()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "join-graph",
            "Join Graph",
            [
                new("sphere", "rekall.modeling.primitive.sphere", 1, new JsonObject { ["radius"] = 1.0, ["segments"] = 8, ["rings"] = 4 }),
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("move", "rekall.modeling.transform", 1, new JsonObject { ["translation"] = new JsonArray(3.0, 0.0, 0.0) }),
                new("join", "rekall.modeling.join", 1, new JsonObject()),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-move", "box", "geometry", "move", "geometry"),
                new("sphere-join", "sphere", "geometry", "join", "geometry"),
                new("move-join", "move", "geometry", "join", "geometry"),
                new("join-output", "join", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var mesh = result.Outputs["mesh"];
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
        Assert.Equal(54, mesh.Topology.FaceIds.Count);
        Assert.Equal(-1, mesh.Topology.Positions.Min(position => position.X), 6);
        Assert.Equal(3.5, mesh.Topology.Positions.Max(position => position.X), 6);
        Assert.Equal(mesh.Topology.PointIds.Count, mesh.Topology.PointIds.Distinct().Count());
        Assert.Equal(mesh.Topology.FaceIds.Count, mesh.Topology.FaceIds.Distinct().Count());
    }

    [Theory]
    [InlineData(1.0, 16, 48, 10)]
    [InlineData(0.0, 9, 32, 9)]
    public async Task FrustumCreatesValidatedCylinderOrTrueApexCone(
        double radiusTop,
        int expectedPoints,
        int expectedCorners,
        int expectedFaces)
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "frustum-graph",
            "Frustum Graph",
            [
                new("shape", "rekall.modeling.primitive.frustum", 1, new JsonObject
                {
                    ["radiusBottom"] = 1.0,
                    ["radiusTop"] = radiusTop,
                    ["depth"] = 2.0,
                    ["segments"] = 8,
                    ["capBottom"] = true,
                    ["capTop"] = true
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("shape-output", "shape", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var mesh = result.Outputs["mesh"];
        Assert.Equal(expectedPoints, mesh.Topology.PointIds.Count);
        Assert.Equal(expectedCorners, mesh.Topology.CornerIds.Count);
        Assert.Equal(expectedFaces, mesh.Topology.FaceIds.Count);
        Assert.Equal(expectedPoints, mesh.Topology.Positions.Distinct().Count());
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
    }

    [Fact]
    public async Task FrustumRejectsTwoZeroRadiusRingsWithStableDiagnostic()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "degenerate-frustum",
            "Degenerate Frustum",
            [
                new("shape", "rekall.modeling.primitive.frustum", 1, new JsonObject
                {
                    ["radiusBottom"] = 0.0, ["radiusTop"] = 0.0
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("shape-output", "shape", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_MODELING_EVALUATION_PARAMETER_INVALID" && item.NodeId == "shape");
    }

    [Fact]
    public async Task FieldMathCapturedAndNamedAttributesAndMaterialAssignmentAreExecutable()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "attribute-graph",
            "Attribute Graph",
            [
                new("grid", "rekall.modeling.primitive.grid", 1, new JsonObject()),
                new("constant-math", "rekall.modeling.field.math", 1, new JsonObject { ["operation"] = "multiply", ["a"] = 0.25, ["b"] = 2.0 }),
                new("capture", "rekall.modeling.attribute.capture", 1, new JsonObject { ["name"] = "weight", ["domain"] = "point" }),
                new("named", "rekall.modeling.attribute.named", 1, new JsonObject { ["name"] = "weight" }),
                new("add", "rekall.modeling.field.math", 1, new JsonObject { ["operation"] = "add", ["b"] = 0.5 }),
                new("capture-final", "rekall.modeling.attribute.capture", 1, new JsonObject { ["name"] = "weight.final", ["domain"] = "point" }),
                new("material", "rekall.modeling.material.assign", 1, new JsonObject { ["materialAssetId"] = "mat.stone", ["slotName"] = "Stone" }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("grid-capture", "grid", "geometry", "capture", "geometry"),
                new("constant-capture", "constant-math", "value", "capture", "value"),
                new("capture-named", "capture", "geometry", "named", "geometry"),
                new("named-add", "named", "value", "add", "a"),
                new("capture-final-geometry", "capture", "geometry", "capture-final", "geometry"),
                new("add-capture-final", "add", "value", "capture-final", "value"),
                new("capture-material", "capture-final", "geometry", "material", "geometry"),
                new("material-output", "material", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var mesh = result.Outputs["mesh"];
        var weight = Assert.Single(mesh.Attributes, attribute => attribute.Name == "weight");
        var finalWeight = Assert.Single(mesh.Attributes, attribute => attribute.Name == "weight.final");
        Assert.All(weight.Values, value => Assert.Equal(0.5, value.GetDouble()));
        Assert.All(finalWeight.Values, value => Assert.Equal(1.0, value.GetDouble()));
        Assert.Equal("mat.stone", Assert.Single(mesh.MaterialSlots).MaterialAssetId);
        var materialIndices = Assert.Single(mesh.Attributes, attribute => attribute.Semantic == "material-index");
        Assert.All(materialIndices.Values, value => Assert.Equal(0, value.GetInt32()));
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
    }

    private static RekallAgeModelingGraphAsset Graph(double sizeX, long revision)
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "box-graph",
            "Box Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = sizeX, ["sizeY"] = 2.0, ["sizeZ"] = 3.0 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject()),
                new("unused", "rekall.modeling.primitive.sphere", 1, new JsonObject())
            ],
            [new("box-output", "box", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);
        return graph with { Revision = revision };
    }

    private static RekallAgeModelingEvaluationContext EvaluationContext() =>
        new(Seed: 42, DeterministicTime: 0, EngineVersion: "test-engine", TargetProfile: "desktop");
}
