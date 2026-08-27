using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.RainGlassRules;

[RekallAgeModule("RainGlassRules", "Rain Glass Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class RainGlassRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterRuntimeSystem<RainGlassSystem>();
    }
}

/// <summary>
/// Drives the rain-glass shader's animation. AGE has no dedicated engine-time uniform reaching
/// shaders directly, so this system writes world-elapsed seconds into the glass entity's own
/// Rekall.Material.roughnessFactor every frame - the same authored-per-frame-property pattern
/// Ridgebreaker used to drive a wheel motor, here repurposing an existing numeric material slot as
/// a plain "time" value the custom rainglass.frag shader reads back out of Draw.MaterialFactors.y.
/// </summary>
public sealed class RainGlassSystem : IRekallAgeRuntimeModuleSystem
{
    private const string MaterialType = "Rekall.Material";
    private const string GlassEntityId = "rainGlass";

    public string Id => nameof(RainGlassSystem);

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var elapsedSeconds = world.ElapsedTime.TotalSeconds;
        var updatedWorld = world.UpdateEntity(GlassEntityId, entity =>
            entity.WithComponentNumber(MaterialType, "roughnessFactor", elapsedSeconds));

        return ValueTask.FromResult(updatedWorld);
    }
}
