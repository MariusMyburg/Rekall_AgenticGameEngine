using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class SkinWeightAuthoringTests
{
    [Fact]
    public async Task LinearSkinWeightsAuthorNormalizedTwoJointBindingsFromGeometry()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "skin-weight-box", "Skin Weight Box", CancellationToken.None);

        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("assign_linear_skin_weights", RekallAgeGeometryDomain.Point, source.Topology.PointIds, new JsonObject
            {
                ["axis"] = "y",
                ["minimum"] = -0.5,
                ["maximum"] = 0.5,
                ["jointA"] = 2,
                ["jointB"] = 5
            }));

        Assert.True(result.Validation.IsValid);
        Assert.True(result.Changes.Kind.HasFlag(RekallAgeMeshChangeKind.Attributes));
        var joints = Assert.Single(result.Mesh.Attributes, item => item.Semantic == "joint-indices-0");
        var weights = Assert.Single(result.Mesh.Attributes, item => item.Semantic == "joint-weights-0");
        Assert.Equal(RekallAgeGeometryDomain.Point, joints.Domain);
        Assert.Equal(RekallAgeGeometryValueType.Int4, joints.ValueType);
        Assert.Equal(RekallAgeGeometryValueType.Float4, weights.ValueType);
        Assert.Equal(RekallAgeGeometryInterpolation.NormalizedLinear, weights.Interpolation);
        Assert.All(joints.Values, value =>
        {
            Assert.Equal(2, value[0].GetInt32());
            Assert.Equal(5, value[1].GetInt32());
        });
        for (var index = 0; index < source.Topology.PointIds.Count; index++)
        {
            var upper = source.Topology.Positions[index].Y > 0;
            Assert.Equal(upper ? 0 : 1, weights.Values[index][0].GetDouble(), 8);
            Assert.Equal(upper ? 1 : 0, weights.Values[index][1].GetDouble(), 8);
            Assert.Equal(1, weights.Values[index].EnumerateArray().Sum(item => item.GetDouble()), 8);
        }
    }

    [Fact]
    public async Task EnvelopeSkinWeightsKeepTheStrongestFourAndNormalizeDeterministically()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "envelope-weight-box", "Envelope Weight Box", CancellationToken.None);
        var envelopes = new JsonArray
        {
            Envelope(3, 1), Envelope(1, 1), Envelope(5, 2), Envelope(2, 1), Envelope(4, 1.5)
        };

        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("assign_envelope_skin_weights", RekallAgeGeometryDomain.Point, source.Topology.PointIds, new JsonObject
            {
                ["envelopes"] = envelopes,
                ["maximumInfluences"] = 4,
                ["fallbackToNearest"] = true
            }));

        var joints = Assert.Single(result.Mesh.Attributes, item => item.Semantic == "joint-indices-0");
        var weights = Assert.Single(result.Mesh.Attributes, item => item.Semantic == "joint-weights-0");
        Assert.All(joints.Values, value => Assert.Equal([5, 4, 1, 2], value.EnumerateArray().Select(item => item.GetInt32())));
        Assert.All(weights.Values, value =>
        {
            var actual = value.EnumerateArray().Select(item => item.GetDouble()).ToArray();
            Assert.Equal(4d / 11d, actual[0], 10);
            Assert.Equal(3d / 11d, actual[1], 10);
            Assert.Equal(2d / 11d, actual[2], 10);
            Assert.Equal(2d / 11d, actual[3], 10);
            Assert.Equal(1, actual.Sum(), 10);
        });
        Assert.All(new RekallAgeMeshCompiler().Compile(result.Mesh).Vertices, vertex =>
        {
            Assert.Equal([5, 4, 1, 2], vertex.JointIndices);
            Assert.NotNull(vertex.JointWeights);
        });

    }

    [Fact]
    public async Task EnvelopeSkinWeightsRejectMalformedEnvelopeWithoutMutatingSource()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "invalid-envelope-box", "Invalid Envelope Box", CancellationToken.None);
        var sourceJson = JsonSerializer.Serialize(source, RekallAgeModelingJson.Options);
        var invalid = Envelope(0, 1);
        invalid["headRadius"] = 0;

        var error = Assert.Throws<RekallAgeMeshOperationException>(() =>
            new RekallAgeMeshOperationExecutor().Execute(source,
                new("assign_envelope_skin_weights", RekallAgeGeometryDomain.Point, source.Topology.PointIds,
                    new JsonObject { ["envelopes"] = new JsonArray(invalid) })));

        Assert.Equal("REKALL_MESH_SKIN_ENVELOPES_INVALID", error.Code);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(source, RekallAgeModelingJson.Options));
    }

    [Fact]
    public async Task EnvelopeSkinWeightsCanFailClosedWhenASelectedPointIsUnbound()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "unbound-envelope-box", "Unbound Envelope Box", CancellationToken.None);
        var sourceJson = JsonSerializer.Serialize(source, RekallAgeModelingJson.Options);
        var distant = new JsonObject
        {
            ["jointIndex"] = 0,
            ["head"] = new JsonArray(100, 100, 100),
            ["tail"] = new JsonArray(100, 101, 100),
            ["headRadius"] = 0.1,
            ["tailRadius"] = 0.1,
            ["falloff"] = 0,
            ["weight"] = 1
        };

        var error = Assert.Throws<RekallAgeMeshOperationException>(() =>
            new RekallAgeMeshOperationExecutor().Execute(source,
                new("assign_envelope_skin_weights", RekallAgeGeometryDomain.Point, source.Topology.PointIds,
                    new JsonObject
                    {
                        ["envelopes"] = new JsonArray(distant),
                        ["fallbackToNearest"] = false
                    })));

        Assert.Equal("REKALL_MESH_SKIN_POINT_UNBOUND", error.Code);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(source, RekallAgeModelingJson.Options));
    }

    [Fact]
    public async Task EnvelopeSkinWeightNodePublishesStructuredCompilableBindings()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "envelope-skin-graph", "Envelope Skin Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("skin", "rekall.modeling.skin.envelope_weights", 1, new JsonObject
                {
                    ["envelopes"] = new JsonArray { Envelope(7, 1) },
                    ["maximumInfluences"] = 3,
                    ["fallbackToNearest"] = false
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-skin", "box", "geometry", "skin", "geometry"),
                new("skin-output", "skin", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(418, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var compiled = new RekallAgeMeshCompiler().Compile(report.Outputs["mesh"]);
        Assert.All(compiled.Vertices, vertex => Assert.Equal([7, 0, 0, 0], vertex.JointIndices));
        var descriptor = Assert.Single(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modeling.skin.envelope_weights");
        Assert.Equal(["envelopes", "maximumInfluences", "fallbackToNearest", "selectionSet"],
            descriptor.Parameters.Select(item => item.ParameterId));
        Assert.Equal(RekallAgeModelingValueType.Json, descriptor.Parameters[0].ValueType);
    }

    [Fact]
    public async Task LinearSkinWeightNodePublishesInspectableCompilableBindings()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "skin-weight-graph", "Skin Weight Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("skin", "rekall.modeling.skin.linear_weights", 1, new JsonObject
                {
                    ["axis"] = "y", ["minimum"] = -0.5, ["maximum"] = 0.5,
                    ["jointA"] = 0, ["jointB"] = 1
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-skin", "box", "geometry", "skin", "geometry"),
                new("skin-output", "skin", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var compiled = new RekallAgeMeshCompiler().Compile(report.Outputs["mesh"]);
        Assert.All(compiled.Vertices, vertex =>
        {
            Assert.NotNull(vertex.JointIndices);
            Assert.NotNull(vertex.JointWeights);
        });
        var descriptor = Assert.Single(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modeling.skin.linear_weights");
        Assert.Equal(["axis", "minimum", "maximum", "jointA", "jointB", "selectionSet"],
            descriptor.Parameters.Select(item => item.ParameterId));
    }

    [Fact]
    public async Task LinearSkinWeightsAreAvailableThroughTheNonDestructiveModifierStack()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "skin-modifier-box", "Skin Modifier Box", CancellationToken.None);
        var stack = RekallAgeModifierStackAsset.Create(
            "skin-stack", "Skin Stack", source.AssetId, new string('a', 64),
            [new("weights", "rekall.modifier.skin.linear_weights", 1, true, new JsonObject
            {
                ["axis"] = "y", ["minimum"] = -0.5, ["maximum"] = 0.5,
                ["jointA"] = 3, ["jointB"] = 4
            })]);

        var report = await new RekallAgeModifierStackEvaluator().EvaluateAsync(
            stack, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        Assert.Contains(report.Mesh!.Attributes, item => item.Semantic == "joint-indices-0");
        Assert.Contains(report.Mesh.Attributes, item => item.Semantic == "joint-weights-0");
        Assert.Contains(RekallAgeModifierCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modifier.skin.linear_weights");
    }

    [Fact]
    public async Task EnvelopeSkinWeightsAreAvailableThroughTheNonDestructiveModifierStack()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "envelope-skin-modifier-box", "Envelope Skin Modifier Box", CancellationToken.None);
        var stack = RekallAgeModifierStackAsset.Create(
            "envelope-skin-stack", "Envelope Skin Stack", source.AssetId, new string('b', 64),
            [new("weights", "rekall.modifier.skin.envelope_weights", 1, true, new JsonObject
            {
                ["envelopes"] = new JsonArray { Envelope(9, 1) },
                ["maximumInfluences"] = 2,
                ["fallbackToNearest"] = false
            })]);

        var report = await new RekallAgeModifierStackEvaluator().EvaluateAsync(
            stack, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var compiled = new RekallAgeMeshCompiler().Compile(report.Mesh!);
        Assert.All(compiled.Vertices, vertex => Assert.Equal([9, 0, 0, 0], vertex.JointIndices));
        var descriptor = Assert.Single(RekallAgeModifierCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modifier.skin.envelope_weights");
        Assert.Equal(RekallAgeModelingValueType.Json, descriptor.Parameters[0].ValueType);
    }

    [Fact]
    public async Task LinearSkinWeightsRejectAnUnrelatedAttributeUsingTheCanonicalOutputName()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "skin-name-collision", "Skin Name Collision", CancellationToken.None);
        source = source with
        {
            Attributes =
            [
                new("skin.joints", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float,
                    source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(0.5)).ToArray(),
                    "custom-data", RekallAgeGeometryInterpolation.Linear)
            ]
        };

        var error = Assert.Throws<RekallAgeMeshOperationException>(() =>
            new RekallAgeMeshOperationExecutor().Execute(source,
                new("assign_linear_skin_weights", RekallAgeGeometryDomain.Point, source.Topology.PointIds,
                    new JsonObject { ["minimum"] = -0.5, ["maximum"] = 0.5 })));

        Assert.Equal("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", error.Code);
        Assert.Single(source.Attributes);
    }

    [Fact]
    public async Task LinearSkinWeightsRejectAnIncompleteExistingBindingPair()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "skin-incomplete-pair", "Skin Incomplete Pair", CancellationToken.None);
        source = source with
        {
            Attributes =
            [
                new("skin.weights", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float4,
                    source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { 1d, 0d, 0d, 0d })).ToArray(),
                    "joint-weights-0", RekallAgeGeometryInterpolation.NormalizedLinear)
            ]
        };

        var error = Assert.Throws<RekallAgeMeshOperationException>(() =>
            new RekallAgeMeshOperationExecutor().Execute(source,
                new("assign_linear_skin_weights", RekallAgeGeometryDomain.Point, source.Topology.PointIds,
                    new JsonObject { ["minimum"] = -0.5, ["maximum"] = 0.5 })));

        Assert.Equal("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", error.Code);
        Assert.Single(source.Attributes);
    }

    [Fact]
    public async Task LinearSkinWeightsReplaceAnExistingMixedCaseSemanticPair()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "skin-mixed-case", "Skin Mixed Case", CancellationToken.None);
        source = source with
        {
            Attributes =
            [
                new("existing.joints", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Int4,
                    source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { 7, 8, 0, 0 })).ToArray(),
                    "Joint-Indices-0", RekallAgeGeometryInterpolation.Nearest),
                new("existing.weights", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float4,
                    source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { 0.5, 0.5, 0d, 0d })).ToArray(),
                    "Joint-Weights-0", RekallAgeGeometryInterpolation.NormalizedLinear)
            ]
        };

        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("assign_linear_skin_weights", RekallAgeGeometryDomain.Point, source.Topology.PointIds,
                new JsonObject
                {
                    ["minimum"] = -0.5, ["maximum"] = 0.5,
                    ["jointA"] = 2, ["jointB"] = 3
                }));

        Assert.Equal(2, result.Mesh.Attributes.Count);
        var joints = Assert.Single(result.Mesh.Attributes, item => item.Name == "existing.joints");
        Assert.All(joints.Values, value =>
        {
            Assert.Equal(2, value[0].GetInt32());
            Assert.Equal(3, value[1].GetInt32());
        });
    }

    [Fact]
    public async Task LinearSkinWeightsRejectCaseInsensitiveDuplicateSemantics()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "skin-case-duplicate", "Skin Case Duplicate", CancellationToken.None);
        var joints = source.Topology.PointIds
            .Select(_ => JsonSerializer.SerializeToElement(new[] { 0, 1, 0, 0 })).ToArray();
        source = source with
        {
            Attributes =
            [
                new("joints.a", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Int4,
                    joints, "joint-indices-0", RekallAgeGeometryInterpolation.Nearest),
                new("joints.b", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Int4,
                    joints, "Joint-Indices-0", RekallAgeGeometryInterpolation.Nearest),
                new("weights", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float4,
                    source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { 1d, 0d, 0d, 0d })).ToArray(),
                    "joint-weights-0", RekallAgeGeometryInterpolation.NormalizedLinear)
            ]
        };

        var error = Assert.Throws<RekallAgeMeshOperationException>(() =>
            new RekallAgeMeshOperationExecutor().Execute(source,
                new("assign_linear_skin_weights", RekallAgeGeometryDomain.Point, source.Topology.PointIds,
                    new JsonObject { ["minimum"] = -0.5, ["maximum"] = 0.5 })));

        Assert.Equal("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", error.Code);
        Assert.Equal(3, source.Attributes.Count);
    }

    private static JsonObject Envelope(int jointIndex, double weight) => new()
    {
        ["jointIndex"] = jointIndex,
        ["head"] = new JsonArray(0, -1, 0),
        ["tail"] = new JsonArray(0, 1, 0),
        ["headRadius"] = 1,
        ["tailRadius"] = 1,
        ["falloff"] = 0,
        ["weight"] = weight
    };
}
