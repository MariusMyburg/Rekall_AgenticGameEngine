using System.Globalization;
using System.Numerics;

namespace Rekall.Age.Rendering;

/// <summary>
/// Bounded deterministic CPU simulation used by interactive backends that do
/// not yet execute the native Vulkan particle compute pipeline. Rendering stays
/// on the GPU; this bridge only produces camera-facing instance facts.
/// </summary>
public sealed class RekallAgeInteractiveParticleBridge
{
    public const int MaximumParticles = 2_048;

    public RekallAgeInteractiveParticleFrame Build(
        RekallAgeVulkanParticlePlan? plan,
        double elapsedSeconds,
        double deltaSeconds)
    {
        if (plan is null || plan.Emitters.Count == 0 || !double.IsFinite(elapsedSeconds)
            || !double.IsFinite(deltaSeconds) || deltaSeconds < 0)
        {
            return RekallAgeInteractiveParticleFrame.Empty;
        }

        var particles = new List<RekallAgeInteractiveParticle>(Math.Min(plan.AllocatedCapacity, MaximumParticles));
        foreach (var range in plan.Emitters)
        {
            var emitter = range.Source;
            var lifetime = emitter.LifetimeSeconds;
            var active = Math.Min(
                range.AllocationCapacity,
                Math.Max(0, (int)Math.Ceiling(emitter.SpawnRate * lifetime)));
            active = Math.Min(active, MaximumParticles - particles.Count);
            if (active <= 0 || emitter.SpawnRate <= 0)
            {
                continue;
            }

            var interval = 1.0 / emitter.SpawnRate;
            for (var index = 0; index < active; index++)
            {
                var age = PositiveModulo(elapsedSeconds - index * interval, lifetime);
                var normalizedAge = Math.Clamp(age / lifetime, 0, 1);
                var random0 = Random01(emitter.DeterministicSeed, index, 0);
                var random1 = Random01(emitter.DeterministicSeed, index, 1);
                var random2 = Random01(emitter.DeterministicSeed, index, 2);
                var speed = Lerp(emitter.MinimumSpeed, emitter.MaximumSpeed, random0);
                var direction = Vector3.Normalize(new Vector3(
                    (float)emitter.VelocityDirectionX,
                    (float)emitter.VelocityDirectionY,
                    (float)emitter.VelocityDirectionZ));
                if (!float.IsFinite(direction.X)) direction = Vector3.UnitY;
                var cone = (float)Math.Sin(emitter.VelocityConeDegrees * Math.PI / 180.0);
                var jitter = Vector3.Normalize(new Vector3(
                    (float)(random1 * 2 - 1),
                    (float)(random2 * 2 - 1),
                    (float)(Random01(emitter.DeterministicSeed, index, 3) * 2 - 1)));
                if (!float.IsFinite(jitter.X)) jitter = Vector3.UnitX;
                direction = Vector3.Normalize(direction + jitter * cone);
                var damping = Math.Exp(-Math.Max(0, emitter.Drag) * age);
                var velocity = direction * (float)(speed * damping);
                var gravity = new Vector3((float)emitter.GravityX, (float)emitter.GravityY, (float)emitter.GravityZ);
                var origin = new Vector3((float)emitter.Transform.X, (float)emitter.Transform.Y, (float)emitter.Transform.Z);
                var position = origin + velocity * (float)age + gravity * (float)(0.5 * age * age);
                var size = (float)Math.Max(0, Evaluate(emitter.SizeCurve, normalizedAge));
                var color = Evaluate(emitter.ColorCurve, normalizedAge);
                color *= (float)Math.Max(1, emitter.EmissiveIntensity);
                color.W = Math.Clamp(color.W, 0, 1);
                if (size > 0.001f && color.W > 0.001f)
                {
                    particles.Add(new(position, size, color, emitter.BlendMode, emitter.TextureAssetId, emitter.EntityId));
                }
            }
        }

        return new RekallAgeInteractiveParticleFrame(
            particles,
            "cpu-deterministic-sim/gpu-quad-draw",
            plan.Emitters.Count,
            particles.Count);
    }

    private static double PositiveModulo(double value, double divisor) => ((value % divisor) + divisor) % divisor;
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Evaluate(IReadOnlyList<Abstractions.RekallAgeRuntimeViewportParticleScalarKey> keys, double age)
    {
        if (keys.Count == 0) return 1;
        for (var index = 1; index < keys.Count; index++)
        {
            if (age <= keys[index].NormalizedAge)
            {
                var previous = keys[index - 1];
                var span = Math.Max(0.000001, keys[index].NormalizedAge - previous.NormalizedAge);
                return Lerp(previous.Value, keys[index].Value, Math.Clamp((age - previous.NormalizedAge) / span, 0, 1));
            }
        }
        return keys[^1].Value;
    }

    private static Vector4 Evaluate(IReadOnlyList<Abstractions.RekallAgeRuntimeViewportParticleColorKey> keys, double age)
    {
        if (keys.Count == 0) return Vector4.One;
        for (var index = 1; index < keys.Count; index++)
        {
            if (age <= keys[index].NormalizedAge)
            {
                var previous = keys[index - 1];
                var span = Math.Max(0.000001, keys[index].NormalizedAge - previous.NormalizedAge);
                return Vector4.Lerp(ParseColor(previous.Color), ParseColor(keys[index].Color),
                    (float)Math.Clamp((age - previous.NormalizedAge) / span, 0, 1));
            }
        }
        return ParseColor(keys[^1].Color);
    }

    private static Vector4 ParseColor(string value)
    {
        var hex = value.TrimStart('#');
        if (hex.Length is not (6 or 8)) return Vector4.One;
        return new Vector4(
            byte.Parse(hex[..2], NumberStyles.HexNumber) / 255f,
            byte.Parse(hex[2..4], NumberStyles.HexNumber) / 255f,
            byte.Parse(hex[4..6], NumberStyles.HexNumber) / 255f,
            hex.Length == 8 ? byte.Parse(hex[6..8], NumberStyles.HexNumber) / 255f : 1f);
    }

    private static double Random01(uint seed, int index, int lane)
    {
        var value = seed ^ (uint)(index * 747796405) ^ (uint)(lane * 2891336453);
        value ^= value >> 16;
        value *= 2246822519u;
        value ^= value >> 13;
        return value / (double)uint.MaxValue;
    }
}

public sealed record RekallAgeInteractiveParticleFrame(
    IReadOnlyList<RekallAgeInteractiveParticle> Particles,
    string ExecutionMode,
    int EmitterCount,
    int ActiveParticleCount)
{
    public static RekallAgeInteractiveParticleFrame Empty { get; } = new([], "disabled", 0, 0);
}

public sealed record RekallAgeInteractiveParticle(
    Vector3 Position,
    float Size,
    Vector4 Color,
    string BlendMode,
    string? TextureAssetId,
    string EmitterEntityId);
