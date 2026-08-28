using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Ordnance", Description =
    "A shot in flight, or the mark one leaves. Missiles travel and detonate on arrival; beams " +
    "are instantaneous and only draw; trails follow a missile; flashes are the impact itself; " +
    "audio reports retain a spatial one-shot after its short-lived visual has faded. " +
    "Spawned by CombatSystem and reaped by OrdnanceSystem - never authored into a scene.")]
public sealed class Ordnance : RekallAgeComponent
{
    [RekallAgeProperty(AllowedValues = ["missile", "beam", "trail", "flash", "audio"])]
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

            if (kind == "audio")
            {
                updated[round.Id] = round.WithComponentNumber(OrdnanceType, "life", life);
                continue;
            }

            if (kind is "flash" or "trail")
            {
                // These only age. Both fade over their life so a burst of fire reads as a
                // rhythm rather than a strobe.
                updated[round.Id] = Fade(round, kind, life, maxLife)
                    .WithComponentNumber(OrdnanceType, "life", life);
                continue;
            }

            if (kind == "beam")
            {
                // A beam is a line between two ships, and both of them are moving. Fixing it
                // at the muzzle it was fired from leaves it hanging in space behind whatever
                // fired it, which is what it looked like.
                var aged = Fade(round, kind, life, maxLife)
                    .WithComponentNumber(OrdnanceType, "life", life);

                var ownerId = round.ComponentString(OrdnanceType, "ownerId", string.Empty) ?? string.Empty;
                var beamTargetId = round.ComponentString(OrdnanceType, "targetId", string.Empty) ?? string.Empty;
                updated[round.Id] = byId.TryGetValue(ownerId, out var owner)
                    && byId.TryGetValue(beamTargetId, out var struck)
                        ? OrdnanceFactory.Reaim(
                            aged, owner.Transform.Position3D, struck.Transform.Position3D)
                        : aged;
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
    public const string HeavyBeamClip = "asset_stellar-dominion-heavy-beam_434b3a06";
    public const string HeavyImpactClip = "asset_stellar-dominion-heavy-impact_13a0bf75";

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
                ("life", 0.0), ("maxLife", 7.0), ("detonated", false)))
            .UpsertComponent("Rekall.AudioEmitter", Emitter(0.4, 5000))
            .UpsertComponent("Rekall.ProceduralAudioClip", Props(
                // Launch: mostly noise, rising, so it reads as a rail rather than a beam.
                ("waveform", "noise"),
                ("durationSeconds", 0.45),
                ("startFrequency", 220.0), ("endFrequency", 1000.0),
                ("sweep", "exponential"),
                ("attack", 0.01), ("decay", 0.10),
                ("sustain", 0.4), ("release", 0.30),
                ("noiseMix", 0.85), ("harmonics", 1.0),
                ("amplitude", 0.4), ("seed", 63.0)));

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
        string colour,
        string ownerId,
        string targetId)
    {
        // Line segments are expressed in the entity's own space and the renderer applies the
        // entity transform to them. The entity sits at the muzzle so the report pans from the
        // firing ship, so the segment has to run from the local origin to the target's offset -
        // holding world coordinates here would translate the whole beam twice.
        var segments = new JsonArray { Segment(Zero, Delta(from, to)) };
        return Base(id, "Beam", from, 1)
            .UpsertComponent("Rekall.LineSegments", Props(
                ("segments", segments), ("thickness", 1.1), ("color", colour + "ff")))
            .UpsertComponent("Rekall.Material", Props(
                ("baseColor", colour), ("emissiveColor", colour), ("emissiveStrength", 10.0)))
            .UpsertComponent(OrdnanceType, Props(
                ("kind", "beam"), ("life", 0.0), ("maxLife", 0.30),
                ("ownerId", ownerId), ("targetId", targetId)));
    }

    public static RekallAgeRuntimeEntity Flash(string id, RekallAgeRuntimeVector3 at, string colour)
    {
        return Base(id, "Impact", at, 2.4)
            .UpsertComponent("Rekall.GeometryPrimitive", Props(
                ("primitive", "sphere"), ("color", "#fff4dd")))
            .UpsertComponent("Rekall.MeshRenderer", Props(
                ("active", true), ("castShadows", false), ("receiveShadows", false)))
            .UpsertComponent("Rekall.Material", Props(
                ("baseColor", "#fff4dd"), ("emissiveColor", colour),
                ("emissiveStrength", 18.0), ("roughnessFactor", 0.18)))
            .UpsertComponent("Rekall.PointLight", Props(
                ("color", colour), ("intensity", 14.0), ("range", 22.0),
                ("priority", 120.0), ("shadowPriority", 0.0), ("castShadows", false)))
            .UpsertComponent("Rekall.ParticleEmitter3D", Props(
                ("role", "kinetic-impact"), ("enabled", true),
                ("simulationSpace", "world"), ("capacity", 96.0),
                ("spawnRate", 190.0), ("lifetime", 0.48), ("seed", 971.0),
                ("velocityDirection", new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0 }),
                ("velocityConeDegrees", 180.0), ("minimumSpeed", 8.0), ("maximumSpeed", 38.0),
                ("gravity", new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 }),
                ("drag", 1.8),
                ("sizeCurve", new JsonArray {
                    new JsonObject { ["time"] = 0, ["value"] = 0.48 },
                    new JsonObject { ["time"] = 1, ["value"] = 0.0 },
                }),
                ("colorCurve", new JsonArray {
                    new JsonObject { ["time"] = 0, ["color"] = "#fff1d8ff" },
                    new JsonObject { ["time"] = 0.28, ["color"] = colour + "dd" },
                    new JsonObject { ["time"] = 1, ["color"] = colour + "00" },
                }),
                ("drawMode", "quad"), ("lit", false),
                ("emissiveIntensity", 7.0), ("softParticleFade", 0.7),
                ("blendMode", "add"), ("priority", 100.0), ("visibilityDistance", 5000.0)))
            .UpsertComponent(OrdnanceType, Props(
                ("kind", "flash"), ("life", 0.0), ("maxLife", 0.55)));
    }

    public static RekallAgeRuntimeEntity AudioReport(
        string id,
        string name,
        RekallAgeRuntimeVector3 at,
        string clip,
        double lifeSeconds,
        double gain,
        double pitch)
    {
        return Base(id, name, at, 1)
            .UpsertComponent(OrdnanceType, Props(
                ("kind", "audio"), ("life", 0.0), ("maxLife", lifeSeconds)))
            .UpsertComponent("Rekall.AudioEmitter", FileEmitter(
                clip, gain, 6000, pitch));
    }

    /// <summary>
    /// A short-lived three-axis energy cage with a light, particle front and low synthetic
    /// report. It is an ordinary runtime entity, so the same lifetime/fade path as a weapon
    /// trail removes it without adding an engine-specific ability concept.
    /// </summary>
    public static RekallAgeRuntimeEntity AbilityPulse(
        string id,
        RekallAgeRuntimeVector3 at,
        string colour,
        bool overcharge)
    {
        const int steps = 40;
        var segments = new JsonArray();
        var radius = overcharge ? 7.0 : 18.0;
        if (overcharge)
        {
            // Broken helical filaments hug the hull. Complete luminous circles looked like a
            // game icon; intermittent plasma crawls like stressed machinery instead.
            for (var index = 0; index < 64; index++)
            {
                if (index % 5 == 0)
                {
                    continue;
                }

                var t = index / 63.0;
                var z = (t - 0.5) * 42.0;
                var a = index * 2.399963;
                var b = a + 0.23 + (0.09 * Math.Sin(index * 1.7));
                var r0 = radius * (0.78 + (0.22 * Math.Sin(index * 0.91)));
                var r1 = radius * (0.78 + (0.22 * Math.Sin((index + 1) * 0.91)));
                segments.Add(Segment(
                    new RekallAgeRuntimeVector3(Math.Cos(a) * r0, Math.Sin(a) * r0, z),
                    new RekallAgeRuntimeVector3(Math.Cos(b) * r1, Math.Sin(b) * r1, z + 1.1)));
            }
        }
        else
        {
            for (var axis = 0; axis < 3; axis++)
            {
                for (var index = 0; index < steps; index++)
                {
                    var a = Math.PI * 2 * index / steps;
                    var b = Math.PI * 2 * (index + 1) / steps;
                    RekallAgeRuntimeVector3 Point(double angle) => axis switch
                    {
                        0 => new RekallAgeRuntimeVector3(Math.Cos(angle) * radius, Math.Sin(angle) * radius, 0),
                        1 => new RekallAgeRuntimeVector3(0, Math.Cos(angle) * radius, Math.Sin(angle) * radius),
                        _ => new RekallAgeRuntimeVector3(Math.Cos(angle) * radius, 0, Math.Sin(angle) * radius),
                    };
                    segments.Add(Segment(Point(a), Point(b)));
                }
            }
        }

        return Base(id, overcharge ? "Overcharge Envelope" : "Shield Pulse", at, 1)
            .UpsertComponent("Rekall.LineSegments", Props(
                ("segments", segments), ("thickness", overcharge ? 0.42 : 1.8),
                ("color", colour + (overcharge ? "a8" : "f0"))))
            .UpsertComponent("Rekall.Material", Props(
                ("baseColor", colour), ("emissiveColor", colour), ("emissiveStrength", 16.0)))
            .UpsertComponent("Rekall.PointLight", Props(
                ("color", colour), ("intensity", overcharge ? 20.0 : 28.0),
                ("range", overcharge ? 34.0 : 48.0), ("castShadows", false)))
            .UpsertComponent("Rekall.ParticleEmitter3D", Props(
                ("role", overcharge ? "overcharge" : "shield-pulse"), ("enabled", true),
                ("simulationSpace", "world"), ("capacity", 180.0), ("spawnRate", 320.0),
                ("lifetime", 0.9), ("seed", overcharge ? 810.0 : 504.0),
                ("velocityDirection", new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0 }),
                ("velocityConeDegrees", 180.0), ("minimumSpeed", 5.0), ("maximumSpeed", 32.0),
                ("gravity", new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 }),
                ("drag", 1.1), ("drawMode", "quad"), ("lit", false),
                ("emissiveIntensity", 9.0), ("softParticleFade", 0.8),
                ("blendMode", "add"), ("priority", 115.0), ("visibilityDistance", 5000.0)))
            .UpsertComponent("Rekall.AudioEmitter", Emitter(overcharge ? 0.52 : 0.62, 6000))
            .UpsertComponent("Rekall.ProceduralAudioClip", Props(
                ("waveform", overcharge ? "saw" : "sine"), ("durationSeconds", 1.1),
                ("startFrequency", overcharge ? 58.0 : 90.0),
                ("endFrequency", overcharge ? 210.0 : 42.0),
                ("sweep", "exponential"), ("attack", 0.015), ("decay", 0.22),
                ("sustain", 0.52), ("release", 0.5), ("noiseMix", overcharge ? 0.32 : 0.14),
                ("harmonics", overcharge ? 4.0 : 2.0), ("amplitude", 0.55), ("seed", 408.0)))
            .UpsertComponent(OrdnanceType, Props(
                ("kind", "trail"), ("life", 0.0), ("maxLife", overcharge ? 1.4 : 1.15)));
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

    public static readonly RekallAgeRuntimeVector3 Zero = new(0, 0, 0);

    public static RekallAgeRuntimeVector3 Delta(RekallAgeRuntimeVector3 from, RekallAgeRuntimeVector3 to) =>
        new(to.X - from.X, to.Y - from.Y, to.Z - from.Z);

    /// <summary>Re-aims a live beam at where its firer and its target are now.</summary>
    public static RekallAgeRuntimeEntity Reaim(
        RekallAgeRuntimeEntity beam,
        RekallAgeRuntimeVector3 from,
        RekallAgeRuntimeVector3 to)
    {
        return beam
            .WithPosition3D(from)
            .UpdateComponent("Rekall.LineSegments", properties =>
            {
                properties["segments"] = new JsonArray { Segment(Zero, Delta(from, to)) };
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

    /// <summary>
    /// Positional one-shot. Spatial so a distant exchange sits behind a near one, and so
    /// zooming in on a firefight brings it forward.
    /// </summary>
    private static JsonObject Emitter(double gain, double maxDistance) => Props(
        ("gain", gain), ("playOnStart", true), ("loop", false),
        // Falloff has to be sized to the scene, not to a room. Framed on the whole
        // engagement the camera sits over a thousand units back, so a reference distance
        // and a cutoff picked for human scale silenced every shot: attenuation is
        // referenceDistance/distance, and past maxDistance it is flatly zero.
        ("spatial", true), ("referenceDistance", 450.0), ("maxDistance", maxDistance));

    private static JsonObject FileEmitter(
        string clip,
        double gain,
        double maxDistance,
        double pitch) => Props(
        ("clip", clip), ("gain", gain), ("pitch", pitch),
        ("playOnStart", true), ("loop", false),
        ("spatial", true), ("referenceDistance", 450.0), ("maxDistance", maxDistance));

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
