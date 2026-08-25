using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class CurveAuthoringTests
{
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
}
