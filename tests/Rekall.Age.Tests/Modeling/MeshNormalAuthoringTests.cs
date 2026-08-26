using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshNormalAuthoringTests
{
    [Fact]
    public async Task PolicyShadeFacesPreservesUnselectedSmoothValuesAndTopology()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "shade-policy", "Shade Policy", CancellationToken.None);
        var executor = new RekallAgeMeshOperationExecutor();
        var firstFace = source.Topology.FaceIds[0];
        var secondFace = source.Topology.FaceIds[1];

        var first = executor.Execute(source, new(
            "shade_faces",
            RekallAgeGeometryDomain.Face,
            [firstFace],
            new JsonObject { ["smooth"] = false }));
        var second = executor.Execute(first.Mesh, new(
            "shade_faces",
            RekallAgeGeometryDomain.Face,
            [secondFace],
            new JsonObject { ["smooth"] = false }));
        var restored = executor.Execute(second.Mesh, new(
            "shade_faces",
            RekallAgeGeometryDomain.Face,
            [firstFace],
            new JsonObject { ["smooth"] = true }));

        var policy = Assert.Single(restored.Mesh.Attributes, item => item.Name == "normal.smooth");
        Assert.Equal(RekallAgeGeometryDomain.Face, policy.Domain);
        Assert.Equal(RekallAgeGeometryValueType.Bool, policy.ValueType);
        Assert.True(policy.Values[0].GetBoolean());
        Assert.False(policy.Values[1].GetBoolean());
        Assert.All(policy.Values.Skip(2), value => Assert.True(value.GetBoolean()));
        Assert.Equal(source.Topology, restored.Mesh.Topology);
        Assert.Equal(source.Revision + 3, restored.Mesh.Revision);
        Assert.Equal([firstFace], restored.Changes.ModifiedFaceIds);
        Assert.Equal(["normal.smooth"], restored.Changes.ChangedAttributes);
    }

    [Fact]
    public async Task PolicyMarkSharpPreservesUnselectedEdgeValuesAndSupportsUnmarking()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "sharp-policy", "Sharp Policy", CancellationToken.None);
        var executor = new RekallAgeMeshOperationExecutor();
        var firstEdge = source.Topology.EdgeIds[0];
        var secondEdge = source.Topology.EdgeIds[1];

        var first = executor.Execute(source, new(
            "mark_sharp",
            RekallAgeGeometryDomain.Edge,
            [firstEdge],
            new JsonObject { ["sharp"] = true }));
        var second = executor.Execute(first.Mesh, new(
            "mark_sharp",
            RekallAgeGeometryDomain.Edge,
            [secondEdge],
            new JsonObject { ["sharp"] = true }));
        var restored = executor.Execute(second.Mesh, new(
            "mark_sharp",
            RekallAgeGeometryDomain.Edge,
            [firstEdge],
            new JsonObject { ["sharp"] = false }));

        var policy = Assert.Single(restored.Mesh.Attributes, item => item.Name == "normal.sharp");
        Assert.Equal(RekallAgeGeometryDomain.Edge, policy.Domain);
        Assert.Equal(RekallAgeGeometryValueType.Bool, policy.ValueType);
        Assert.False(policy.Values[0].GetBoolean());
        Assert.True(policy.Values[1].GetBoolean());
        Assert.All(policy.Values.Skip(2), value => Assert.False(value.GetBoolean()));
        Assert.Equal(source.Topology, restored.Mesh.Topology);
        Assert.Equal(source.Revision + 3, restored.Mesh.Revision);
        Assert.Equal([firstEdge], restored.Changes.ModifiedEdgeIds);
        Assert.Equal(["normal.sharp"], restored.Changes.ChangedAttributes);
    }

    [Fact]
    public async Task AutoSmoothClassifiesManifoldAnglesAndBoundariesDeterministically()
    {
        var executor = new RekallAgeMeshOperationExecutor();
        var box = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "auto-smooth-box", "Auto Smooth Box", CancellationToken.None);
        var plane = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "plane", "auto-smooth-plane", "Auto Smooth Plane", CancellationToken.None);

        var smoothBox = executor.Execute(box, new(
            "auto_smooth",
            RekallAgeGeometryDomain.Face,
            box.Topology.FaceIds,
            new JsonObject { ["angleDegrees"] = 180.0 }));
        var sharpBox = executor.Execute(box, new(
            "auto_smooth",
            RekallAgeGeometryDomain.Face,
            box.Topology.FaceIds,
            new JsonObject { ["angleDegrees"] = 89.0 }));
        var repeated = executor.Execute(box, new(
            "auto_smooth",
            RekallAgeGeometryDomain.Face,
            box.Topology.FaceIds,
            new JsonObject { ["angleDegrees"] = 89.0 }));
        var boundaryPlane = executor.Execute(plane, new(
            "auto_smooth",
            RekallAgeGeometryDomain.Face,
            plane.Topology.FaceIds,
            new JsonObject { ["angleDegrees"] = 180.0 }));

        Assert.All(Attribute(smoothBox.Mesh, "normal.sharp").Values, value => Assert.False(value.GetBoolean()));
        Assert.All(Attribute(sharpBox.Mesh, "normal.sharp").Values, value => Assert.True(value.GetBoolean()));
        Assert.Equal(
            Attribute(sharpBox.Mesh, "normal.sharp").Values.Select(value => value.GetRawText()),
            Attribute(repeated.Mesh, "normal.sharp").Values.Select(value => value.GetRawText()));
        Assert.All(Attribute(boundaryPlane.Mesh, "normal.sharp").Values, value => Assert.True(value.GetBoolean()));
    }

    [Fact]
    public void AutoSmoothMarksNonManifoldEdgesSharp()
    {
        var source = NonManifoldThreeFaceFan();
        var result = new RekallAgeMeshOperationExecutor().Execute(source, new(
            "auto_smooth",
            RekallAgeGeometryDomain.Face,
            source.Topology.FaceIds,
            new JsonObject { ["angleDegrees"] = 180.0 }));

        var sharp = Attribute(result.Mesh, "normal.sharp");
        Assert.True(sharp.Values[0].GetBoolean());
    }

    [Fact]
    public async Task WeightedNormalsSplitSharpFansHonorFlatFacesAndCompileTangentFrames()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box", "split-normal-box", "Split Normal Box", CancellationToken.None);
        var executor = new RekallAgeMeshOperationExecutor();
        var allSmooth = executor.Execute(source, new(
            "weighted_normals",
            RekallAgeGeometryDomain.Face,
            source.Topology.FaceIds,
            new JsonObject
            {
                ["attribute"] = "normal.authored",
                ["faceAreaWeight"] = 1.0,
                ["cornerAngleWeight"] = 1.0
            }));
        var sharpPolicy = executor.Execute(source, new(
            "auto_smooth",
            RekallAgeGeometryDomain.Face,
            source.Topology.FaceIds,
            new JsonObject { ["angleDegrees"] = 89.0 }));
        var split = executor.Execute(sharpPolicy.Mesh, new(
            "weighted_normals",
            RekallAgeGeometryDomain.Face,
            source.Topology.FaceIds,
            new JsonObject
            {
                ["attribute"] = "normal.authored",
                ["faceAreaWeight"] = 1.0,
                ["cornerAngleWeight"] = 1.0
            }));
        var flatPolicy = executor.Execute(source, new(
            "shade_faces",
            RekallAgeGeometryDomain.Face,
            [source.Topology.FaceIds[0]],
            new JsonObject { ["smooth"] = false }));
        var flat = executor.Execute(flatPolicy.Mesh, new(
            "weighted_normals",
            RekallAgeGeometryDomain.Face,
            source.Topology.FaceIds,
            new JsonObject
            {
                ["attribute"] = "normal.authored",
                ["faceAreaWeight"] = 1.0,
                ["cornerAngleWeight"] = 1.0
            }));

        var pointIndex = 0;
        var incidentCorners = source.Topology.CornerPointIndices
            .Select((point, corner) => (point, corner))
            .Where(item => item.point == pointIndex)
            .Select(item => item.corner)
            .ToArray();
        var smoothVectors = incidentCorners.Select(corner => Vector(Attribute(allSmooth.Mesh, "normal.authored"), corner)).ToArray();
        var splitVectors = incidentCorners.Select(corner => Vector(Attribute(split.Mesh, "normal.authored"), corner)).ToArray();
        Assert.All(smoothVectors.Skip(1), vector => AssertVectorNear(smoothVectors[0], vector));
        Assert.Equal(3, splitVectors.Select(Rounded).Distinct().Count());

        var firstFaceCorners = Enumerable.Range(
            source.Topology.FaceOffsets[0],
            source.Topology.FaceOffsets[1] - source.Topology.FaceOffsets[0]).ToArray();
        var flatNormal = FaceNormal(source, 0);
        Assert.All(firstFaceCorners, corner => AssertVectorNear(flatNormal, Vector(Attribute(flat.Mesh, "normal.authored"), corner)));
        Assert.All(Attribute(split.Mesh, "normal.authored").Values, value => AssertUnit(value));

        var compiled = new RekallAgeMeshCompiler().Compile(split.Mesh);
        Assert.Equal(split.Mesh.Topology.CornerIds.Count, compiled.Vertices.Count);
        for (var index = 0; index < compiled.Vertices.Count; index++)
        {
            AssertVectorNear(Vector(Attribute(split.Mesh, "normal.authored"), index), compiled.Vertices[index].Normal);
            var tangent = compiled.Vertices[index].Tangent;
            var normal = compiled.Vertices[index].Normal;
            Assert.InRange(Math.Abs(normal.X * tangent.X + normal.Y * tangent.Y + normal.Z * tangent.Z), 0, 1e-6);
        }
    }

    [Fact]
    public async Task GraphAutoSmoothFeedsPolicyAwareWeightedNormals()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "split-normal-graph",
            "Split Normal Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("smooth", "rekall.modeling.auto_smooth", 1, new JsonObject { ["angleDegrees"] = 89.0 }),
                new("normals", "rekall.modeling.weighted_normals", 1, new JsonObject
                {
                    ["attribute"] = "normal.authored",
                    ["faceAreaWeight"] = 1.0,
                    ["cornerAngleWeight"] = 1.0,
                    ["smoothAttribute"] = "normal.smooth",
                    ["sharpAttribute"] = "normal.sharp"
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-smooth", "box", "geometry", "smooth", "geometry"),
                new("smooth-normals", "smooth", "geometry", "normals", "geometry"),
                new("normals-output", "normals", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph,
            ["mesh"],
            RekallAgeModelingEvaluationBudget.Default,
            new(1, 0, "tests", "desktop"),
            CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var mesh = report.Outputs["mesh"];
        Assert.All(Attribute(mesh, "normal.sharp").Values, value => Assert.True(value.GetBoolean()));
        Assert.All(Attribute(mesh, "normal.authored").Values, AssertUnit);

        var catalog = RekallAgeModelingNodeCatalog.CreateDefault();
        var auto = Assert.Single(catalog.Descriptors, item => item.TypeId == "rekall.modeling.auto_smooth");
        Assert.Contains(auto.Parameters, item => item.ParameterId == "angleDegrees" && item.Minimum == 0 && item.Maximum == 180);
        var weighted = Assert.Single(catalog.Descriptors, item => item.TypeId == "rekall.modeling.weighted_normals");
        Assert.Equal(
            ["attribute", "faceAreaWeight", "cornerAngleWeight", "smoothAttribute", "sharpAttribute"],
            weighted.Parameters.Select(item => item.ParameterId));
    }

    [Fact]
    public async Task WeightedNormalsShadeSegmentedBevelWithFiniteUnitCornerVectors()
    {
        var graph = RekallAgeModelingGraphAsset.Create("weighted-normal-proof", "Weighted Normal Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("bevel", "rekall.modeling.bevel", 1, new JsonObject { ["width"] = 0.08, ["segments"] = 3 }),
                new("normals", "rekall.modeling.weighted_normals", 1, new JsonObject { ["attribute"] = "normal.weighted", ["faceAreaWeight"] = 1.0 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("box-bevel", "box", "geometry", "bevel", "geometry"), new("bevel-normal", "bevel", "geometry", "normals", "geometry"), new("normal-output", "normals", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(1, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var mesh = report.Outputs["mesh"];
        var normals = Assert.Single(mesh.Attributes, item => item.Name == "normal.weighted");
        Assert.Equal(RekallAgeGeometryDomain.Corner, normals.Domain);
        Assert.Equal(mesh.Topology.CornerIds.Count, normals.Values.Count);
        Assert.All(normals.Values, value => Assert.InRange(Math.Sqrt(value[0].GetDouble() * value[0].GetDouble() + value[1].GetDouble() * value[1].GetDouble() + value[2].GetDouble() * value[2].GetDouble()), 0.999999, 1.000001));
    }

    private static RekallAgeGeometryAttribute Attribute(RekallAgeMeshAsset mesh, string name) =>
        Assert.Single(mesh.Attributes, item => item.Name == name);

    private static RekallAgeGeometryVector3 Vector(RekallAgeGeometryAttribute attribute, int index) =>
        new(
            attribute.Values[index][0].GetDouble(),
            attribute.Values[index][1].GetDouble(),
            attribute.Values[index][2].GetDouble());

    private static RekallAgeGeometryVector3 FaceNormal(RekallAgeMeshAsset mesh, int faceIndex)
    {
        var topology = mesh.Topology;
        var start = topology.FaceOffsets[faceIndex];
        var end = topology.FaceOffsets[faceIndex + 1];
        var normal = new RekallAgeGeometryVector3(0, 0, 0);
        for (var corner = start; corner < end; corner++)
        {
            var next = corner + 1 == end ? start : corner + 1;
            var a = topology.Positions[topology.CornerPointIndices[corner]];
            var b = topology.Positions[topology.CornerPointIndices[next]];
            normal = new(
                normal.X + (a.Y - b.Y) * (a.Z + b.Z),
                normal.Y + (a.Z - b.Z) * (a.X + b.X),
                normal.Z + (a.X - b.X) * (a.Y + b.Y));
        }
        var length = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        return new(normal.X / length, normal.Y / length, normal.Z / length);
    }

    private static string Rounded(RekallAgeGeometryVector3 vector) =>
        $"{Math.Round(vector.X, 6)},{Math.Round(vector.Y, 6)},{Math.Round(vector.Z, 6)}";

    private static void AssertVectorNear(RekallAgeGeometryVector3 expected, RekallAgeGeometryVector3 actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, 1e-6);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 1e-6);
        Assert.InRange(Math.Abs(expected.Z - actual.Z), 0, 1e-6);
    }

    private static void AssertUnit(System.Text.Json.JsonElement value)
    {
        var x = value[0].GetDouble();
        var y = value[1].GetDouble();
        var z = value[2].GetDouble();
        Assert.True(double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z));
        Assert.InRange(Math.Sqrt(x * x + y * y + z * z), 0.999999, 1.000001);
    }

    private static RekallAgeMeshAsset NonManifoldThreeFaceFan() =>
        RekallAgeMeshAsset.Create(
            "normal-non-manifold",
            "Normal Non Manifold",
            new RekallAgeMeshTopology(
                PointIds: [1, 2, 3, 4, 5],
                Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1)],
                EdgeIds: [11, 12, 13, 14, 15, 16, 17],
                EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0), new(1, 3), new(3, 0), new(1, 4), new(4, 0)],
                FaceIds: [21, 22, 23],
                FaceOffsets: [0, 3, 6, 9],
                CornerIds: [31, 32, 33, 34, 35, 36, 37, 38, 39],
                CornerPointIndices: [0, 1, 2, 1, 0, 3, 0, 1, 4],
                CornerEdgeIndices: [0, 1, 2, 0, 4, 3, 0, 5, 6]));
}
