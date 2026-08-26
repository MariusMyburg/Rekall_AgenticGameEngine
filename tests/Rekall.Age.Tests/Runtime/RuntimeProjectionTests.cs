using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeProjectionTests
{
    [Fact]
    public void ParticleEmitterProjectsCompleteGenericAuthoredContractIntoViewportFrame()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Emitter", ["vfx", "upper"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = 1,
                    ["y"] = 2,
                    ["z"] = 3,
                    ["yaw"] = 45
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject
                {
                    ["layer"] = "effects"
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.ParticleEmitter3D", new JsonObject
                {
                    ["enabled"] = true,
                    ["simulationSpace"] = "local",
                    ["capacity"] = 2048,
                    ["spawnRate"] = 120.5,
                    ["bursts"] = new JsonArray
                    {
                        new JsonObject { ["time"] = 0.25, ["count"] = 32 },
                        new JsonObject { ["time"] = 1.5, ["count"] = 7 }
                    },
                    ["lifetime"] = 4.5,
                    ["seed"] = 123456,
                    ["velocityDirection"] = new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0.25 },
                    ["velocityConeDegrees"] = 35,
                    ["minimumSpeed"] = 2,
                    ["maximumSpeed"] = 9,
                    ["gravity"] = new JsonObject { ["x"] = 0, ["y"] = -9.81, ["z"] = 0.5 },
                    ["drag"] = 0.4,
                    ["sizeCurve"] = new JsonArray
                    {
                        new JsonObject { ["time"] = 0, ["value"] = 0.25 },
                        new JsonObject { ["time"] = 1, ["value"] = 2 }
                    },
                    ["colorCurve"] = new JsonArray
                    {
                        new JsonObject { ["time"] = 0, ["color"] = "#ff8040ff" },
                        new JsonObject { ["time"] = 1, ["color"] = "#10203000" }
                    },
                    ["drawMode"] = "quad",
                    ["lit"] = true,
                    ["emissiveIntensity"] = 6.25,
                    ["softParticleFade"] = 0.75,
                    ["texture"] = "asset_particle",
                    ["flipbookColumns"] = 8,
                    ["flipbookRows"] = 4,
                    ["flipbookFramesPerSecond"] = 24,
                    ["blendMode"] = "additive",
                    ["priority"] = 9,
                    ["visibilityDistance"] = 85
                })));

        var projected = new RekallAgeRuntimeProjectionBuilder().Project(new RekallAgeRuntimeWorldBuilder().Build(scene));
        var emitter = Assert.Single(projected.Subsystems.Rendering.ParticleEmitters);
        var frameEmitter = Assert.Single(new RekallAgeRuntimeRenderFrameBuilder()
            .Build(projected, 640, 360, debugOverlay: false).ParticleEmitters);

        Assert.Equal("Emitter", emitter.EntityName);
        Assert.True(emitter.Enabled);
        Assert.Equal("local", emitter.SimulationSpace);
        Assert.Equal(2048, emitter.Capacity);
        Assert.Equal(120.5, emitter.SpawnRate);
        Assert.Equal([(0.25, 32), (1.5, 7)], emitter.Bursts.Select(item => (item.TimeSeconds, item.Count)));
        Assert.Equal(4.5, emitter.LifetimeSeconds);
        Assert.Equal(123456u, emitter.DeterministicSeed);
        Assert.Equal((0d, 1d, 0.25d), (emitter.VelocityDirection.X, emitter.VelocityDirection.Y, emitter.VelocityDirection.Z));
        Assert.Equal(35, emitter.VelocityConeDegrees);
        Assert.Equal(2, emitter.MinimumSpeed);
        Assert.Equal(9, emitter.MaximumSpeed);
        Assert.Equal((0d, -9.81d, 0.5d), (emitter.Gravity.X, emitter.Gravity.Y, emitter.Gravity.Z));
        Assert.Equal(0.4, emitter.Drag);
        Assert.Equal([(0d, 0.25d), (1d, 2d)], emitter.SizeCurve.Select(item => (item.NormalizedAge, item.Value)));
        Assert.Equal([(0d, "#ff8040ff"), (1d, "#10203000")], emitter.ColorCurve.Select(item => (item.NormalizedAge, item.Color)));
        Assert.Equal("quad", emitter.DrawMode);
        Assert.True(emitter.Lit);
        Assert.Equal(6.25, emitter.EmissiveIntensity);
        Assert.Equal(0.75, emitter.SoftParticleFade);
        Assert.Equal("asset_particle", emitter.TextureAssetId);
        Assert.Equal((8, 4, 24d), (emitter.FlipbookColumns, emitter.FlipbookRows, emitter.FlipbookFramesPerSecond));
        Assert.Equal("additive", emitter.BlendMode);
        Assert.Equal(9, emitter.Priority);
        Assert.Equal(85, emitter.VisibilityDistance);
        Assert.Equal("effects", emitter.Layer);
        Assert.Equal((1d, 2d, 3d, 45d), (frameEmitter.Transform.X, frameEmitter.Transform.Y, frameEmitter.Transform.Z, frameEmitter.Transform.RotationY));
        Assert.Equal("effects", frameEmitter.Layer);
    }
}
