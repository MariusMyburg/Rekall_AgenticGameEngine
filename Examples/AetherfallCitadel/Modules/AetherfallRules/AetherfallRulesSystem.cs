using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;
using System.Text.Json.Nodes;

namespace Game.Modules.AetherfallRules;

public sealed class AetherfallRulesSystem : IRekallAgeRuntimeModuleSystem
{
    public string Id => nameof(AetherfallRulesSystem);

    public int Priority => 10;

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
        var breath = Math.Sin(time * 2.2) * 0.018;
        var weightShift = Math.Sin(time * 1.35 + 0.4) * 0.035;
        var stride = Math.Sin(time * 1.55) * 0.028;
        var glance = Math.Sin(time * 0.61 + 0.8) * 0.026;
        var chest = System.Numerics.Matrix4x4.CreateRotationX((float)breath)
            * System.Numerics.Matrix4x4.CreateRotationZ((float)weightShift);
        return world.UpdateEntity(warden.Id, entity => entity.UpdateComponent("Rekall.RigPose", properties =>
        {
            properties["assetId"] = "aetherfall.warden.rig";
            properties["skinIndex"] = 0;
            properties["jointDeltas"] = new JsonArray(
                Pose("pelvis", System.Numerics.Matrix4x4.CreateRotationZ((float)(-weightShift * 0.35))),
                Pose("chest", chest),
                Pose("head", System.Numerics.Matrix4x4.CreateRotationY((float)glance)
                    * System.Numerics.Matrix4x4.CreateRotationZ((float)(-weightShift * 0.45))),
                Pose("upper_arm_l", System.Numerics.Matrix4x4.CreateRotationX((float)(-stride * 0.55))
                    * System.Numerics.Matrix4x4.CreateRotationZ((float)(weightShift * 0.7))),
                Pose("forearm_l", System.Numerics.Matrix4x4.CreateRotationX((float)(breath * 1.4 + stride * 0.35))),
                Pose("upper_arm_r", System.Numerics.Matrix4x4.CreateRotationX((float)(stride * 0.55))
                    * System.Numerics.Matrix4x4.CreateRotationZ((float)(weightShift * 0.7))),
                Pose("forearm_r", System.Numerics.Matrix4x4.CreateRotationX((float)(breath * 1.4 - stride * 0.35))),
                Pose("leg_l", System.Numerics.Matrix4x4.CreateRotationX((float)stride)
                    * System.Numerics.Matrix4x4.CreateRotationZ((float)(-weightShift * 0.22))),
                Pose("leg_r", System.Numerics.Matrix4x4.CreateRotationX((float)-stride)
                    * System.Numerics.Matrix4x4.CreateRotationZ((float)(-weightShift * 0.22))));
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
}
