using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModelingProductionContractMatrixTests
{
    public static TheoryData<string> MeshOperationCases => new()
    {
        "transform",
        "reverse_faces",
        "triangulate_faces",
        "extrude_faces",
        "delete",
        "generate_normals",
        "project_uv",
        "subdivide_faces",
        "subdivide_smooth",
        "set_edge_crease",
        "merge_by_distance",
        "assign_envelope_skin_weights",
        "bend_points"
    };

    public static TheoryData<string> ModifierCases => new()
    {
        "rekall.modifier.transform",
        "rekall.modifier.triangulate",
        "rekall.modifier.extrude",
        "rekall.modifier.subdivide",
        "rekall.modifier.subdivide_smooth",
        "rekall.modifier.merge_by_distance",
        "rekall.modifier.skin.envelope_weights",
        "rekall.modifier.deform.bend"
    };

    public static TheoryData<string, JsonObject, int> ClosedPrimitiveCases => new()
    {
        { "rekall.modeling.primitive.box", new JsonObject(), 2 },
        { "rekall.modeling.primitive.sphere", new JsonObject { ["segments"] = 8, ["rings"] = 4 }, 2 },
        { "rekall.modeling.primitive.frustum", new JsonObject { ["radiusBottom"] = 1.0, ["radiusTop"] = 1.0, ["segments"] = 8 }, 2 },
        { "rekall.modeling.primitive.frustum", new JsonObject { ["radiusBottom"] = 1.0, ["radiusTop"] = 0.0, ["segments"] = 8 }, 2 },
        { "rekall.modeling.primitive.torus", new JsonObject { ["majorSegments"] = 8, ["minorSegments"] = 4 }, 0 }
    };

    private static readonly (string TypeId, JsonObject Parameters)[] ClosedPrimitiveSpecifications =
    {
        ("rekall.modeling.primitive.box", new JsonObject { ["sizeX"] = 2.0, ["sizeY"] = 2.0, ["sizeZ"] = 2.0 }),
        ("rekall.modeling.primitive.sphere", new JsonObject { ["radius"] = 1.2, ["segments"] = 12, ["rings"] = 6 }),
        ("rekall.modeling.primitive.frustum", new JsonObject { ["radiusBottom"] = 0.9, ["radiusTop"] = 0.9, ["depth"] = 2.0, ["segments"] = 12 }),
        ("rekall.modeling.primitive.frustum", new JsonObject { ["radiusBottom"] = 1.0, ["radiusTop"] = 0.0, ["depth"] = 2.0, ["segments"] = 12 }),
        ("rekall.modeling.primitive.torus", new JsonObject { ["majorRadius"] = 0.7, ["minorRadius"] = 0.35, ["majorSegments"] = 12, ["minorSegments"] = 6 })
    };

    public static IEnumerable<object[]> BooleanPrimitivePairs
    {
        get
        {
            for (var left = 0; left < ClosedPrimitiveSpecifications.Length; left++)
            for (var right = left; right < ClosedPrimitiveSpecifications.Length; right++)
            {
                yield return
                [
                    ClosedPrimitiveSpecifications[left].TypeId,
                    ClosedPrimitiveSpecifications[left].Parameters,
                    ClosedPrimitiveSpecifications[right].TypeId,
                    ClosedPrimitiveSpecifications[right].Parameters
                ];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ClosedPrimitiveCases))]
    public async Task ClosedPrimitivesPublishFiniteManifoldTopology(
        string typeId,
        JsonObject parameters,
        int expectedEulerCharacteristic)
    {
        var result = await EvaluatePrimitive(typeId, parameters);

        Assert.True(result.Succeeded, Diagnostics(result));
        var mesh = result.Outputs["mesh"];
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        Assert.True(validation.IsValid);
        Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
        Assert.Equal(0, validation.Summary.NonManifoldEdgeCount);
        Assert.Equal(expectedEulerCharacteristic,
            validation.Summary.PointCount - validation.Summary.EdgeCount + validation.Summary.FaceCount);
        Assert.All(mesh.Topology.Positions, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.True(double.IsFinite(point.Z));
        });
    }

    [Theory]
    [MemberData(nameof(BooleanPrimitivePairs))]
    public async Task BooleanOperationsProduceClosedTopologyAcrossDistinctPrimitivePairs(
        string leftType,
        JsonObject leftParameters,
        string rightType,
        JsonObject rightParameters)
    {
        foreach (var operation in new[] { "union", "intersect", "difference" })
        {
            var graph = RekallAgeModelingGraphAsset.Create(
                $"boolean-matrix-{operation}",
                "Boolean Matrix",
                [
                    new("left", leftType, 1, (JsonObject)leftParameters.DeepClone()),
                    new("right", rightType, 1, (JsonObject)rightParameters.DeepClone()),
                    new("move-right", "rekall.modeling.transform", 1, new JsonObject { ["translation"] = new JsonArray(0.25, 0.1, 0.15) }),
                    new("boolean", "rekall.modeling.boolean", 1, new JsonObject { ["operation"] = operation }),
                    new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
                ],
                [
                    new("right-move", "right", "geometry", "move-right", "geometry"),
                    new("left-boolean", "left", "geometry", "boolean", "a"),
                    new("right-boolean", "move-right", "geometry", "boolean", "b"),
                    new("boolean-output", "boolean", "geometry", "output", "input")
                ],
                [new("mesh", "output", "geometry")]);

            var result = await Evaluate(graph);

            Assert.True(result.Succeeded, $"{leftType} {operation} {rightType}{Environment.NewLine}{Diagnostics(result)}");
            var validation = new RekallAgeMeshValidator().Validate(result.Outputs["mesh"]);
            Assert.True(validation.IsValid);
            Assert.True(validation.Summary.FaceCount > 0);
            Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
            Assert.Equal(0, validation.Summary.NonManifoldEdgeCount);
        }
    }

    [Theory]
    [MemberData(nameof(MeshOperationCases))]
    public async Task EveryAdvertisedMeshOperationExecutesDeterministicallyAndPreservesAValidAsset(string operationId)
    {
        var source = await Box();
        var sourceJson = System.Text.Json.JsonSerializer.Serialize(source, RekallAgeModelingJson.Options);
        var executor = new RekallAgeMeshOperationExecutor();
        var descriptor = Assert.Single(executor.Descriptors, item => item.OperationId == operationId);
        var request = OperationRequest(operationId, descriptor.Domain, source);

        var first = executor.Execute(source, request);
        var second = executor.Execute(source, request);

        Assert.Equal(source.Revision, first.BeforeRevision);
        Assert.Equal(source.Revision + 1, first.AfterRevision);
        Assert.Equal(first.AfterRevision, first.Mesh.Revision);
        Assert.True(first.Validation.IsValid, Diagnostics(first.Validation));
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first.Mesh, RekallAgeModelingJson.Options),
            System.Text.Json.JsonSerializer.Serialize(second.Mesh, RekallAgeModelingJson.Options));
        Assert.Equal(sourceJson, System.Text.Json.JsonSerializer.Serialize(source, RekallAgeModelingJson.Options));
        Assert.Equal(RekallAgeMeshChangeKind.None, first.Changes.Kind & ~descriptor.PossibleChanges);
    }

    [Theory]
    [MemberData(nameof(ModifierCases))]
    public async Task EveryAdvertisedModifierEvaluatesDeterministicallyAndPreservesAValidAsset(string typeId)
    {
        var source = await Box();
        var sourceJson = System.Text.Json.JsonSerializer.Serialize(source, RekallAgeModelingJson.Options);
        var catalog = RekallAgeModifierCatalog.CreateDefault();
        Assert.Single(catalog.Descriptors, item => item.TypeId == typeId);
        var stack = RekallAgeModifierStackAsset.Create(
            "production-contract-stack",
            "Production Contract Stack",
            source.AssetId,
            new string('a', 64),
            [new("modifier", typeId, 1, true, ModifierParameters(typeId))]);

        var first = await new RekallAgeModifierStackEvaluator().EvaluateAsync(
            stack, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);
        var second = await new RekallAgeModifierStackEvaluator().EvaluateAsync(
            stack, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);

        Assert.True(first.Succeeded, Diagnostics(first));
        Assert.True(second.Succeeded, Diagnostics(second));
        Assert.NotNull(first.Mesh);
        Assert.True(new RekallAgeMeshValidator().Validate(first.Mesh!).IsValid);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first.Mesh, RekallAgeModelingJson.Options),
            System.Text.Json.JsonSerializer.Serialize(second.Mesh, RekallAgeModelingJson.Options));
        Assert.Equal(sourceJson, System.Text.Json.JsonSerializer.Serialize(source, RekallAgeModelingJson.Options));
        Assert.Single(first.Modifiers, item => item.TypeId == typeId);
    }

    [Fact]
    public void ProductionMatricesExactlyCoverThePublishedOperationAndModifierCatalogs()
    {
        var operationIds = new RekallAgeMeshOperationExecutor().Descriptors.Select(item => item.OperationId).ToArray();
        Assert.Equal(30, operationIds.Length);
        Assert.All(new[]
        {
            "transform", "reverse_faces", "triangulate_faces", "extrude_faces", "delete",
            "generate_normals", "shade_faces", "mark_sharp", "auto_smooth", "project_uv", "mark_uv_seams", "unwrap_pack_uv", "subdivide_faces", "subdivide_smooth", "set_edge_crease", "merge_by_distance",
            "bevel_edges", "select_edges_by_angle", "assign_linear_skin_weights", "assign_envelope_skin_weights", "taper_points", "bend_points", "inset_faces", "solidify", "weighted_normals",
            "fill_holes", "bridge_edge_loops", "poke_faces", "dissolve_edges", "bisect_plane"
        }, expected => Assert.Contains(expected, operationIds));

        var modifierIds = RekallAgeModifierCatalog.CreateDefault().Descriptors.Select(item => item.TypeId).ToArray();
        Assert.Equal(16, modifierIds.Length);
        Assert.All(new[]
        {
            "rekall.modifier.transform", "rekall.modifier.triangulate", "rekall.modifier.extrude",
            "rekall.modifier.subdivide", "rekall.modifier.subdivide_smooth", "rekall.modifier.merge_by_distance",
            "rekall.modifier.bevel", "rekall.modifier.solidify", "rekall.modifier.mirror", "rekall.modifier.array", "rekall.modifier.auto_smooth",
            "rekall.modifier.weighted_normals", "rekall.modifier.skin.linear_weights", "rekall.modifier.skin.envelope_weights", "rekall.modifier.deform.taper", "rekall.modifier.deform.bend"
        }, expected => Assert.Contains(expected, modifierIds));
    }

    private static ValueTask<RekallAgeModelingGraphEvaluationReport> EvaluatePrimitive(string typeId, JsonObject parameters)
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "primitive-contract",
            "Primitive Contract",
            [new("primitive", typeId, 1, (JsonObject)parameters.DeepClone()), new("output", "rekall.modeling.output.mesh", 1, new JsonObject())],
            [new("primitive-output", "primitive", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);
        return Evaluate(graph);
    }

    private static ValueTask<RekallAgeModelingGraphEvaluationReport> Evaluate(RekallAgeModelingGraphAsset graph) =>
        new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph,
            ["mesh"],
            RekallAgeModelingEvaluationBudget.Default,
            new(Seed: 17, DeterministicTime: 0, EngineVersion: "contract-test", TargetProfile: "desktop"),
            CancellationToken.None);

    private static string Diagnostics(RekallAgeModelingGraphEvaluationReport result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));

    private static string Diagnostics(RekallAgeModifierStackEvaluationReport result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));

    private static string Diagnostics(RekallAgeMeshValidationReport result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));

    private static RekallAgeMeshOperationRequest OperationRequest(
        string operationId,
        RekallAgeGeometryDomain domain,
        RekallAgeMeshAsset source)
    {
        IReadOnlyList<ulong> elementIds = operationId is "extrude_faces" or "delete"
            ? [source.Topology.FaceIds[0]]
            : domain switch
            {
                RekallAgeGeometryDomain.Point => source.Topology.PointIds,
                RekallAgeGeometryDomain.Edge => source.Topology.EdgeIds,
                RekallAgeGeometryDomain.Corner => source.Topology.CornerIds,
                _ => source.Topology.FaceIds
            };
        var parameters = operationId switch
        {
            "transform" => new JsonObject { ["x"] = 0.25, ["y"] = -0.5, ["z"] = 0.75 },
            "extrude_faces" => new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.5 },
            "generate_normals" => new JsonObject { ["attribute"] = "normal.contract" },
            "project_uv" => new JsonObject { ["attribute"] = "uv.contract", ["axis"] = "xz" },
            "set_edge_crease" => new JsonObject { ["weight"] = 0.75 },
            "merge_by_distance" => new JsonObject { ["distance"] = 0.0001 },
            "assign_envelope_skin_weights" => EnvelopeParameters(),
            "bend_points" => new JsonObject { ["axis"] = "y", ["bendAxis"] = "z", ["minimum"] = -1.0, ["maximum"] = 1.0, ["angleDegrees"] = 15.0 },
            _ => new JsonObject()
        };
        return new(operationId, domain, elementIds, parameters);
    }

    private static JsonObject ModifierParameters(string typeId) => typeId switch
    {
        "rekall.modifier.transform" => new JsonObject { ["x"] = 0.25, ["y"] = -0.5, ["z"] = 0.75 },
        "rekall.modifier.extrude" => new JsonObject { ["z"] = 0.5, ["selection"] = "contract-face" },
        "rekall.modifier.merge_by_distance" => new JsonObject { ["distance"] = 0.0001 },
        "rekall.modifier.skin.envelope_weights" => EnvelopeParameters(),
        "rekall.modifier.deform.bend" => new JsonObject { ["axis"] = "y", ["bendAxis"] = "z", ["minimum"] = -1.0, ["maximum"] = 1.0, ["angleDegrees"] = 15.0 },
        _ => new JsonObject()
    };

    private static JsonObject EnvelopeParameters() => new()
    {
        ["envelopes"] = new JsonArray(new JsonObject
        {
            ["jointIndex"] = 0,
            ["head"] = new JsonArray(0, -1, 0),
            ["tail"] = new JsonArray(0, 1, 0),
            ["headRadius"] = 2,
            ["tailRadius"] = 2,
            ["falloff"] = 0,
            ["weight"] = 0.5
        })
    };

    private static async ValueTask<RekallAgeMeshAsset> Box()
    {
        var result = await EvaluatePrimitive("rekall.modeling.primitive.box", new JsonObject());
        Assert.True(result.Succeeded, Diagnostics(result));
        var mesh = result.Outputs["mesh"];
        return mesh with
        {
            SelectionSets =
            [
                new("contract-face", RekallAgeGeometryDomain.Face, [mesh.Topology.FaceIds[0]], mesh.Topology.FaceIds[0], [mesh.Topology.FaceIds[0]])
            ]
        };
    }
}
