using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.AetherfallRules;

internal static class AetherfallEntityFactory
{
    public static RekallAgeRuntimeEntity CreateWardenPulse(
        long frameIndex,
        RekallAgeRuntimeVector3 origin,
        double directionX,
        double directionZ)
    {
        var id = $"warden-pulse-{frameIndex}";
        return RekallAgeRuntimeModuleSdk.CreateEntity(id, id)
            .WithTag("projectile")
            .WithTag("warden.projectile")
            .WithPosition3D(origin)
            .WithScale3D(new RekallAgeRuntimeVector3(0.32, 0.32, 0.65))
            .UpsertComponent(
                AetherfallConstants.ProjectileStateType,
                new JsonObject
                {
                    ["faction"] = "warden",
                    ["damage"] = AetherfallConstants.PulseDamage,
                    ["velocityX"] = directionX * AetherfallConstants.PulseSpeed,
                    ["velocityZ"] = directionZ * AetherfallConstants.PulseSpeed,
                    ["remainingLifetime"] = AetherfallConstants.PulseLifetimeSeconds,
                    ["radius"] = AetherfallConstants.PulseRadius,
                    ["visualRole"] = "pulse"
                })
            .UpsertComponent(
                "Rekall.GeometryPrimitive",
                new JsonObject
                {
                    ["primitive"] = "sphere",
                    ["color"] = "#8ffcff"
                });
    }

    public static RekallAgeRuntimeEntity CreateDashEffect(
        long frameIndex,
        RekallAgeRuntimeVector3 origin)
    {
        var id = $"dash-effect-{frameIndex}";
        return RekallAgeRuntimeModuleSdk.CreateEntity(id, id)
            .WithTag("effect")
            .WithPosition3D(new RekallAgeRuntimeVector3(origin.X, 0.18, origin.Z))
            .WithScale3D(new RekallAgeRuntimeVector3(0.4, 0.12, 0.4))
            .UpsertComponent(
                AetherfallConstants.EffectStateType,
                new JsonObject
                {
                    ["kind"] = "dash-ring",
                    ["age"] = 0,
                    ["lifetime"] = 0.3,
                    ["startScale"] = 0.4,
                    ["endScale"] = 2.2,
                    ["colorRole"] = "aether"
                })
            .UpsertComponent(
                "Rekall.GeometryPrimitive",
                new JsonObject
                {
                    ["primitive"] = "torus",
                    ["color"] = "#79f5ff88"
                });
    }
}
