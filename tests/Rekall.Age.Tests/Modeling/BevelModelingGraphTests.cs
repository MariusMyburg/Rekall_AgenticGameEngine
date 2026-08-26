using System.Text.Json.Nodes;
using System.Text.Json;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class BevelModelingGraphTests
{
    [Fact]
    public async Task AngleSelectionDrivesAPartialBevelThroughTheModelingGraph()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "angle-bevel-proof", "Angle Bevel Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject
                {
                    ["sizeX"] = 2.0, ["sizeY"] = 2.0, ["sizeZ"] = 2.0
                }),
                new("triangulate", "rekall.modeling.triangulate", 1, new JsonObject()),
                new("select", "rekall.modeling.selection.edge_angle", 1, new JsonObject
                {
                    ["name"] = "cap-rims",
                    ["minimumAngleDegrees"] = 80,
                    ["maximumAngleDegrees"] = 100
                }),
                new("bevel", "rekall.modeling.bevel", 1, new JsonObject
                {
                    ["selectionSet"] = "cap-rims",
                    ["width"] = 0.08,
                    ["segments"] = 2,
                    ["profile"] = 0.5
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-triangulate", "box", "geometry", "triangulate", "geometry"),
                new("triangulate-select", "triangulate", "geometry", "select", "geometry"),
                new("select-bevel", "select", "geometry", "bevel", "geometry"),
                new("bevel-output", "bevel", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var selected = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph with { Outputs = [new("selected", "select", "geometry")] },
            ["selected"],
            RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"),
            CancellationToken.None);
        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(selected.Succeeded, string.Join(Environment.NewLine, selected.Diagnostics.Select(item => item.Message)));
        var selection = Assert.Single(selected.Outputs["selected"].SelectionSets, item => item.Name == "cap-rims");
        Assert.NotEmpty(selection.ElementIds);
        Assert.True(selection.ElementIds.Count < selected.Outputs["selected"].Topology.EdgeIds.Count);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.True(result.Outputs["mesh"].Topology.FaceIds.Count > selected.Outputs["selected"].Topology.FaceIds.Count);
    }

    [Fact]
    public async Task BevelExecutorRoundsAnArbitraryManifoldEdgeSubset()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "subset-bevel", "Subset Bevel", CancellationToken.None);
        source = source with
        {
            Attributes = source.Attributes
                .Where(item => item.Name != "normal.smooth")
                .Append(new RekallAgeGeometryAttribute(
                    "normal.smooth",
                    RekallAgeGeometryDomain.Face,
                    RekallAgeGeometryValueType.Bool,
                    source.Topology.FaceIds.Select(_ => JsonSerializer.SerializeToElement(false)).ToArray(),
                    "smooth-shading",
                    RekallAgeGeometryInterpolation.Nearest,
                    JsonSerializer.SerializeToElement(false)))
                .ToArray()
        };
        var selectedEdge = source.Topology.EdgeIds[0];

        var result = new RekallAgeMeshOperationExecutor().Execute(
            source,
            new("bevel_edges", RekallAgeGeometryDomain.Edge, [selectedEdge], new JsonObject
            {
                ["width"] = 0.12,
                ["segments"] = 2,
                ["profile"] = 0.5,
                ["clampOverlap"] = true,
                ["hardenNormals"] = true
            }));

        Assert.True(result.Validation.IsValid);
        Assert.True(result.Mesh.Topology.FaceIds.Count > source.Topology.FaceIds.Count);
        Assert.Contains(result.Provenance, item =>
            item.Domain == RekallAgeGeometryDomain.Edge
            && item.InputElementId == selectedEdge
            && item.OutputElementIds.Count > 1);
        AssertConsistentClosedWinding(result.Mesh);
        Assert.Equal(
            "smooth-shading",
            Assert.Single(result.Mesh.Attributes, item => item.Name == "normal.smooth").Semantic);
    }

    [Fact]
    public async Task HardenedMaterialBevelFromAttributeLessMeshReportsAttributesAndKeepsImplicitSmoothDefault()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "attribute-bevel", "Attribute Bevel", CancellationToken.None);
        source = source with
        {
            Attributes = [],
            MaterialSlots = [new("body", "material.body")]
        };

        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("bevel_edges", RekallAgeGeometryDomain.Edge, [source.Topology.EdgeIds[0]], new JsonObject
            {
                ["width"] = 0.08,
                ["segments"] = 2,
                ["hardenNormals"] = true,
                ["materialIndex"] = 0
            }));

        Assert.True(result.Changes.Kind.HasFlag(RekallAgeMeshChangeKind.Attributes));
        Assert.Contains(result.Mesh.Attributes, item => item.Name == "material.index");
        var smooth = Assert.Single(result.Mesh.Attributes, item => item.Name == "normal.smooth");
        Assert.True(smooth.DefaultValue!.Value.GetBoolean());
        Assert.Contains(smooth.Values, value => value.GetBoolean());
        Assert.Contains(smooth.Values, value => !value.GetBoolean());
    }

    [Fact]
    public async Task HardenNormalsRejectsAnIncompatibleSmoothAttributeWithoutReplacingIt()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "smooth-collision", "Smooth Collision", CancellationToken.None);
        source = source with
        {
            Attributes =
            [
                new("normal.smooth", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float,
                    source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(0.5)).ToArray(),
                    "custom-data", RekallAgeGeometryInterpolation.Linear)
            ]
        };

        var error = Assert.Throws<RekallAgeMeshOperationException>(() =>
            new RekallAgeMeshOperationExecutor().Execute(source,
                new("bevel_edges", RekallAgeGeometryDomain.Edge, [source.Topology.EdgeIds[0]], new JsonObject
                {
                    ["width"] = 0.08,
                    ["hardenNormals"] = true
                })));

        Assert.Equal("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", error.Code);
        Assert.Single(source.Attributes);
        Assert.Equal(RekallAgeGeometryDomain.Point, source.Attributes[0].Domain);
    }

    [Fact]
    public async Task RepresentativeBoxEdgeSubsetsRemainClosedAndConsistentlyWound()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "winding-bevel", "Winding Bevel", CancellationToken.None);
        var subsets = new[]
        {
            source.Topology.EdgeIds.Take(1).ToArray(),
            source.Topology.EdgeIds.Take(2).ToArray(),
            new[] { source.Topology.EdgeIds[0], source.Topology.EdgeIds[4] },
            source.Topology.EdgeIds.ToArray()
        };

        foreach (var subset in subsets)
        {
            var result = new RekallAgeMeshOperationExecutor().Execute(source,
                new("bevel_edges", RekallAgeGeometryDomain.Edge, subset, new JsonObject
                {
                    ["width"] = 0.06,
                    ["segments"] = 3,
                    ["hardenNormals"] = true
                }));

            Assert.Equal(0, result.Validation.Summary.BoundaryEdgeCount);
            Assert.Equal(0, result.Validation.Summary.NonManifoldEdgeCount);
            AssertConsistentClosedWinding(result.Mesh);
        }
    }

    [Fact]
    public async Task HardenedBevelCanJoinImplicitlySmoothGeometryAndFeedWeightedNormals()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "bevel-join-normal-proof", "Bevel Join Normal Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("bevel", "rekall.modeling.bevel", 1, new JsonObject
                {
                    ["width"] = 0.06, ["segments"] = 2, ["hardenNormals"] = true
                }),
                new("sphere", "rekall.modeling.primitive.sphere", 1, new JsonObject
                {
                    ["segments"] = 12, ["rings"] = 8
                }),
                new("join", "rekall.modeling.join", 1, new JsonObject()),
                new("normals", "rekall.modeling.weighted_normals", 1, new JsonObject
                {
                    ["attribute"] = "normal.authored"
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-bevel", "box", "geometry", "bevel", "geometry"),
                new("bevel-join", "bevel", "geometry", "join", "geometry"),
                new("sphere-join", "sphere", "geometry", "join", "geometry"),
                new("join-normals", "join", "geometry", "normals", "geometry"),
                new("normals-output", "normals", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var mesh = report.Outputs["mesh"];
        var smooth = Assert.Single(mesh.Attributes, item => item.Name == "normal.smooth");
        Assert.True(smooth.DefaultValue!.Value.GetBoolean());
        Assert.Contains(smooth.Values, value => !value.GetBoolean());
        Assert.Contains(smooth.Values, value => value.GetBoolean());
        Assert.Equal(mesh.Topology.CornerIds.Count,
            Assert.Single(mesh.Attributes, item => item.Name == "normal.authored").Values.Count);
    }

    [Fact]
    public async Task BevelWeightsFilterSelectedEdgesAndGeneratedFacesUseAuthoredMaterial()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "weighted-bevel", "Weighted Bevel", CancellationToken.None);
        var weights = source.Topology.EdgeIds
            .Select((_, index) => JsonSerializer.SerializeToElement(index == 0 ? 1.0 : 0.0))
            .ToArray();
        var material = source.Topology.FaceIds
            .Select(_ => JsonSerializer.SerializeToElement(0))
            .ToArray();
        source = source with
        {
            MaterialSlots = [new("body", "material.body"), new("bevel", "material.bevel")],
            Attributes =
            [
                new("bevel.weight", RekallAgeGeometryDomain.Edge, RekallAgeGeometryValueType.Float,
                    weights, "bevel-weight", RekallAgeGeometryInterpolation.Linear),
                new("material.index", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.Int32,
                    material, "material-index", RekallAgeGeometryInterpolation.Nearest)
            ]
        };
        var parameters = new JsonObject
        {
            ["width"] = 0.12,
            ["segments"] = 2,
            ["profile"] = 0.5,
            ["clampOverlap"] = true,
            ["weightAttribute"] = "bevel.weight",
            ["materialIndex"] = 1
        };
        var weighted = new RekallAgeMeshOperationExecutor().Execute(
            source,
            new("bevel_edges", RekallAgeGeometryDomain.Edge, source.Topology.EdgeIds.Take(2).ToArray(), parameters));
        var explicitSingle = new RekallAgeMeshOperationExecutor().Execute(
            source,
            new("bevel_edges", RekallAgeGeometryDomain.Edge, [source.Topology.EdgeIds[0]], parameters));

        Assert.Equal(explicitSingle.Mesh.Topology.Positions, weighted.Mesh.Topology.Positions);
        Assert.Equal(explicitSingle.Mesh.Topology.EdgePointIndices, weighted.Mesh.Topology.EdgePointIndices);
        Assert.Equal(explicitSingle.Mesh.Topology.FaceOffsets, weighted.Mesh.Topology.FaceOffsets);
        Assert.Equal(explicitSingle.Mesh.Topology.CornerPointIndices, weighted.Mesh.Topology.CornerPointIndices);
        var outputMaterial = Assert.Single(weighted.Mesh.Attributes, item => item.Name == "material.index");
        Assert.Contains(outputMaterial.Values, value => value.GetInt32() == 1);
        Assert.Contains(outputMaterial.Values, value => value.GetInt32() == 0);
    }

    [Fact]
    public async Task BevelNodeCreatesStableInsetFacesEdgeStripsAndVertexCaps()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "bevel-proof", "Bevel Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject
                {
                    ["sizeX"] = 2.0, ["sizeY"] = 2.0, ["sizeZ"] = 2.0
                }),
                new("bevel", "rekall.modeling.bevel", 1, new JsonObject { ["width"] = 0.2 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-bevel", "box", "geometry", "bevel", "geometry"),
                new("bevel-output", "bevel", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);
        var evaluator = new RekallAgeModelingGraphEvaluator();
        var first = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);
        var second = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        Assert.Equal(24, first.Outputs["mesh"].Topology.Positions.Count);
        Assert.Equal(26, first.Outputs["mesh"].Topology.FaceIds.Count);
        Assert.Equal(first.Outputs["mesh"].Topology, second.Outputs["mesh"].Topology);
        Assert.Contains(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modeling.bevel");
    }

    [Fact]
    public async Task SegmentedBevelRoundsTransitionsAndPreservesUvAndMaterialData()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "segmented-bevel-proof", "Segmented Bevel Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject
                {
                    ["sizeX"] = 2.0, ["sizeY"] = 2.0, ["sizeZ"] = 2.0
                }),
                new("uv", "rekall.modeling.project_uv", 1, new JsonObject
                {
                    ["attribute"] = "uv.main", ["axis"] = "xz"
                }),
                new("material", "rekall.modeling.material.assign", 1, new JsonObject
                {
                    ["materialAssetId"] = "material.weathered-stone", ["slotName"] = "stone"
                }),
                new("bevel", "rekall.modeling.bevel", 1, new JsonObject
                {
                    ["width"] = 0.2, ["segments"] = 3, ["profile"] = 0.5,
                    ["clampOverlap"] = true, ["hardenNormals"] = true
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-uv", "box", "geometry", "uv", "geometry"),
                new("uv-material", "uv", "geometry", "material", "geometry"),
                new("material-bevel", "material", "geometry", "bevel", "geometry"),
                new("bevel-output", "bevel", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);
        var evaluator = new RekallAgeModelingGraphEvaluator();

        var first = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);
        var second = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        var mesh = first.Outputs["mesh"];
        Assert.Equal(80, mesh.Topology.Positions.Count);
        Assert.Equal(114, mesh.Topology.FaceIds.Count);
        Assert.Equal(mesh.Topology, second.Outputs["mesh"].Topology);
        Assert.Equal("material.weathered-stone", Assert.Single(mesh.MaterialSlots).MaterialAssetId);
        var uv = Assert.Single(mesh.Attributes, item => item.Name == "uv.main");
        Assert.Equal(mesh.Topology.CornerIds.Count, uv.Values.Count);
        Assert.All(uv.Values, value => Assert.Equal(2, value.GetArrayLength()));
        var smooth = Assert.Single(mesh.Attributes, item => item.Name == "normal.smooth");
        Assert.Contains(smooth.Values, value => value.GetBoolean());
        Assert.Contains(smooth.Values, value => !value.GetBoolean());
    }

    private static void AssertConsistentClosedWinding(RekallAgeMeshAsset mesh)
    {
        var directedUses = new Dictionary<(int A, int B), int>();
        for (var face = 0; face < mesh.Topology.FaceIds.Count; face++)
        {
            var start = mesh.Topology.FaceOffsets[face];
            var end = mesh.Topology.FaceOffsets[face + 1];
            for (var corner = start; corner < end; corner++)
            {
                var next = corner + 1 == end ? start : corner + 1;
                var a = mesh.Topology.CornerPointIndices[corner];
                var b = mesh.Topology.CornerPointIndices[next];
                directedUses[(a, b)] = directedUses.GetValueOrDefault((a, b)) + 1;
            }
        }

        foreach (var use in directedUses)
            Assert.True(
                use.Value == directedUses.GetValueOrDefault((use.Key.B, use.Key.A)),
                $"Directed edge {use.Key.A}->{use.Key.B} has {use.Value} uses but its reverse has {directedUses.GetValueOrDefault((use.Key.B, use.Key.A))}; incident faces: "
                + string.Join(" | ", Enumerable.Range(0, mesh.Topology.FaceIds.Count)
                    .Select(face => mesh.Topology.CornerPointIndices
                        .Skip(mesh.Topology.FaceOffsets[face])
                        .Take(mesh.Topology.FaceOffsets[face + 1] - mesh.Topology.FaceOffsets[face])
                        .ToArray())
                    .Where(points => points.Contains(use.Key.A) && points.Contains(use.Key.B))
                    .Select(points => string.Join(",", points))));
    }
}
