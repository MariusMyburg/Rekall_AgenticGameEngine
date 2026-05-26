using System.Globalization;
using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public static class RekallAgeProceduralMaterialTextureGenerator
{
    public static RekallAgeProceduralMaterialTextures Generate(
        string entityId,
        RekallAgeRuntimeViewportProceduralMaterial material)
    {
        var resolution = Math.Clamp(material.Resolution, 2, 2048);
        var samples = new float[resolution * resolution];
        var baseColor = new byte[resolution * resolution * 4];
        var metallicRoughness = new byte[resolution * resolution * 4];
        var emissive = material.EmissiveStrength > 0
            ? new byte[resolution * resolution * 4]
            : null;
        var colorA = ParseColor(material.BaseColorA);
        var colorB = ParseColor(material.BaseColorB);
        var metallic = ToByte(material.MetallicFactor);
        var roughnessA = ToByte(material.RoughnessA);
        var roughnessB = ToByte(material.RoughnessB);

        for (var y = 0; y < resolution; y++)
        {
            for (var x = 0; x < resolution; x++)
            {
                var offset = (y * resolution + x) * 4;
                var sample = Sample(material, x, y, resolution);
                samples[y * resolution + x] = sample;
                var color = Lerp(colorA, colorB, sample);
                baseColor[offset + 0] = ToByte(color.X);
                baseColor[offset + 1] = ToByte(color.Y);
                baseColor[offset + 2] = ToByte(color.Z);
                baseColor[offset + 3] = ToByte(color.W);
                metallicRoughness[offset + 0] = 0;
                metallicRoughness[offset + 1] = sample < 0.5f ? roughnessA : roughnessB;
                metallicRoughness[offset + 2] = metallic;
                metallicRoughness[offset + 3] = 255;
                if (emissive is not null)
                {
                    emissive[offset + 0] = baseColor[offset + 0];
                    emissive[offset + 1] = baseColor[offset + 1];
                    emissive[offset + 2] = baseColor[offset + 2];
                    emissive[offset + 3] = baseColor[offset + 3];
                }
            }
        }

        var sampler = new RekallAgeVulkanSceneSampler(
            RekallAgeVulkanSceneFilter.Linear,
            RekallAgeVulkanSceneFilter.Linear,
            RekallAgeVulkanSceneWrapMode.Repeat,
            RekallAgeVulkanSceneWrapMode.Repeat);
        var normalizedId = string.IsNullOrWhiteSpace(entityId) ? "entity" : entityId.Trim();
        return new RekallAgeProceduralMaterialTextures(
            new RekallAgeVulkanSceneTexture($"{normalizedId}/procedural/baseColor", resolution, resolution, baseColor, sampler),
            new RekallAgeVulkanSceneTexture($"{normalizedId}/procedural/metallicRoughness", resolution, resolution, metallicRoughness, sampler),
            material.NormalStrength > 0
                ? new RekallAgeVulkanSceneTexture(
                    $"{normalizedId}/procedural/normal",
                    resolution,
                    resolution,
                    GenerateNormalMap(samples, resolution, (float)Math.Clamp(material.NormalStrength, 0, 4)),
                    sampler)
                : null,
            emissive is null
                ? null
                : new RekallAgeVulkanSceneTexture($"{normalizedId}/procedural/emissive", resolution, resolution, emissive, sampler),
            material.EmissiveStrength > 0
                ? new Vector4(1, 1, 1, (float)Math.Clamp(material.EmissiveStrength, 0, 64))
                : Vector4.Zero);
    }

    private static byte[] GenerateNormalMap(float[] samples, int resolution, float strength)
    {
        var rgba = new byte[resolution * resolution * 4];
        for (var y = 0; y < resolution; y++)
        {
            for (var x = 0; x < resolution; x++)
            {
                var left = samples[y * resolution + Wrap(x - 1, resolution)];
                var right = samples[y * resolution + Wrap(x + 1, resolution)];
                var up = samples[Wrap(y - 1, resolution) * resolution + x];
                var down = samples[Wrap(y + 1, resolution) * resolution + x];
                var normal = Vector3.Normalize(new Vector3((left - right) * strength, (up - down) * strength, 1));
                var offset = (y * resolution + x) * 4;
                rgba[offset + 0] = ToByte(normal.X * 0.5f + 0.5f);
                rgba[offset + 1] = ToByte(normal.Y * 0.5f + 0.5f);
                rgba[offset + 2] = ToByte(normal.Z * 0.5f + 0.5f);
                rgba[offset + 3] = 255;
            }
        }

        return rgba;
    }

    private static int Wrap(int value, int count)
    {
        return (value % count + count) % count;
    }

    private static float Sample(RekallAgeRuntimeViewportProceduralMaterial material, int x, int y, int resolution)
    {
        var u = x / (float)resolution * (float)Math.Max(0.0001, material.Scale);
        var v = y / (float)resolution * (float)Math.Max(0.0001, material.Scale);
        return NormalizeGenerator(material.Generator) switch
        {
            "stripes" => MathF.Floor(u + material.Seed * 0.017f) % 2 == 0 ? 0 : 1,
            "rings" => RingSample(u, v, material.Seed),
            "noise" => NoiseSample(x, y, material.Seed),
            _ => (MathF.Floor(u) + MathF.Floor(v) + material.Seed) % 2 == 0 ? 0 : 1
        };
    }

    private static string NormalizeGenerator(string? generator)
    {
        return string.IsNullOrWhiteSpace(generator)
            ? "checker"
            : generator.Trim().ToLowerInvariant();
    }

    private static float RingSample(float u, float v, int seed)
    {
        var dx = u - MathF.Floor(u) - 0.5f;
        var dy = v - MathF.Floor(v) - 0.5f;
        return MathF.Floor(MathF.Sqrt(dx * dx + dy * dy) * 8 + seed * 0.031f) % 2 == 0 ? 0 : 1;
    }

    private static float NoiseSample(int x, int y, int seed)
    {
        unchecked
        {
            var value = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            value = (value ^ (value >> 13)) * 1274126177;
            return ((value ^ (value >> 16)) & 0xffff) / 65535f;
        }
    }

    private static Vector4 ParseColor(string? color)
    {
        if (color is { Length: 7 or 9 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            var alpha = color.Length == 9
                && byte.TryParse(color.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
                ? a / 255f
                : 1;
            return new Vector4(r / 255f, g / 255f, b / 255f, alpha);
        }

        return Vector4.One;
    }

    private static Vector4 Lerp(Vector4 a, Vector4 b, float amount)
    {
        return a + (b - a) * Math.Clamp(amount, 0, 1);
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(Math.Clamp(value, 0, 1) * 255), 0, 255);
    }
}

public sealed record RekallAgeProceduralMaterialTextures(
    RekallAgeVulkanSceneTexture BaseColorTexture,
    RekallAgeVulkanSceneTexture MetallicRoughnessTexture,
    RekallAgeVulkanSceneTexture? NormalTexture,
    RekallAgeVulkanSceneTexture? EmissiveTexture,
    Vector4 EmissiveFactor);
