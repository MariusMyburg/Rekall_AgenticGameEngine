using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Ordnance", Description =
    "A shot in flight, or the mark one leaves. Missiles travel and detonate on arrival; beams " +
    "are instantaneous and only draw; trails follow a missile; flashes are the impact itself. " +
    "Spawned by CombatSystem and reaped by OrdnanceSystem - never authored into a scene.")]
public sealed class Ordnance : RekallAgeComponent
{
    [RekallAgeProperty(AllowedValues = ["missile", "beam", "trail", "flash"])]
    public string Kind { get; init; } = "missile";

    [RekallAgeProperty]
    public string OwnerId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TargetId { get; init; } = string.Empty;

    /// <summary>
    /// A missile's trail is a separate entity. Line segments are expressed in the entity's own
    /// space, so a trail sharing the round's transform would be dragged along behind it and
    /// scaled by the round's size - it has to sit on its own untransformed entity.
    /// </summary>
    [RekallAgeProperty]
    public string TrailId { get; init; } = string.Empty;

    [RekallAgeProperty(Minimum = 0, Maximum = 100000)]
    public double Damage { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 10000)]
    public double Speed { get; init; } = 110;

    [RekallAgeProperty(Minimum = 0, Maximum = 600)]
    public double Life { get; init; }

    [RekallAgeProperty(Minimum = 0.01, Maximum = 600)]
    public double MaxLife { get; init; } = 6;

    /// <summary>
    /// Set the step a missile reaches its target. CombatSystem collects the damage that step;
    /// this system reaps the round and leaves a flash the step after.
    /// </summary>
    [RekallAgeProperty]
    public bool Detonated { get; init; }
}

/// <summary>
/// Everything a shot does between leaving the rail and going out.
///
/// Damage deliberately does not live here. CombatSystem stays the single owner of hull and
/// shield arithmetic, and a missile that arrives only raises a flag; letting two systems both
/// subtract from the same hull is how a ship ends up dying twice or not at all. The ordering
/// is what makes that work: this system runs at 32, immediately before combat at 33, so a
/// round that lands is accounted for in the same step it lands in.
/// </summary>
public sealed class OrdnanceSystem : IRekallAgeRuntimeModuleSystem
{
    internal const string OrdnanceType = "Game.Modules.FleetRules.Ordnance";
    private const string LineSegmentsType = "Rekall.LineSegments";
    private const string HaloType = "Rekall.HaloRenderer";
    private const string MaterialType = "Rekall.Material";

    /// <summary>A round that arrives inside this distance has hit.</summary>
    private const double ImpactDistance = 6.0;

    /// <summary>How long a trail lingers once its round is gone.</summary>
    private const double TrailLingerSeconds = 0.45;

    public string Id => "game.ordnance";

    public int Priority => 32;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var rounds = world.Entities
            .Where(entity => entity.FindComponent(OrdnanceType) is not null)
            .ToArray();
        if (rounds.Length == 0)
        {
            return ValueTask.FromResult(world);
        }

        var delta = context.DeltaTime.TotalSeconds;
        var byId = world.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var expired = new HashSet<string>(StringComparer.Ordinal);
        var flashes = new List<RekallAgeRuntimeEntity>();
        var updated = new Dictionary<string, RekallAgeRuntimeEntity>(StringComparer.Ordinal);
        var trailSteps = new Dictionary<string, (RekallAgeRuntimeVector3 From, RekallAgeRuntimeVector3 To)>(
            StringComparer.Ordinal);
        var trailsToRetire = new HashSet<string>(StringComparer.Ordinal);
        var sequence = 0;

        // Elapsed ticks, not FrameIndex: a rendered frame can run several fixed steps and
        // FrameIndex does not change between them, so ids keyed on it collided.
        var stamp = context.ElapsedTime.Ticks;

        foreach (var round in rounds)
        {
            var kind = round.ComponentString(OrdnanceType, "kind", "missile") ?? "missile";
            var life = round.ComponentNumber(OrdnanceType, "life") + delta;
            var maxLife = Math.Max(0.01, round.ComponentNumber(OrdnanceType, "maxLife", 6));
            var trailId = round.ComponentString(OrdnanceType, "trailId", string.Empty) ?? string.Empty;

            // A round that landed last step has already been paid for. Reap it, leave the flash
            // where it went off, and let its trail fade out behind it.
            if (round.ComponentBoolean(OrdnanceType, "detonated"))
            {
                expired.Add(round.Id);
                if (trailId.Length > 0)
                {
                    trailsToRetire.Add(trailId);
                }

                flashes.Add(OrdnanceFactory.Flash(
                    $"ord_flash_{stamp}_{sequence++}",
                    round.Transform.Position3D,
                    round.ComponentString(MaterialType, "emissiveColor", "#bfe9ff") ?? "#bfe9ff"));
                continue;
            }

            if (life >= maxLife)
            {
                expired.Add(round.Id);
                if (trailId.Length > 0)
                {
                    trailsToRetire.Add(trailId);
                }

                continue;
            }

            if (kind is "beam" or "flash" or "trail")
            {
                // These only age. All three fade over their life so a burst of fire reads as a
                // rhythm rather than a strobe.
                updated[round.Id] = Fade(round, kind, life, maxLife)
                    .WithComponentNumber(OrdnanceType, "life", life);
                continue;
            }

            var targetId = round.ComponentString(OrdnanceType, "targetId", string.Empty) ?? string.Empty;
            if (!byId.TryGetValue(targetId, out var target) || CombatRules.IsDestroyed(target))
            {
                // The target died mid-flight. The round runs on until it times out rather than
                // blinking out of existence, which is both truthful and better looking.
                updated[round.Id] = round.WithComponentNumber(OrdnanceType, "life", life);
                continue;
            }

            var from = round.Transform.Position3D;
            var to = target.Transform.Position3D;
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var dz = to.Z - from.Z;
            var distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            var speed = Math.Max(1, round.ComponentNumber(OrdnanceType, "speed", 110));
            var step = speed * delta;
            var arrived = distance <= Math.Max(ImpactDistance, step);
            var next = arrived
                ? to
                : new RekallAgeRuntimeVector3(
                    from.X + (dx / distance * step),
                    from.Y + (dy / distance * step),
                    from.Z + (dz / distance * step));

            updated[round.Id] = round
                .WithPosition3D(next)
                .WithComponentNumber(OrdnanceType, "life", life)
                .WithComponentBoolean(OrdnanceType, "detonated", arrived);

            if (trailId.Length > 0)
            {
                trailSteps[trailId] = (from, next);
            }
        }

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count + flashes.Count);
        foreach (var entity in world.Entities)
        {
            if (expired.Contains(entity.Id))
            {
                continue;
            }

            var next = updated.TryGetValue(entity.Id, out var replacement) ? replacement : entity;

            if (trailSteps.TryGetValue(entity.Id, out var leg))
            {
                next = OrdnanceFactory.ExtendTrail(next, leg.From, leg.To);
            }

            if (trailsToRetire.Contains(entity.Id))
            {
                // Stop growing and start fading, from wherever the trail had got to.
                next = next.WithComponentNumber(
                    OrdnanceType,
                    "maxLife",
                    next.ComponentNumber(OrdnanceType, "life") + TrailLingerSeconds);
            }

            entities.Add(next);
        }

        var existing = entities.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        entities.AddRange(flashes.Where(item => existing.Add(item.Id)));
        return ValueTask.FromResult(world with { Entities = entities });
    }

    /// <summary>Drops the alpha of a round's colour as it ages out.</summary>
    private static RekallAgeRuntimeEntity Fade(
        RekallAgeRuntimeEntity round,
        string kind,
        double life,
        double maxLife)
    {
        var remaining = Math.Clamp(1.0 - (life / maxLife), 0, 1);
        var alpha = ((int)Math.Round(remaining * 255)).ToString("x2", CultureInfo.InvariantCulture);
        var componentType = kind == "flash" ? HaloType : LineSegmentsType;
        var colour = round.ComponentString(componentType, "color", "#bfe9ffff") ?? "#bfe9ffff";

        return colour.Length < 7
            ? round
            : round.WithComponentString(componentType, "color", colour[..7] + alpha);
    }
}

/// <summary>
/// Builds the entities a shot is made of.
///
/// These are spawned into the world at runtime rather than pooled in the scene, which the
/// engine supports directly through the SDK's CreateEntity/AddEntity. A missile keeps the same
/// entity for its whole flight, so its geometry is parsed once and the frame builder's
/// identity-keyed mesh cache keeps hitting.
/// </summary>
internal static class OrdnanceFactory
{
    private const string OrdnanceType = OrdnanceSystem.OrdnanceType;

    /// <summary>
    /// Segments of history a trail keeps. The trail is what actually reads at fleet range - the
    /// round itself is a couple of pixels - so this is a legibility number, not a decorative one.
    /// </summary>
    private const int TrailLimit = 16;

    /// <summary>A round and the trail that follows it, in that order.</summary>
    public static IEnumerable<RekallAgeRuntimeEntity> Missile(
        string id,
        RekallAgeRuntimeVector3 origin,
        string ownerId,
        string targetId,
        double damage,
        double speed,
        string colour)
    {
        var trailId = id + "_t";

        yield return Base(id, "Missile", origin, 2.2)
            .UpsertComponent("Rekall.GeometryPrimitive", Props(("primitive", "sphere"), ("color", colour)))
            .UpsertComponent("Rekall.MeshRenderer", Props(
                ("active", true), ("castShadows", false), ("receiveShadows", false)))
            .UpsertComponent("Rekall.Material", Props(
                ("baseColor", colour), ("emissiveColor", colour),
                ("emissiveStrength", 14.0), ("roughnessFactor", 1.0)))
            .UpsertComponent(OrdnanceType, Props(
                ("kind", "missile"), ("ownerId", ownerId), ("targetId", targetId),
                ("trailId", trailId), ("damage", damage), ("speed", speed),
                ("life", 0.0), ("maxLife", 7.0), ("detonated", false)));

        // Untransformed, because its segments are world-space. Outlives the round by a little
        // so the streak is still there after the warhead goes off.
        yield return Base(trailId, "Missile Trail", new RekallAgeRuntimeVector3(0, 0, 0), 1)
            .UpsertComponent("Rekall.LineSegments", Props(
                ("segments", new JsonArray()), ("thickness", 1.4), ("color", colour + "dd")))
            .UpsertComponent("Rekall.Material", Props(
                ("baseColor", colour), ("emissiveColor", colour), ("emissiveStrength", 7.0)))
            .UpsertComponent(OrdnanceType, Props(
                ("kind", "trail"), ("ownerId", id), ("life", 0.0), ("maxLife", 9.0)));
    }

    public static RekallAgeRuntimeEntity Beam(
        string id,
        RekallAgeRuntimeVector3 from,
        RekallAgeRuntimeVector3 to,
        string colour)
    {
        var segments = new JsonArray { Segment(from, to) };
        return Base(id, "Beam", new RekallAgeRuntimeVector3(0, 0, 0), 1)
            .UpsertComponent("Rekall.LineSegments", Props(
                ("segments", segments), ("thickness", 1.1), ("color", colour + "ff")))
            .UpsertComponent("Rekall.Material", Props(
                ("baseColor", colour), ("emissiveColor", colour), ("emissiveStrength", 10.0)))
            .UpsertComponent(OrdnanceType, Props(
                ("kind", "beam"), ("life", 0.0), ("maxLife", 0.14)));
    }

    public static RekallAgeRuntimeEntity Flash(string id, RekallAgeRuntimeVector3 at, string colour)
    {
        return Base(id, "Impact", at, 1)
            .UpsertComponent("Rekall.HaloRenderer", Props(
                ("radius", 5.5), ("segments", 32.0), ("rings", 3.0),
                ("falloff", 2.0), ("intensity", 6.0), ("color", colour + "ff"),
                ("facingMode", "camera")))
            .UpsertComponent("Rekall.Material", Props(
                ("baseColor", colour), ("emissiveColor", colour), ("emissiveStrength", 12.0)))
            .UpsertComponent(OrdnanceType, Props(
                ("kind", "flash"), ("life", 0.0), ("maxLife", 0.35)));
    }

    /// <summary>Adds this step's travel to a trail and drops the oldest segment.</summary>
    public static RekallAgeRuntimeEntity ExtendTrail(
        RekallAgeRuntimeEntity trail,
        RekallAgeRuntimeVector3 from,
        RekallAgeRuntimeVector3 to)
    {
        return trail.UpdateComponent("Rekall.LineSegments", properties =>
        {
            var segments = properties["segments"] as JsonArray ?? new JsonArray();
            var kept = new JsonArray();
            // Oldest first, so trimming the front is what ages the tail off.
            var skip = Math.Max(0, segments.Count - (TrailLimit - 1));
            for (var index = skip; index < segments.Count; index++)
            {
                kept.Add(segments[index]!.DeepClone());
            }

            kept.Add(Segment(from, to));
            properties["segments"] = kept;
            return properties;
        });
    }

    private static JsonObject Segment(RekallAgeRuntimeVector3 from, RekallAgeRuntimeVector3 to) =>
        new()
        {
            ["fromX"] = from.X, ["fromY"] = from.Y, ["fromZ"] = from.Z,
            ["toX"] = to.X, ["toY"] = to.Y, ["toZ"] = to.Z,
        };

    private static RekallAgeRuntimeEntity Base(
        string id,
        string name,
        RekallAgeRuntimeVector3 at,
        double scale) =>
        RekallAgeRuntimeModuleSdk.CreateEntity(id, name) with
        {
            Visible = true,
            Tags = ["ordnance"],
            Transform = RekallAgeRuntimeTransform.Identity with
            {
                Position3D = at,
                Scale3D = new RekallAgeRuntimeVector3(scale, scale, scale),
            },
        };

    private static JsonObject Props(params (string Key, object Value)[] values)
    {
        var properties = new JsonObject();
        foreach (var (key, value) in values)
        {
            properties[key] = value switch
            {
                string text => JsonValue.Create(text),
                bool flag => JsonValue.Create(flag),
                double number => JsonValue.Create(number),
                JsonNode node => node,
                _ => JsonValue.Create(value.ToString()),
            };
        }

        return properties;
    }
}
