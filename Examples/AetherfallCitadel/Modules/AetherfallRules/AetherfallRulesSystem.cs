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
        var delta = System.Numerics.Matrix4x4.CreateRotationX((float)breath)
            * System.Numerics.Matrix4x4.CreateRotationZ((float)weightShift);
        return world.UpdateEntity(warden.Id, entity => entity.UpdateComponent("Rekall.RigPose", properties =>
        {
            properties["assetId"] = "aetherfall.warden.rig";
            properties["skinIndex"] = 0;
            properties["jointDeltas"] = new JsonArray(new JsonObject
            {
                ["jointId"] = "chest",
                ["matrix"] = new JsonArray(
                    delta.M11, delta.M12, delta.M13, delta.M14,
                    delta.M21, delta.M22, delta.M23, delta.M24,
                    delta.M31, delta.M32, delta.M33, delta.M34,
                    delta.M41, delta.M42, delta.M43, delta.M44)
            });
            return properties;
        }));
    }
}
