using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;
using System.Text.Json.Nodes;

namespace Game.Modules.AetherfallRules;

public sealed class AetherfallRulesSystem : IRekallAgeRuntimeModuleSystem
{
    public string Id => nameof(AetherfallRulesSystem);

    public int Priority => -5;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        if (world.WasInputActionPressed("reset"))
        {
            world = AetherfallReset.Apply(world);
            return ValueTask.FromResult(PresentationSimulation.Update(world, context));
        }

        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is not null && world.WasInputActionPressed("pause"))
        {
            var phase = warden.ComponentString(AetherfallConstants.WardenStateType, "phase", "playing");
            world = world.UpdateEntity(warden.Id, entity => entity.WithComponentString(
                AetherfallConstants.WardenStateType,
                "phase",
                phase == "paused" ? "playing" : "paused"));
            return ValueTask.FromResult(PresentationSimulation.Update(world, context));
        }

        if (warden?.ComponentString(AetherfallConstants.WardenStateType, "phase", "playing") != "playing")
        {
            return ValueTask.FromResult(PresentationSimulation.Update(world, context));
        }

        world = WardenSimulation.Update(world, context);
        world = WorldInteractionSimulation.Update(world, context);
        world = EncounterSimulation.Update(world, context);
        world = HostileSimulation.Update(world, context);
        world = UpdateMantlePose(world, context);
        world = UpdateWardenRigPose(world, context);
        world = UpdateWardenAnimationMixer(world);
        warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is not null
            && warden.ComponentNumber(AetherfallConstants.WardenStateType, "integrity", 100) <= 0)
        {
            world = world.UpdateEntity(warden.Id, entity => entity.WithComponentString(
                AetherfallConstants.WardenStateType,
                "phase",
                "defeat"));
        }

        return ValueTask.FromResult(PresentationSimulation.Update(world, context));
    }

    private static RekallAgeRuntimeWorld UpdateMantlePose(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var mantle = world.FindEntity("Warden Deformable Mantle");
        if (mantle is null)
        {
            return world;
        }

        var phase = context.ElapsedTime.TotalSeconds * 2.4;
        var swayX = Math.Sin(phase) * 0.38;
        var liftZ = (Math.Cos(phase * 0.73) - 1) * 0.10;
        return world.UpdateEntity(mantle.Id, entity => entity.UpdateComponent("Rekall.SkeletonPose", properties =>
        {
            properties["skinIndex"] = 0;
            properties["joints"] = new JsonArray(
                Joint(0, 0, 0),
                Joint(1, swayX, liftZ));
            return properties;
        }));

        static JsonObject Joint(int index, double x, double z) => new()
        {
            ["jointIndex"] = index,
            ["matrix"] = new JsonArray(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                x, 0, z, 1)
        };
    }

    private static RekallAgeRuntimeWorld UpdateWardenRigPose(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is null)
        {
            return world;
        }

        var time = context.ElapsedTime.TotalSeconds;
        var velocityX = warden.ComponentNumber(AetherfallConstants.WardenStateType, "velocityX");
        var velocityZ = warden.ComponentNumber(AetherfallConstants.WardenStateType, "velocityZ");
        var movement = Math.Clamp(
            Math.Sqrt(velocityX * velocityX + velocityZ * velocityZ) / AetherfallConstants.WardenSpeed,
            0,
            1);
        var facingX = warden.ComponentNumber(AetherfallConstants.WardenStateType, "facingX");
        var facingZ = warden.ComponentNumber(AetherfallConstants.WardenStateType, "facingZ", 1);
        var facingYaw = Math.Atan2(facingX, facingZ);
        var walkPhase = time * 7.5;
        var stepBob = (0.5 - 0.5 * Math.Cos(walkPhase * 2)) * 0.045 * movement;
        var dashBlend = Math.Clamp(
            (warden.ComponentNumber(AetherfallConstants.WardenStateType, "dashCooldown") - 0.52) / 0.33,
            0,
            1);
        var breath = Math.Sin(time * 2.2) * 0.018;
        var weightShift = Math.Sin(time * 1.35 + 0.4) * 0.035;
        var glance = Math.Sin(time * 0.61 + 0.8) * 0.026;
        var pelvis = System.Numerics.Matrix4x4.CreateRotationY((float)facingYaw)
            * System.Numerics.Matrix4x4.CreateRotationX((float)(dashBlend * -0.16))
            * System.Numerics.Matrix4x4.CreateRotationZ((float)(-weightShift * (1 - movement * 0.7)))
            * System.Numerics.Matrix4x4.CreateTranslation(0, (float)(stepBob - dashBlend * 0.08), 0);
        var chest = System.Numerics.Matrix4x4.CreateRotationX((float)(breath + movement * 0.045 + dashBlend * -0.12))
            * System.Numerics.Matrix4x4.CreateRotationZ((float)weightShift);
        return world.UpdateEntity(warden.Id, entity => entity.UpdateComponent("Rekall.RigPose", properties =>
        {
            properties["assetId"] = "aetherfall.warden.rig";
            properties["skinIndex"] = 0;
            properties["jointDeltas"] = new JsonArray(
                Pose("pelvis", pelvis),
                Pose("chest", chest),
                Pose("head", System.Numerics.Matrix4x4.CreateRotationY((float)glance)
                    * System.Numerics.Matrix4x4.CreateRotationZ((float)(-weightShift * 0.45))));
            return properties;
        }));

        static JsonObject Pose(string jointId, System.Numerics.Matrix4x4 matrix) => new()
        {
            ["jointId"] = jointId,
            ["matrix"] = new JsonArray(
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44)
        };
    }

    private static RekallAgeRuntimeWorld UpdateWardenAnimationMixer(RekallAgeRuntimeWorld world)
    {
        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is null)
        {
            return world;
        }
        var velocityX = warden.ComponentNumber(AetherfallConstants.WardenStateType, "velocityX");
        var velocityZ = warden.ComponentNumber(AetherfallConstants.WardenStateType, "velocityZ");
        var movement = Math.Clamp(
            Math.Sqrt(velocityX * velocityX + velocityZ * velocityZ) / AetherfallConstants.WardenSpeed,
            0,
            1);
        return world.UpdateEntity(warden.Id, entity => entity.UpsertComponent(
            "Rekall.AnimationMixer",
            new JsonObject
            {
                ["playing"] = true,
                ["layers"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = "presentation",
                        ["clip"] = "aetherfall-warden-presentation",
                        ["weight"] = 1,
                        ["loopMode"] = "pingpong"
                    },
                    new JsonObject
                    {
                        ["name"] = "guarded-idle",
                        ["clip"] = "aetherfall-warden-idle",
                        ["weight"] = 1 - movement,
                        ["loopMode"] = "loop"
                    },
                    new JsonObject
                    {
                        ["name"] = "armored-walk",
                        ["clip"] = "aetherfall-warden-walk",
                        ["weight"] = movement,
                        ["speed"] = 1 + movement * 0.08,
                        ["loopMode"] = "loop"
                    })
            }));
    }
}
