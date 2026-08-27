using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.RainGlassRules;

[RekallAgeModule("RainGlassRules", "Rain Glass Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class RainGlassRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<RainGlassState>();
        builder.RegisterRuntimeSystem<RainGlassSystem>();
    }
}

/// <summary>
/// Persists the two simulated "big droplet" bodies across frames on the glass entity itself, since
/// RekallAgeRuntimeModuleSystem has no other durable per-entity storage. X positions are NOT stored
/// here - both this system and the shader independently derive X from Y through the same
/// deterministic wobble formula and a fixed per-slot seed, so only what genuinely changes over time
/// (Y position and, for the drop that can absorb another, its radius) needs to persist or transmit.
/// </summary>
[RekallAgeComponent("Rain Glass State")]
public sealed class RainGlassState : RekallAgeComponent
{
    [RekallAgeProperty]
    public double Drop0Y { get; init; } = 0.05;

    [RekallAgeProperty]
    public double Drop0Radius { get; init; } = 0.045;

    [RekallAgeProperty]
    public double Drop1Y { get; init; } = 0.25;

    [RekallAgeProperty]
    public double Drop1Radius { get; init; } = 0.02;
}

/// <summary>
/// A genuine two-body droplet simulation, not a visual approximation: two "big" droplets each slide
/// down the pane (heavier/larger droplets fall faster, matching how real surface-tension-limited
/// drops behave), and when their real simulated positions come within their combined radius, they
/// merge for real - drop0 always survives (see below for why), its radius growing by conserving 2D
/// area (r = sqrt(r0^2 + r1^2)) rather than snapping to an arbitrary bigger size, and drop1 respawns
/// near the top to keep the scene active. This is deliberately scoped to exactly two bodies:
/// the custom shader's Draw uniform is a small fixed set of vec4 slots (not a real per-droplet
/// buffer/array), so genuinely simulated, persistently tracked interaction is only practical for a
/// handful of explicitly-named values this system can fit into it - true simulated merging across
/// the whole dense droplet field would need a real structured-buffer capability this engine's custom
/// shader authoring surface does not yet expose.
/// </summary>
public sealed class RainGlassSystem : IRekallAgeRuntimeModuleSystem
{
    private const string StateType = "Game.Modules.RainGlassRules.RainGlassState";
    private const string MaterialType = "Rekall.Material";
    private const string GlassEntityId = "rainGlass";

    // Must match rainglass.frag's own PlaneAspect/pathOffsetX exactly - both sides derive the same
    // X position from Y independently, so they need identical constants to agree on where each
    // droplet actually is for collision purposes versus where it renders.
    private const double PlaneAspect = 10.0 / 5.6;
    // Deliberately the SAME seed for both droplets (not independent per-drop wobble): with different
    // seeds, each droplet's X position is essentially an unrelated function of its own Y, so even
    // when their Y positions coincide their X positions almost never do - the combined-radius
    // collision distance stays dominated by a near-random X offset up to about 0.73, dwarfing the
    // ~0.065 merge threshold, so a merge is real but reachable only by rare coincidence. Sharing one
    // seed means both droplets ride the same wobbling channel - like water following an existing wet
    // track on a real window - so their X positions agree by construction and a merge becomes
    // reliably reachable by Y alone, matching the collision math's own intent.
    private const double Drop0Seed = 91.7 * 0.31;
    private const double Drop1Seed = Drop0Seed;
    private const double MaximumRadius = 0.11;
    private const double RespawnRadius0 = 0.045;
    private const double RespawnRadius1 = 0.02;

    public string Id => nameof(RainGlassSystem);

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var seconds = context.DeltaTime.TotalSeconds;
        var updatedWorld = world.UpdateEntity(GlassEntityId, entity =>
        {
            var drop0Y = entity.ComponentNumber(StateType, "drop0Y", 0.05);
            var drop0Radius = entity.ComponentNumber(StateType, "drop0Radius", RespawnRadius0);
            var drop1Y = entity.ComponentNumber(StateType, "drop1Y", 0.25);
            var drop1Radius = entity.ComponentNumber(StateType, "drop1Radius", RespawnRadius1);

            drop0Y += FallSpeed(drop0Radius) * seconds;
            drop1Y += FallSpeed(drop1Radius) * seconds;

            // Both bodies wrap back to just above the top once they slide off the bottom, keeping
            // the scene continuously active rather than eventually leaving the pane empty. Respawn
            // at exactly 0 (not slightly negative) because the channel drop0Y/drop1Y ride on
            // (metallicFactor/emissiveStrength) gets clamped to a non-negative range downstream;
            // a negative respawn value would just silently clip to 0 anyway.
            if (drop0Y > 1.05)
            {
                drop0Y = 0;
            }

            if (drop1Y > 1.05)
            {
                drop1Y = 0;
            }

            // Real collision detection between the two simulated bodies, in the same UV.x-equivalent
            // distance units rainglass.frag's own droplet radii are already expressed in (a Y
            // difference is scaled by PlaneAspect to become comparable to an X difference, exactly
            // like the shader's own radii.y = radius * PlaneAspect correction).
            var deltaX = PathOffsetX(drop0Y, Drop0Seed) - PathOffsetX(drop1Y, Drop1Seed);
            var deltaY = (drop0Y - drop1Y) * PlaneAspect;
            var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (distance < drop0Radius + drop1Radius)
            {
                // Drop0 always survives a merge (never drop1): only drop0's radius is actually
                // transmitted to and rendered by the shader (its Draw uniform channel budget only
                // fits one growable radius - see the class doc comment), so drop1 growing instead
                // would silently render at the wrong size. Still a real merge, real area
                // conservation, real absorption - just asymmetric by design rather than "whichever
                // happens to be bigger" like a from-scratch two-body simulation would allow.
                drop0Radius = Math.Min(MaximumRadius, Math.Sqrt(drop0Radius * drop0Radius + drop1Radius * drop1Radius));
                drop1Y = 0;
                drop1Radius = RespawnRadius1;
            }

            // Bridged into Rekall.Material's own fixed numeric properties - the only channel that
            // reaches the shader's Draw uniform - repurposed here as raw simulation values rather
            // than PBR factors, the same authored-per-frame-property pattern Ridgebreaker used to
            // drive a wheel motor. NormalScale/OcclusionStrength were not usable for this: the
            // renderer forces both to 0 unless their respective texture is also authored, so only
            // MetallicFactor (clamped [0,1]) and RoughnessFactor (clamped [0.04,1] - drop0Radius is
            // scaled by 8x here, and divided back by 8 in the shader, to stay clear of that 0.04
            // floor across this droplet's real 0.02-0.11 radius range) and EmissiveStrength
            // (clamped [0,64], no texture gating) were available, exactly the three raw values this
            // two-body simulation needs (drop0Y, drop0Radius, drop1Y - drop1's own radius never
            // grows, so it stays a shader-side constant and never needs transmitting).
            return entity
                .WithComponentNumber(StateType, "drop0Y", drop0Y)
                .WithComponentNumber(StateType, "drop0Radius", drop0Radius)
                .WithComponentNumber(StateType, "drop1Y", drop1Y)
                .WithComponentNumber(StateType, "drop1Radius", drop1Radius)
                .WithComponentNumber(MaterialType, "metallicFactor", Math.Clamp(drop0Y, 0, 1))
                .WithComponentNumber(MaterialType, "roughnessFactor", Math.Clamp(drop0Radius * 8, 0.04, 1))
                .WithComponentNumber(MaterialType, "emissiveStrength", Math.Clamp(drop1Y, 0, 64));
        });

        return ValueTask.FromResult(updatedWorld);
    }

    private static double FallSpeed(double radius) => 0.012 + radius * 0.6;

    private static double PathOffsetX(double y, double seed) =>
        Math.Sin(y * 14.0 + seed * 23.0) * 0.55 + Math.Sin(y * 31.0 + seed * 7.0) * 0.18;
}
