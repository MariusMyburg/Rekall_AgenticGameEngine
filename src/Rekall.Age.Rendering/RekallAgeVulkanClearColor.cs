namespace Rekall.Age.Rendering;

public readonly record struct RekallAgeVulkanClearColor(float R, float G, float B, float A)
{
    public static RekallAgeVulkanClearColor Default => new(0.08f, 0.10f, 0.14f, 1.0f);

    public static RekallAgeVulkanClearColor Normalize(RekallAgeVulkanClearColor? color)
    {
        var value = color ?? Default;
        return new RekallAgeVulkanClearColor(
            Clamp01(value.R),
            Clamp01(value.G),
            Clamp01(value.B),
            Clamp01(value.A));
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value))
        {
            return 0.0f;
        }

        return Math.Clamp(value, 0.0f, 1.0f);
    }
}
