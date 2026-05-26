using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class StereoRenderPlanCommandTests
{
    [Fact]
    public async Task InspectStereoRenderPlanReportsMultiviewReadySharedGeometry()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = 6 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo",
                    ["stereoRenderMode"] = "single-pass-multiview",
                    ["interpupillaryDistance"] = 0.064
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("inspect stereo plan"),
            CancellationToken.None);

        var result = await new InspectStereoRenderPlanCommand().ExecuteAsync(
            new InspectStereoRenderPlanRequest(root, "Main", Width: 1024, Height: 512),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.StereoEnabled);
        Assert.Equal("single-pass-multiview", result.Value.RenderMode);
        Assert.True(result.Value.PreferSinglePassMultiview);
        Assert.True(result.Value.SharedGeometryBuffers);
        Assert.Equal(2, result.Value.EyeCount);
        Assert.Equal(2, result.Value.EyeUniformCount);
        Assert.True(result.Value.VertexCount > 0);
        Assert.True(result.Value.DrawCount > 0);
        Assert.Equal(result.Value.DrawCount * 2, result.Value.CurrentPreviewDrawSubmissions);
        Assert.Equal(result.Value.DrawCount, result.Value.TargetMultiviewDrawSubmissions);
        Assert.Contains(result.Value.Recommendations, item => item.Contains("multiview", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.Value.Warnings);
    }
}
