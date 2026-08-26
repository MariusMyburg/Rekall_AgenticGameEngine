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
    public async Task NoiseDeformProducesDeterministicInspectableTerrainBreakup()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "terrain-noise", "Terrain Noise",
            [
                new("grid", "rekall.modeling.primitive.grid", 1, new JsonObject
                {
                    ["sizeX"] = 8.0, ["sizeY"] = 8.0, ["segmentsX"] = 8, ["segmentsY"] = 8
                }),
                new("noise", "rekall.modeling.deform.noise", 1, new JsonObject
                {
                    ["amplitude"] = 1.25, ["frequency"] = 0.42, ["seed"] = 417, ["axis"] = "z"
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("grid-noise", "grid", "geometry", "noise", "geometry"),
                new("noise-output", "noise", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var evaluator = new RekallAgeModelingGraphEvaluator();
        var first = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);
        var second = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        Assert.Contains(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modeling.deform.noise");
        var heights = first.Outputs["mesh"].Topology.Positions.Select(position => position.Z).ToArray();
        Assert.True(heights.Max() - heights.Min() > 0.25);
        Assert.Equal(heights, second.Outputs["mesh"].Topology.Positions.Select(position => position.Z));
    }

    [Fact]
    public async Task ScatterAreaCreatesDeterministicVariedInspectableInstances()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "scatter-area", "Scatter Area",
            [
                new("rock", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("scatter", "rekall.modeling.scatter.area", 1, new JsonObject
                {
                    ["count"] = 7, ["sizeX"] = 12.0, ["sizeZ"] = 8.0, ["seed"] = 913,
                    ["minimumScale"] = 0.45, ["maximumScale"] = 1.2,
                    ["minimumYaw"] = -35.0, ["maximumYaw"] = 35.0
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("rock-scatter", "rock", "geometry", "scatter", "geometry"),
                new("scatter-output", "scatter", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var first = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);
        var second = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        Assert.Contains(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modeling.scatter.area");
        Assert.Equal(56, first.Outputs["mesh"].Topology.PointIds.Count);
        Assert.Equal(first.Outputs["mesh"].Topology.Positions, second.Outputs["mesh"].Topology.Positions);
        Assert.True(first.Outputs["mesh"].Topology.Positions.Max(point => point.X)
            - first.Outputs["mesh"].Topology.Positions.Min(point => point.X) > 5);
        Assert.True(first.Outputs["mesh"].Topology.Positions.Max(point => point.Z)
            - first.Outputs["mesh"].Topology.Positions.Min(point => point.Z) > 3);
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
        Assert.Equal(38, mesh.Topology.FaceIds.Count);
        Assert.Equal(-1, mesh.Topology.Positions.Min(position => position.X), 6);
        Assert.Equal(3.5, mesh.Topology.Positions.Max(position => position.X), 6);
        Assert.Equal(mesh.Topology.PointIds.Count, mesh.Topology.PointIds.Distinct().Count());
        Assert.Equal(mesh.Topology.FaceIds.Count, mesh.Topology.FaceIds.Distinct().Count());
    }

    [Fact]
    public async Task SphereCreatesClosedManifoldWithoutSeamOrPoleDuplicates()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "sphere-graph",
            "Sphere Graph",
            [
                new("sphere", "rekall.modeling.primitive.sphere", 1, new JsonObject
                {
                    ["radius"] = 1.0,
                    ["segments"] = 8,
                    ["rings"] = 4
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("sphere-output", "sphere", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var validation = new RekallAgeMeshValidator().Validate(result.Outputs["mesh"]);
        Assert.True(validation.IsValid);
        Assert.Equal(26, validation.Summary.PointCount);
        Assert.Equal(56, validation.Summary.EdgeCount);
        Assert.Equal(32, validation.Summary.FaceCount);
        Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
        Assert.Equal(0, validation.Summary.NonManifoldEdgeCount);
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
    public async Task TorusCreatesClosedPeriodicSharedTopologyWithoutSeamDuplicates()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "torus-graph",
            "Torus Graph",
            [
                new("shape", "rekall.modeling.primitive.torus", 1, new JsonObject
                {
                    ["majorRadius"] = 2.0, ["minorRadius"] = 0.5,
                    ["majorSegments"] = 8, ["minorSegments"] = 4
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("shape-output", "shape", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var mesh = result.Outputs["mesh"];
        Assert.Equal(32, mesh.Topology.PointIds.Count);
        Assert.Equal(64, mesh.Topology.EdgeIds.Count);
        Assert.Equal(32, mesh.Topology.FaceIds.Count);
        Assert.Equal(128, mesh.Topology.CornerIds.Count);
        Assert.Equal(32, mesh.Topology.Positions.Distinct().Count());
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        Assert.True(validation.IsValid);
        Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
        Assert.Equal(0, validation.Summary.NonManifoldEdgeCount);
    }

    [Fact]
    public async Task TorusRejectsSelfIntersectingRadiusPairWithStableDiagnostic()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "invalid-torus", "Invalid Torus",
            [
                new("shape", "rekall.modeling.primitive.torus", 1, new JsonObject { ["majorRadius"] = 1.0, ["minorRadius"] = 1.0 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("shape-output", "shape", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_MODELING_EVALUATION_PARAMETER_INVALID" && item.NodeId == "shape");
    }

    [Theory]
    [InlineData("union", -1.0, 2.0)]
    [InlineData("intersect", 0.0, 1.0)]
    [InlineData("difference", -1.0, 0.0)]
    public async Task BooleanCombinesOverlappingClosedMeshesIntoValidatedClosedTopology(
        string operation,
        double expectedMinX,
        double expectedMaxX)
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "boolean-graph", "Boolean Graph",
            [
                new("a", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = 2.0, ["sizeY"] = 2.0, ["sizeZ"] = 2.0 }),
                new("b", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = 2.0, ["sizeY"] = 2.0, ["sizeZ"] = 2.0 }),
                new("move-b", "rekall.modeling.transform", 1, new JsonObject { ["translation"] = new JsonArray(1.0, 0.0, 0.0) }),
                new("boolean", "rekall.modeling.boolean", 1, new JsonObject { ["operation"] = operation }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("b-move", "b", "geometry", "move-b", "geometry"),
                new("a-boolean", "a", "geometry", "boolean", "a"),
                new("b-boolean", "move-b", "geometry", "boolean", "b"),
                new("boolean-output", "boolean", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        var validation = new RekallAgeMeshValidator().Validate(result.Outputs["mesh"]);
        Assert.True(validation.IsValid);
        Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
        Assert.Equal(0, validation.Summary.NonManifoldEdgeCount);
        Assert.Equal(expectedMinX, validation.Summary.Bounds.Min.X, 6);
        Assert.Equal(expectedMaxX, validation.Summary.Bounds.Max.X, 6);
        var sourceOperands = Assert.Single(result.Outputs["mesh"].Attributes, item => item.Name == "boolean.sourceOperand");
        var sourceFaces = Assert.Single(result.Outputs["mesh"].Attributes, item => item.Name == "boolean.sourceFaceId");
        Assert.Equal(validation.Summary.FaceCount, sourceOperands.Values.Count);
        Assert.Equal(validation.Summary.FaceCount, sourceFaces.Values.Count);
        Assert.All(sourceOperands.Values, value => Assert.Contains(value.GetString(), new[] { "a", "b" }));
        Assert.All(sourceFaces.Values, value => Assert.True(ulong.TryParse(value.GetString(), out _)));
    }

    [Fact]
    public async Task BooleanHandlesNonCoplanarRotatedClosedInputs()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "rotated-boolean", "Rotated Boolean",
            [
                new("a", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("b", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("move-b", "rekall.modeling.transform", 1, new JsonObject
                {
                    ["translation"] = new JsonArray(0.35, 0.0, 0.0), ["rotation"] = new JsonArray(20.0, 35.0, 10.0)
                }),
                new("boolean", "rekall.modeling.boolean", 1, new JsonObject { ["operation"] = "union" }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("b-move", "b", "geometry", "move-b", "geometry"),
                new("a-boolean", "a", "geometry", "boolean", "a"),
                new("b-boolean", "move-b", "geometry", "boolean", "b"),
                new("boolean-output", "boolean", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        var validation = new RekallAgeMeshValidator().Validate(result.Outputs["mesh"]);
        Assert.True(validation.IsValid);
        Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
        Assert.Equal(0, validation.Summary.NonManifoldEdgeCount);
        Assert.True(validation.Summary.PointCount > 8);
    }

    [Fact]
    public async Task BooleanRejectsOpenSurfaceInputWithStableDiagnostic()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "open-boolean", "Open Boolean",
            [
                new("open", "rekall.modeling.primitive.grid", 1, new JsonObject()),
                new("closed", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("boolean", "rekall.modeling.boolean", 1, new JsonObject()),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("open-boolean", "open", "geometry", "boolean", "a"),
                new("closed-boolean", "closed", "geometry", "boolean", "b"),
                new("boolean-output", "boolean", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_MODELING_BOOLEAN_INPUT_NOT_CLOSED_MANIFOLD" && item.NodeId == "boolean");
    }

    [Fact]
    public async Task BooleanFailsClosedOnOneSidedMaterialSchema()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "attributed-boolean", "Attributed Boolean",
            [
                new("a", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("material", "rekall.modeling.material.assign", 1, new JsonObject { ["materialAssetId"] = "mat.stone", ["slotName"] = "Stone" }),
                new("b", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("boolean", "rekall.modeling.boolean", 1, new JsonObject()),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("a-material", "a", "geometry", "material", "geometry"),
                new("material-boolean", "material", "geometry", "boolean", "a"),
                new("b-boolean", "b", "geometry", "boolean", "b"),
                new("boolean-output", "boolean", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_MODELING_BOOLEAN_MATERIAL_SCHEMA_MISMATCH" && item.NodeId == "boolean");
    }

    [Fact]
    public async Task BooleanRejectsPointAttributesUntilVertexInterpolationIsAvailable()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "point-attribute-boolean", "Point Attribute Boolean",
            [
                new("a", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("value", "rekall.modeling.field.math", 1, new JsonObject { ["operation"] = "add", ["a"] = 0.25, ["b"] = 0.25 }),
                new("capture", "rekall.modeling.attribute.capture", 1, new JsonObject { ["name"] = "weight", ["domain"] = "point" }),
                new("b", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("boolean", "rekall.modeling.boolean", 1, new JsonObject()),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("a-capture", "a", "geometry", "capture", "geometry"),
                new("value-capture", "value", "value", "capture", "value"),
                new("capture-boolean", "capture", "geometry", "boolean", "a"),
                new("b-boolean", "b", "geometry", "boolean", "b"),
                new("boolean-output", "boolean", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_MODELING_BOOLEAN_ATTRIBUTES_UNSUPPORTED" && item.NodeId == "boolean");
    }

    [Fact]
    public async Task BooleanPreservesAndRemapsCompatibleFaceMaterialsFromBothOperands()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "material-boolean", "Material Boolean",
            [
                new("a", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("mat-a", "rekall.modeling.material.assign", 1, new JsonObject { ["materialAssetId"] = "mat.stone", ["slotName"] = "Stone" }),
                new("b", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("move-b", "rekall.modeling.transform", 1, new JsonObject { ["translation"] = new JsonArray(0.5, 0.0, 0.0) }),
                new("mat-b", "rekall.modeling.material.assign", 1, new JsonObject { ["materialAssetId"] = "mat.metal", ["slotName"] = "Metal" }),
                new("boolean", "rekall.modeling.boolean", 1, new JsonObject()),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("a-material", "a", "geometry", "mat-a", "geometry"),
                new("b-move", "b", "geometry", "move-b", "geometry"),
                new("b-material", "move-b", "geometry", "mat-b", "geometry"),
                new("a-boolean", "mat-a", "geometry", "boolean", "a"),
                new("b-boolean", "mat-b", "geometry", "boolean", "b"),
                new("boolean-output", "boolean", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        var mesh = result.Outputs["mesh"];
        Assert.Equal(["mat.stone", "mat.metal"], mesh.MaterialSlots.Select(slot => slot.MaterialAssetId));
        var materialIndices = Assert.Single(mesh.Attributes, item => item.Semantic == "material-index");
        Assert.Contains(materialIndices.Values, value => value.GetInt32() == 0);
        Assert.Contains(materialIndices.Values, value => value.GetInt32() == 1);
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
    }

    [Fact]
    public async Task BooleanInterpolatesCompatibleCornerUvsAtSplitVertices()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "uv-boolean", "UV Boolean",
            [
                new("a", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("uv-a", "rekall.modeling.project_uv", 1, new JsonObject { ["attribute"] = "uv.generated", ["axis"] = "xy" }),
                new("b", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("move-b", "rekall.modeling.transform", 1, new JsonObject { ["translation"] = new JsonArray(0.5, 0.0, 0.0), ["rotation"] = new JsonArray(0.0, 25.0, 0.0) }),
                new("uv-b", "rekall.modeling.project_uv", 1, new JsonObject { ["attribute"] = "uv.generated", ["axis"] = "xy" }),
                new("boolean", "rekall.modeling.boolean", 1, new JsonObject()),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("a-uv", "a", "geometry", "uv-a", "geometry"),
                new("b-move", "b", "geometry", "move-b", "geometry"),
                new("move-uv", "move-b", "geometry", "uv-b", "geometry"),
                new("a-boolean", "uv-a", "geometry", "boolean", "a"),
                new("b-boolean", "uv-b", "geometry", "boolean", "b"),
                new("boolean-output", "boolean", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        var mesh = result.Outputs["mesh"];
        var uv = Assert.Single(mesh.Attributes, item => item.Name == "uv.generated");
        Assert.Equal(RekallAgeGeometryDomain.Corner, uv.Domain);
        Assert.Equal(mesh.Topology.CornerIds.Count, uv.Values.Count);
        Assert.All(uv.Values, value => Assert.All(value.EnumerateArray(), component => Assert.True(double.IsFinite(component.GetDouble()))));
        Assert.True(uv.Values.Select(value => value.GetRawText()).Distinct().Count() > 4);
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
    }

    [Fact]
    public async Task BooleanOutputCanFeedAnotherBooleanWithoutLosingCurrentNodeProvenance()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "boolean-chain", "Boolean Chain",
            [
                new("a", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("b", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("move-b", "rekall.modeling.transform", 1, new JsonObject { ["translation"] = new JsonArray(0.5, 0.0, 0.0) }),
                new("c", "rekall.modeling.primitive.frustum", 1, new JsonObject { ["radiusBottom"] = 0.2, ["radiusTop"] = 0.2, ["depth"] = 2.0, ["segments"] = 12 }),
                new("union", "rekall.modeling.boolean", 1, new JsonObject { ["operation"] = "union" }),
                new("cut", "rekall.modeling.boolean", 1, new JsonObject { ["operation"] = "difference" }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("b-move", "b", "geometry", "move-b", "geometry"),
                new("a-union", "a", "geometry", "union", "a"),
                new("b-union", "move-b", "geometry", "union", "b"),
                new("union-cut", "union", "geometry", "cut", "a"),
                new("c-cut", "c", "geometry", "cut", "b"),
                new("cut-output", "cut", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        var mesh = result.Outputs["mesh"];
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        Assert.True(validation.IsValid);
        Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
        Assert.All(Assert.Single(mesh.Attributes, item => item.Name == "boolean.sourceOperand").Values,
            value => Assert.Contains(value.GetString(), new[] { "a", "b" }));
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
