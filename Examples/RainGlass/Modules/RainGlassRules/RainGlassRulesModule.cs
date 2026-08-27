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
/// Rekall.Material.emissiveStrength every frame - the same authored-per-frame-property pattern
/// Ridgebreaker used to drive a wheel motor, here repurposing an existing numeric material slot as
/// a plain "time" value the custom rainglass.frag shader reads back out of Draw.EmissiveFactors.w.
/// EmissiveStrength was chosen over the tighter-clamped RoughnessFactor (renderer-clamped to
/// [0.04, 1], which froze the animation after one second) specifically because it keeps a wide
/// [0, 64] clamp range; time still wraps well inside that range (never above 50) so a real elapsed-
/// time value never approaches the clamp and silently stalls again.
/// </summary>
public sealed class RainGlassSystem : IRekallAgeRuntimeModuleSystem
{
    private const string MaterialType = "Rekall.Material";
    private const string GlassEntityId = "rainGlass";
    private const double WrapPeriodSeconds = 50;

    public string Id => nameof(RainGlassSystem);

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var wrappedSeconds = world.ElapsedTime.TotalSeconds % WrapPeriodSeconds;
        var updatedWorld = world.UpdateEntity(GlassEntityId, entity =>
            entity.WithComponentNumber(MaterialType, "emissiveStrength", wrappedSeconds));

        return ValueTask.FromResult(updatedWorld);
    }
}
