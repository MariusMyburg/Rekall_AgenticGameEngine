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
        world = WardenSimulation.Update(world, context);
        world = WorldInteractionSimulation.Update(world, context);
        return ValueTask.FromResult(world);
    }
}
