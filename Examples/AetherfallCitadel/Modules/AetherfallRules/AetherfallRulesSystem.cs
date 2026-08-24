using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

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
            return ValueTask.FromResult(AetherfallReset.Apply(world));
        }

        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is not null && world.WasInputActionPressed("pause"))
        {
            var phase = warden.ComponentString(AetherfallConstants.WardenStateType, "phase", "playing");
            world = world.UpdateEntity(warden.Id, entity => entity.WithComponentString(
                AetherfallConstants.WardenStateType,
                "phase",
                phase == "paused" ? "playing" : "paused"));
            return ValueTask.FromResult(world);
        }

        if (warden?.ComponentString(AetherfallConstants.WardenStateType, "phase", "playing") != "playing")
        {
            return ValueTask.FromResult(world);
        }

        world = WardenSimulation.Update(world, context);
        world = WorldInteractionSimulation.Update(world, context);
        world = EncounterSimulation.Update(world, context);
        world = HostileSimulation.Update(world, context);
        warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is not null
            && warden.ComponentNumber(AetherfallConstants.WardenStateType, "integrity", 100) <= 0)
        {
            world = world.UpdateEntity(warden.Id, entity => entity.WithComponentString(
                AetherfallConstants.WardenStateType,
                "phase",
                "defeat"));
        }

        return ValueTask.FromResult(world);
    }
}
