using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class ScenePerformanceBudgetCommandTests
{
    [Fact]
    public async Task InspectScenePerformanceBudgetReportsGeometryAndVrInvocationBudget()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo",
                    ["stereoRenderMode"] = "single-pass-multiview"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject
                {
                    ["primitive"] = "cube"
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("perf budget"), CancellationToken.None);

        var result = await new InspectScenePerformanceBudgetCommand().ExecuteAsync(
            new InspectScenePerformanceBudgetRequest(root, "Main", Profile: "vr90"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("vr90", result.Value.Profile);
        Assert.Equal(90, result.Value.TargetFramesPerSecond);
        Assert.True(result.Value.StereoEnabled);
        Assert.True(result.Value.UsesSinglePassMultiview);
        Assert.Equal(1, result.Value.DrawCalls);
        Assert.Equal(1, result.Value.EstimatedDrawInvocations);
        Assert.True(result.Value.Triangles > 0);
        Assert.Empty(result.Value.Blockers);
    }

    [Fact]
    public async Task InspectScenePerformanceBudgetBlocksMobileWhenDrawCallsExceedBudget()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]);
        for (var i = 0; i < 275; i++)
        {
            scene = scene.AddEntity(RekallAgeEntityDocument.Create($"Cube {i}", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject
                {
                    ["primitive"] = "cube"
                })));
        }

        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("mobile perf budget"), CancellationToken.None);

        var result = await new InspectScenePerformanceBudgetCommand().ExecuteAsync(
            new InspectScenePerformanceBudgetRequest(root, "Main", Profile: "mobile60"),
            context);

        Assert.False(result.Ok);
        Assert.Equal("mobile60", result.Value.Profile);
        Assert.Equal(275, result.Value.DrawCalls);
        Assert.Contains(result.Value.Blockers, blocker => blocker.Contains("draw", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Code == "REKALL_SCENE_PERFORMANCE_BUDGET_EXCEEDED");
    }
}
