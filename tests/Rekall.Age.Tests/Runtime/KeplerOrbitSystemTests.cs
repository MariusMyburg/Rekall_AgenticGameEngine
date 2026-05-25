using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class KeplerOrbitSystemTests
{
    [Fact]
    public async Task DefaultRuntimeAdvancesKeplerOrbitComponentsAroundTheirParentBody()
    {
        var sol = new RekallAgeRuntimeEntity(
            "sol",
            "Sol",
            ["celestial"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.CelestialBody",
                    new JsonObject
                    {
                        ["bodyId"] = "Sol",
                        ["massKg"] = 1.98847e30
                    })
            ]);
        var probe = new RekallAgeRuntimeEntity(
            "probe",
            "Probe",
            ["spacecraft"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.KeplerOrbit",
                    new JsonObject
                    {
                        ["parentBodyId"] = "Sol",
                        ["semiMajorAxisKm"] = 10,
                        ["eccentricity"] = 0,
                        ["periodSeconds"] = 1,
                        ["distanceScale"] = 1
                    })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [sol, probe],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 15, CancellationToken.None);

        var updatedProbe = result.World.Entities.Single(entity => entity.Name == "Probe");
        Assert.Equal(0, updatedProbe.Transform.Position3D.X, precision: 1);
        Assert.Equal(10, updatedProbe.Transform.Position3D.Z, precision: 1);
        Assert.Contains("runtime.celestial.kepler", result.World.SystemsRun);
    }

    [Fact]
    public async Task KeplerOrbitHonorsAuthoredTimeScaleForReadableOrreries()
    {
        var sol = new RekallAgeRuntimeEntity(
            "sol",
            "Sol",
            ["celestial"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.CelestialBody",
                    new JsonObject
                    {
                        ["bodyId"] = "Sol",
                        ["massKg"] = 1.98847e30
                    })
            ]);
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
                    "Rekall.KeplerOrbit",
                    new JsonObject
                    {
                        ["parentBodyId"] = "Sol",
                        ["semiMajorAxisKm"] = 20,
                        ["eccentricity"] = 0,
                        ["periodSeconds"] = 4,
                        ["distanceScale"] = 1,
                        ["timeScale"] = 4
                    })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [sol, planet],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 15, CancellationToken.None);

        var updatedPlanet = result.World.Entities.Single(entity => entity.Name == "Planet");
        Assert.Equal(0, updatedPlanet.Transform.Position3D.X, precision: 1);
        Assert.Equal(20, updatedPlanet.Transform.Position3D.Z, precision: 1);
    }

    [Fact]
    public async Task KeplerOrbitResolvesMoonPositionsRelativeToUpdatedParentPlanet()
    {
        var sol = new RekallAgeRuntimeEntity(
            "sol",
            "Sol",
            ["celestial"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.CelestialBody",
                    new JsonObject
                    {
                        ["bodyId"] = "Sol",
                        ["massKg"] = 1.98847e30
                    })
            ]);
        var earth = new RekallAgeRuntimeEntity(
            "earth",
            "Earth",
            ["celestial"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.CelestialBody",
                    new JsonObject
                    {
                        ["bodyId"] = "Earth",
                        ["massKg"] = 5.9722e24
                    }),
                new RekallAgeRuntimeComponent(
                    "Rekall.KeplerOrbit",
                    new JsonObject
                    {
                        ["parentBodyId"] = "Sol",
                        ["semiMajorAxisKm"] = 10,
                        ["eccentricity"] = 0,
                        ["periodSeconds"] = 1,
                        ["distanceScale"] = 1
                    })
            ]);
        var luna = new RekallAgeRuntimeEntity(
            "luna",
            "Luna",
            ["celestial"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.CelestialBody",
                    new JsonObject
                    {
                        ["bodyId"] = "Luna",
                        ["massKg"] = 7.342e22
                    }),
                new RekallAgeRuntimeComponent(
                    "Rekall.KeplerOrbit",
                    new JsonObject
                    {
                        ["parentBodyId"] = "Earth",
                        ["semiMajorAxisKm"] = 2,
                        ["eccentricity"] = 0,
                        ["periodSeconds"] = 1,
                        ["distanceScale"] = 1
                    })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [sol, earth, luna],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 15, CancellationToken.None);

        var updatedEarth = result.World.Entities.Single(entity => entity.Name == "Earth");
        var updatedLuna = result.World.Entities.Single(entity => entity.Name == "Luna");
        Assert.Equal(10, updatedEarth.Transform.Position3D.Z, precision: 1);
        Assert.Equal(12, updatedLuna.Transform.Position3D.Z, precision: 1);
    }
}
