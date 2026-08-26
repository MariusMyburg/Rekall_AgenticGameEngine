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
}
