using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class XrPoseSystemTests
{
    [Fact]
    public async Task XrPoseSourceAppliesHeadsetPoseToTransform()
    {
        var camera = new RekallAgeRuntimeEntity(
            "camera",
            "VR Camera",
            ["camera"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent("Rekall.Transform3D", new JsonObject()),
                new RekallAgeRuntimeComponent("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo",
                    ["xrViewConfiguration"] = "primary-stereo"
                }),
                new RekallAgeRuntimeComponent("Rekall.XrPoseSource", new JsonObject
                {
                    ["source"] = "head",
                    ["active"] = true
                })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [camera],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(
                    XrPoses:
                    [
                        new RekallAgeRuntimeXrPose(
                            "head",
                            IsTracked: true,
                            X: 1,
                            Y: 1.75,
                            Z: -2,
                            Pitch: 5,
                            Yaw: 15,
                            Roll: -1)
                    ]));

        var updated = Assert.Single(result.World.Entities);
        Assert.Equal(1, updated.Transform.Position3D.X, precision: 6);
        Assert.Equal(1.75, updated.Transform.Position3D.Y, precision: 6);
        Assert.Equal(-2, updated.Transform.Position3D.Z, precision: 6);
        Assert.Equal(5, updated.Transform.Rotation3D.X, precision: 6);
        Assert.Equal(15, updated.Transform.Rotation3D.Y, precision: 6);
        Assert.Contains("runtime.xr.pose", result.World.SystemsRun);
        var xrPose = Assert.Single(result.World.Subsystems.Xr.Poses);
        Assert.Equal("camera", xrPose.EntityId);
        Assert.Equal("head", xrPose.Source);
    }

    [Fact]
    public async Task XrActionSourceProjectsControllerActions()
    {
        var controller = new RekallAgeRuntimeEntity(
            "left-controller",
            "Left Controller",
            ["controller"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent("Rekall.XrController", new JsonObject
                {
                    ["hand"] = "left",
                    ["active"] = true
                })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [controller],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(
                    XrActions:
                    [
                        new RekallAgeRuntimeXrAction("left", "trigger", 0.9, true, true, false)
                    ]));

        var controllerView = Assert.Single(result.World.Subsystems.Xr.Controllers);
        Assert.Equal("left-controller", controllerView.EntityId);
        Assert.Equal("left", controllerView.Hand);
        var action = Assert.Single(result.World.Subsystems.Xr.Actions);
        Assert.Equal("trigger", action.Name);
        Assert.Equal(0.9, action.Value, precision: 6);
        Assert.True(action.WasPressed);
    }

    [Fact]
    public async Task RuntimeInspectionReportsXrSubsystemCounts()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = Rekall.Age.World.RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(Rekall.Age.World.RekallAgeEntityDocument.Create("VrRig", ["xr"])
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.XrRig", new JsonObject())))
            .AddEntity(Rekall.Age.World.RekallAgeEntityDocument.Create("HeadCamera", ["camera"])
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo"
                }))
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.XrPoseSource", new JsonObject
                {
                    ["source"] = "head"
                })));
        await new Rekall.Age.World.RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var context = new Rekall.Age.Core.Commands.RekallAgeCommandContext(
            "test",
            Rekall.Age.Core.Transactions.RekallAgeTransaction.Begin("inspect xr"),
            CancellationToken.None);
        var result = await new Rekall.Age.Runtime.Commands.InspectSceneRuntimeCommand().ExecuteAsync(
            new Rekall.Age.Runtime.Commands.InspectSceneRuntimeRequest(
                root,
                "Main",
                1,
                [
                    new RekallAgeRuntimeInputFrame(
                        XrPoses:
                        [
                            new RekallAgeRuntimeXrPose("head", true, X: 0, Y: 1.8, Z: 0)
                        ])
                ]),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(1, result.Value.XrRigCount);
        Assert.Equal(1, result.Value.XrPoseCount);
    }
}
