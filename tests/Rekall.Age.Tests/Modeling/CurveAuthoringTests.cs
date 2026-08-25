using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class CurveAuthoringTests
{
    [Fact]
    public void LineAndCircleBuildersProduceDeterministicOpenAndCyclicCurves()
    {
        var operations = new RekallAgeCurveOperations();

        var line = Assert.Single(operations.Line(new(0, 0, 0), new(0, 3, 0), 0.5, 1.5).Splines);
        var circle = Assert.Single(operations.Circle(new(2, 3, 4), 2, 8, "xz").Splines);

        Assert.False(line.Cyclic);
        Assert.Equal(2, line.Points.Count);
        Assert.Equal(0.5, line.Points[0].Radius);
        Assert.Equal(1.5, line.Points[1].Radius);
        Assert.True(circle.Cyclic);
        Assert.Equal(8, circle.Points.Count);
        Assert.Equal(new RekallAgeGeometryVector3(4, 3, 4), circle.Points[0].Position);
        Assert.All(circle.Points, point => Assert.Equal(2, Distance(point.Position, new(2, 3, 4)), 8));
    }

    [Fact]
    public void ReverseAndArcLengthResamplePreserveShapeRadiusTiltAndProvenance()
    {
        var operations = new RekallAgeCurveOperations();
        var source = operations.Line(new(0, 0, 0), new(0, 4, 0), 1, 2, 0, Math.PI);

        var resampled = Assert.Single(operations.Resample(source, 5).Splines);
        var reversed = Assert.Single(operations.Reverse(new([resampled])).Splines);

        Assert.Equal([0d, 1d, 2d, 3d, 4d], resampled.Points.Select(point => point.Position.Y));
        Assert.Equal(1.5, resampled.Points[2].Radius, 8);
        Assert.Equal(Math.PI / 2, resampled.Points[2].TiltRadians, 8);
        Assert.Equal(resampled.Points[^1].Position, reversed.Points[0].Position);
        Assert.Equal(new RekallAgeGeometryVector3(0, -1, 0), reversed.Points[0].Tangent);
        Assert.Equal(2UL, reversed.Points[0].SourceStartControlPointId);
        Assert.Equal(1UL, reversed.Points[0].SourceEndControlPointId);
    }

    [Fact]
    public async Task CurveBuilderAndOperationsComposeThroughTypedGraphPorts()
    {
        var graph = RekallAgeModelingGraphAsset.Create("curve-operations", "Curve Operations",
            [
                new("line", "rekall.modeling.curve.line", 1, new JsonObject { ["start"] = new JsonArray(0d, 0d, 0d), ["end"] = new JsonArray(0d, 4d, 0d) }),
                new("resample", "rekall.modeling.curve.resample", 1, new JsonObject { ["count"] = 9 }),
                new("reverse", "rekall.modeling.curve.reverse", 1, new JsonObject()),
                new("sweep", "rekall.modeling.curve.profile_sweep", 1, new JsonObject { ["profile"] = "circle", ["profileSegments"] = 8, ["radius"] = 0.2 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("line-resample", "line", "curve", "resample", "curve"),
                new("resample-reverse", "resample", "curve", "reverse", "curve"),
                new("reverse-sweep", "reverse", "curve", "sweep", "curve"),
                new("sweep-output", "sweep", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(72, result.Outputs["mesh"].Topology.PointIds.Count);
        Assert.True(new RekallAgeMeshValidator().Validate(result.Outputs["mesh"]).IsValid);
    }

    [Fact]
    public void VersionedCurveDocumentRoundTripsStableIdsAndValidates()
    {
        var curve = BezierCurve();

        var json = JsonSerializer.Serialize(curve, RekallAgeModelingJson.Options);
        var roundTrip = JsonSerializer.Deserialize<RekallAgeCurveAsset>(json, RekallAgeModelingJson.Options);

        Assert.Equal(json, JsonSerializer.Serialize(roundTrip, RekallAgeModelingJson.Options));
        Assert.True(new RekallAgeCurveValidator().Validate(roundTrip!).IsValid);
        Assert.Equal([11UL, 12UL], roundTrip!.Splines[0].ControlPoints.Select(point => point.ControlPointId));
    }

    [Fact]
    public async Task CurveStorePersistsVersionedResourcesUnderModelingCurves()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-curve-tests", Guid.NewGuid().ToString("N"));
        var store = new RekallAgeCurveAssetStore();
        try
        {
            await store.SaveAsync(root, BezierCurve(), CancellationToken.None);
            var loaded = await store.LoadAsync(root, "curve.arch", CancellationToken.None);

            Assert.Equal(Path.Combine(Path.GetFullPath(root), "Modeling", "Curves", "curve.arch.age.curve.json"), store.GetCurvePath(root, "curve.arch"));
            Assert.Equal(store.Serialize(BezierCurve()), store.Serialize(loaded));
            Assert.Equal(["curve.arch"], store.ListAssetIds(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidatorRejectsDuplicateStableControlPointIds()
    {
        var curve = BezierCurve();
        var duplicate = curve with
        {
            Splines = [curve.Splines[0] with { ControlPoints = [curve.Splines[0].ControlPoints[0], curve.Splines[0].ControlPoints[1] with { ControlPointId = 11 }] }]
        };

        var report = new RekallAgeCurveValidator().Validate(duplicate);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_CURVE_CONTROL_POINT_ID_DUPLICATE");
    }

    [Fact]
    public void CubicBezierEvaluationIsDeterministicAndInterpolatesRadiusTiltAndProvenance()
    {
        var evaluator = new RekallAgeCurveEvaluator();

        var first = evaluator.Evaluate(BezierCurve(), 4);
        var second = evaluator.Evaluate(BezierCurve(), 4);

        var spline = Assert.Single(first.Splines);
        Assert.Equal(5, spline.Points.Count);
        Assert.Equal(spline.Points, Assert.Single(second.Splines).Points);
        Assert.Equal(new RekallAgeGeometryVector3(0, 0, 0), spline.Points[0].Position);
        Assert.Equal(new RekallAgeGeometryVector3(2, 1, 0), spline.Points[^1].Position);
        Assert.Equal(1.5, spline.Points[2].Radius, 8);
        Assert.Equal(Math.PI / 4, spline.Points[2].TiltRadians, 8);
        Assert.Equal(11UL, spline.Points[2].SourceStartControlPointId);
        Assert.Equal(12UL, spline.Points[2].SourceEndControlPointId);
    }

    [Fact]
    public async Task CurveResourceFeedsProfileSweepWithRadiusTiltUvsAndMaterial()
    {
        var document = JsonSerializer.SerializeToNode(BezierCurve(), RekallAgeModelingJson.Options)!.AsObject();
        var graph = RekallAgeModelingGraphAsset.Create("curve-resource-sweep", "Curve Resource Sweep",
            [
                new("curve", "rekall.modeling.curve.source", 1, new JsonObject { ["document"] = document, ["resolution"] = 6 }),
                new("sweep", "rekall.modeling.curve.profile_sweep", 1, new JsonObject { ["profile"] = "circle", ["profileSegments"] = 8, ["radius"] = 0.2, ["materialAssetId"] = "material.trim" }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("curve-sweep", "curve", "curve", "sweep", "curve"), new("sweep-output", "sweep", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        var mesh = result.Outputs["mesh"];
        Assert.Equal(56, mesh.Topology.PointIds.Count);
        Assert.Contains(mesh.Attributes, item => item.Semantic == "texcoord-0");
        Assert.Contains(mesh.MaterialSlots, item => item.MaterialAssetId == "material.trim");
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
    }

    [Fact]
    public async Task CyclicPolylineSweepJoinsItsSeamWithoutCapsOrBoundaryEdges()
    {
        var curve = RekallAgeCurveAsset.Create("curve.loop", "Loop",
            [new(20, RekallAgeCurveSplineKind.Poly, true,
            [
                PolyPoint(21, 0, 0), PolyPoint(22, 2, 0), PolyPoint(23, 1, 2)
            ])]);
        var document = JsonSerializer.SerializeToNode(curve, RekallAgeModelingJson.Options)!.AsObject();
        var graph = RekallAgeModelingGraphAsset.Create("curve-loop-sweep", "Curve Loop Sweep",
            [
                new("curve", "rekall.modeling.curve.source", 1, new JsonObject { ["document"] = document, ["resolution"] = 2 }),
                new("sweep", "rekall.modeling.curve.profile_sweep", 1, new JsonObject { ["profile"] = "rectangle", ["profileWidth"] = 0.2, ["profileHeight"] = 0.1 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("curve-sweep", "curve", "curve", "sweep", "curve"), new("sweep-output", "sweep", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        var validation = new RekallAgeMeshValidator().Validate(result.Outputs["mesh"]);
        Assert.True(validation.IsValid);
        Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
        Assert.Equal(24, result.Outputs["mesh"].Topology.FaceIds.Count);
    }

    private static RekallAgeCurveAsset BezierCurve() => RekallAgeCurveAsset.Create(
        "curve.arch", "Arch",
        [new(7, RekallAgeCurveSplineKind.CubicBezier, false,
        [
            new(11, new(0, 0, 0), new(0, 0, 0), new(0.5, 1, 0), 1, 0),
            new(12, new(2, 1, 0), new(1.5, 0, 0), new(2, 1, 0), 2, Math.PI / 2)
        ])]);

    private static RekallAgeCurveControlPoint PolyPoint(ulong id, double x, double y) =>
        new(id, new(x, y, 0), new(x, y, 0), new(x, y, 0));

    private static double Distance(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
}
