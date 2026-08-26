using System.Numerics;
using System.Text.Json;
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
        Assert.Equal(36_000, angle.Maximum);
        Assert.Equal("degree", angle.Unit);
        var pitch = Assert.Single(descriptor.Parameters, parameter => parameter.ParameterId == "pitchPerTurn");
        Assert.Equal(-1_000_000, pitch.Minimum);
        Assert.Equal(1_000_000, pitch.Maximum);
        Assert.Equal("world-unit", pitch.Unit);
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

    [Fact]
    public async Task AxisWeldingRemovesDegeneratePoleFacesAndEmitsAuthoringAttributes()
    {
        var result = await EvaluateProfileAsync(
            [new(0, -1, 0), new(1, 1, 0)],
            cyclic: false,
            new JsonObject { ["axis"] = "y", ["segments"] = 8, ["weldDistance"] = 0.00001 });

        Assert.True(result.Succeeded, Diagnostics(result));
        var mesh = result.Outputs["mesh"];
        Assert.Equal(9, mesh.Topology.PointIds.Count);
        Assert.Equal(8, mesh.Topology.FaceIds.Count);
        Assert.All(Enumerable.Range(0, mesh.Topology.FaceIds.Count), faceIndex =>
        {
            var count = mesh.Topology.FaceOffsets[faceIndex + 1] - mesh.Topology.FaceOffsets[faceIndex];
            Assert.Equal(3, count);
        });
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);

        var uv = Attribute(mesh, "uv.generated", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2);
        var provenance = Attribute(mesh, "curve.source.span", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.String);
        var angles = Attribute(mesh, "revolve.angle", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float);
        var material = Attribute(mesh, "material.index", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.Int32);
        var smooth = Attribute(mesh, "normal.smooth", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.Bool);
        Assert.Equal(mesh.Topology.CornerIds.Count, uv.Values.Count);
        Assert.Equal(mesh.Topology.PointIds.Count, provenance.Values.Count);
        Assert.Equal(mesh.Topology.PointIds.Count, angles.Values.Count);
        Assert.Equal(mesh.Topology.FaceIds.Count, material.Values.Count);
        Assert.Equal(mesh.Topology.FaceIds.Count, smooth.Values.Count);
        Assert.All(smooth.Values, value => Assert.True(value.GetBoolean()));
        var uValues = uv.Values.Select(value => value[0].GetDouble()).ToArray();
        Assert.Contains(0, uValues);
        Assert.Contains(1, uValues);
    }

    [Fact]
    public async Task PartialAndCyclicProfilesProduceExpectedOpenAndClosedSpans()
    {
        var partial = await EvaluateProfileAsync(
            [new(1, -1, 0), new(1, 1, 0)],
            cyclic: false,
            new JsonObject { ["axis"] = "y", ["segments"] = 8, ["angleDegrees"] = 180.0 });
        var cyclic = await EvaluateProfileAsync(
            [new(1, -0.5, 0), new(1.5, 0, 0), new(1, 0.5, 0)],
            cyclic: true,
            new JsonObject { ["axis"] = "y", ["segments"] = 8 });

        Assert.True(partial.Succeeded, Diagnostics(partial));
        Assert.Equal(18, partial.Outputs["mesh"].Topology.PointIds.Count);
        Assert.Equal(8, partial.Outputs["mesh"].Topology.FaceIds.Count);
        Assert.Equal(18, new RekallAgeMeshValidator().Validate(partial.Outputs["mesh"]).Summary.BoundaryEdgeCount);
        Assert.True(cyclic.Succeeded, Diagnostics(cyclic));
        Assert.Equal(24, cyclic.Outputs["mesh"].Topology.PointIds.Count);
        Assert.Equal(24, cyclic.Outputs["mesh"].Topology.FaceIds.Count);
        Assert.Equal(0, new RekallAgeMeshValidator().Validate(cyclic.Outputs["mesh"]).Summary.BoundaryEdgeCount);
    }

    [Theory]
    [InlineData("x", -1, 1, 2, 4, 3, 5)]
    [InlineData("y", 1, 3, 1, 5, 3, 5)]
    [InlineData("z", 1, 3, 2, 4, 2, 6)]
    public async Task PrincipalAxesAndOriginProduceFiniteExpectedBounds(
        string axis,
        double minX,
        double maxX,
        double minY,
        double maxY,
        double minZ,
        double maxZ)
    {
        var profile = axis switch
        {
            "x" => new[] { new RekallAgeGeometryVector3(-1, 4, 4), new RekallAgeGeometryVector3(1, 4, 4) },
            "y" => new[] { new RekallAgeGeometryVector3(3, 1, 4), new RekallAgeGeometryVector3(3, 5, 4) },
            _ => new[] { new RekallAgeGeometryVector3(3, 3, 2), new RekallAgeGeometryVector3(3, 3, 6) }
        };
        var result = await EvaluateProfileAsync(profile, false, new JsonObject
        {
            ["axis"] = axis,
            ["origin"] = new JsonArray(2.0, 3.0, 4.0),
            ["segments"] = 8
        });

        Assert.True(result.Succeeded, Diagnostics(result));
        var positions = result.Outputs["mesh"].Topology.Positions;
        Assert.All(positions, position => Assert.True(
            double.IsFinite(position.X) && double.IsFinite(position.Y) && double.IsFinite(position.Z)));
        Assert.Equal(minX, positions.Min(point => point.X), 6);
        Assert.Equal(maxX, positions.Max(point => point.X), 6);
        Assert.Equal(minY, positions.Min(point => point.Y), 6);
        Assert.Equal(maxY, positions.Max(point => point.Y), 6);
        Assert.Equal(minZ, positions.Min(point => point.Z), 6);
        Assert.Equal(maxZ, positions.Max(point => point.Z), 6);
    }

    [Theory]
    [InlineData(2.0, 0.0, 4.25, 4.0)]
    [InlineData(-2.0, -4.0, 0.25, -4.0)]
    public async Task SignedPitchProducesOpenMultiTurnScrewWithInspectableAxialOffsets(
        double pitchPerTurn,
        double expectedMinimumY,
        double expectedMaximumY,
        double expectedFinalOffset)
    {
        var result = await EvaluateProfileAsync(
            [new(1, 0, 0), new(1, 0.25, 0)],
            cyclic: false,
            new JsonObject
            {
                ["axis"] = "y",
                ["angleDegrees"] = 720.0,
                ["segments"] = 8,
                ["pitchPerTurn"] = pitchPerTurn
            });

        Assert.True(result.Succeeded, Diagnostics(result));
        var mesh = result.Outputs["mesh"];
        Assert.Equal(18, mesh.Topology.PointIds.Count);
        Assert.Equal(8, mesh.Topology.FaceIds.Count);
        Assert.Equal(expectedMinimumY, mesh.Topology.Positions.Min(point => point.Y), 6);
        Assert.Equal(expectedMaximumY, mesh.Topology.Positions.Max(point => point.Y), 6);
        Assert.Equal(18, new RekallAgeMeshValidator().Validate(mesh).Summary.BoundaryEdgeCount);

        var angles = Attribute(mesh, "revolve.angle", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float);
        var offsets = Attribute(mesh, "revolve.axial_offset", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float);
        Assert.Equal(720, angles.Values.Max(value => value.GetDouble()), 6);
        Assert.Equal(expectedFinalOffset, offsets.Values[^1].GetDouble(), 6);
    }

    [Fact]
    public async Task AnyFiniteNonzeroPitchProducesOpenFullTurnTopology()
    {
        const double tinyPitch = 1e-13;
        var result = await EvaluateProfileAsync(
            [new(1, 0, 0), new(1, 1, 0)],
            cyclic: false,
            new JsonObject
            {
                ["axis"] = "y",
                ["angleDegrees"] = 360.0,
                ["segments"] = 8,
                ["pitchPerTurn"] = tinyPitch
            });

        Assert.True(result.Succeeded, Diagnostics(result));
        var mesh = result.Outputs["mesh"];
        Assert.Equal(18, mesh.Topology.PointIds.Count);
        Assert.Equal(18, new RekallAgeMeshValidator().Validate(mesh).Summary.BoundaryEdgeCount);
        var offsets = Attribute(mesh, "revolve.axial_offset", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float);
        Assert.InRange(offsets.Values[^1].GetDouble(), tinyPitch - 1e-28, tinyPitch + 1e-28);
    }

    [Fact]
    public async Task MultiTurnZeroPitchRejectsOverlappingRevolution()
    {
        var result = await EvaluateProfileAsync(
            [new(1, 0, 0), new(1, 1, 0)],
            cyclic: false,
            new JsonObject { ["angleDegrees"] = 720.0, ["segments"] = 16, ["pitchPerTurn"] = 0.0 });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "REKALL_MODELING_EVALUATION_PARAMETER_INVALID"
            && diagnostic.Message.Contains("pitchPerTurn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfileUvVUsesCumulativeArcLengthAndReplayIsDeterministic()
    {
        var first = await EvaluateProfileAsync(
            [new(1, 0, 0), new(1, 1, 0), new(1, 4, 0)],
            false,
            new JsonObject { ["segments"] = 8 });
        var second = await EvaluateProfileAsync(
            [new(1, 0, 0), new(1, 1, 0), new(1, 4, 0)],
            false,
            new JsonObject { ["segments"] = 8 });

        Assert.True(first.Succeeded, Diagnostics(first));
        var uv = Attribute(first.Outputs["mesh"], "uv.generated", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2);
        Assert.Contains(uv.Values, value => Math.Abs(value[1].GetDouble() - 0.25) < 1e-9);
        Assert.Equal(
            JsonSerializer.Serialize(first.Outputs["mesh"], RekallAgeModelingJson.Options),
            JsonSerializer.Serialize(second.Outputs["mesh"], RekallAgeModelingJson.Options));
    }

    [Fact]
    public async Task OversizedRevolveFailsBeforeCreatingGeometry()
    {
        var graph = Graph(
            new("profile", "rekall.modeling.curve.circle", 1, new JsonObject
            {
                ["center"] = new JsonArray(2.0, 0.0, 0.0),
                ["radius"] = 0.5,
                ["segments"] = 1000,
                ["plane"] = "xy"
            }),
            new JsonObject { ["segments"] = 4096 });

        var result = await EvaluateAsync(graph);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODELING_REVOLVE_OUTPUT_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task SplitNormalsOnRevolvedMeshCompileToFiniteOrthogonalTangentFrames()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "revolve-compile", "Revolve Compile",
            [
                new("profile", "rekall.modeling.curve.line", 1, new JsonObject
                {
                    ["start"] = new JsonArray(1.0, -1.0, 0.0),
                    ["end"] = new JsonArray(1.0, 1.0, 0.0)
                }),
                new("revolve", "rekall.modeling.curve.revolve", 1, new JsonObject { ["segments"] = 16 }),
                new("auto", "rekall.modeling.auto_smooth", 1, new JsonObject { ["angleDegrees"] = 45.0 }),
                new("normals", "rekall.modeling.weighted_normals", 1, new JsonObject
                {
                    ["attribute"] = "normal.authored",
                    ["faceAreaWeight"] = 1.0,
                    ["cornerAngleWeight"] = 1.0
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("profile-revolve", "profile", "curve", "revolve", "curve"),
                new("revolve-auto", "revolve", "geometry", "auto", "geometry"),
                new("auto-normals", "auto", "geometry", "normals", "geometry"),
                new("normals-output", "normals", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);
        var result = await EvaluateAsync(graph);

        Assert.True(result.Succeeded, Diagnostics(result));
        var compiled = new RekallAgeMeshCompiler().Compile(result.Outputs["mesh"]);
        Assert.Single(compiled.Surfaces);
        Assert.All(compiled.Vertices, vertex =>
        {
            var normal = new Vector3((float)vertex.Normal.X, (float)vertex.Normal.Y, (float)vertex.Normal.Z);
            var tangent = new Vector3((float)vertex.Tangent.X, (float)vertex.Tangent.Y, (float)vertex.Tangent.Z);
            Assert.InRange(normal.Length(), 0.99999f, 1.00001f);
            Assert.InRange(tangent.Length(), 0.99999f, 1.00001f);
            Assert.InRange(Math.Abs(Vector3.Dot(normal, tangent)), 0, 0.00001f);
        });
    }

    private static async Task<RekallAgeModelingGraphEvaluationReport> EvaluateProfileAsync(
        IReadOnlyList<RekallAgeGeometryVector3> points,
        bool cyclic,
        JsonObject parameters)
    {
        var controlPoints = points.Select((position, index) =>
            new RekallAgeCurveControlPoint(
                (ulong)(index + 1), position, position, position)).ToArray();
        var curve = RekallAgeCurveAsset.Create(
            "test.revolve.profile",
            "Test Revolve Profile",
            [new(1, RekallAgeCurveSplineKind.Poly, cyclic, controlPoints)]);
        var source = new RekallAgeModelingGraphNode(
            "profile", "rekall.modeling.curve.source", 1,
            new JsonObject
            {
                ["document"] = JsonSerializer.SerializeToNode(curve, RekallAgeModelingJson.Options),
                ["resolution"] = 1
            });
        return await EvaluateAsync(Graph(source, parameters));
    }

    private static RekallAgeModelingGraphAsset Graph(
        RekallAgeModelingGraphNode source,
        JsonObject revolveParameters) =>
        RekallAgeModelingGraphAsset.Create(
            "curve-revolve-fixture", "Curve Revolve Fixture",
            [
                source,
                new("revolve", "rekall.modeling.curve.revolve", 1, revolveParameters),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("profile-revolve", "profile", "curve", "revolve", "curve"),
                new("revolve-output", "revolve", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

    private static async Task<RekallAgeModelingGraphEvaluationReport> EvaluateAsync(
        RekallAgeModelingGraphAsset graph) =>
        await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph,
            ["mesh"],
            RekallAgeModelingEvaluationBudget.Default,
            new(0, 0, "tests", "desktop"),
            CancellationToken.None);

    private static RekallAgeGeometryAttribute Attribute(
        RekallAgeMeshAsset mesh,
        string name,
        RekallAgeGeometryDomain domain,
        RekallAgeGeometryValueType valueType)
    {
        var attribute = Assert.Single(mesh.Attributes, item => item.Name == name);
        Assert.Equal(domain, attribute.Domain);
        Assert.Equal(valueType, attribute.ValueType);
        return attribute;
    }

    private static string Diagnostics(RekallAgeModelingGraphEvaluationReport report) =>
        string.Join(Environment.NewLine, report.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
}
