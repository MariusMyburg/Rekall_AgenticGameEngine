using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class CelestialRotationSystemTests
{
    [Fact]
    public async Task DefaultRuntimeAppliesDeterministicCelestialRotation()
    {
        var planet = new RekallAgeRuntimeEntity(
            "planet",
            "Planet",
            ["celestial"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.CelestialRotation",
                    new JsonObject
                    {
                        ["siderealPeriodSeconds"] = 60,
                        ["tiltDegrees"] = 23.5,
                        ["azimuthDegrees"] = 5,
                        ["initialLongitudeDegrees"] = 10,
                        ["timeScale"] = 60
                    })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [planet],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 30, CancellationToken.None);

        var updatedPlanet = Assert.Single(result.World.Entities);
        Assert.Equal(23.5, updatedPlanet.Transform.Rotation3D.X, precision: 3);
        Assert.Equal(195, updatedPlanet.Transform.Rotation3D.Y, precision: 3);
        Assert.Equal(0, updatedPlanet.Transform.Rotation3D.Z, precision: 3);
        Assert.Contains("runtime.celestial.rotation", result.World.SystemsRun);
    }
}
