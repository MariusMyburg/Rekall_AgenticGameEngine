using System.Numerics;

namespace Rekall.Age.Rendering.Abstractions;

public static class RekallAgeTextureLayout
{
    public static ulong BytesPerPixel(RekallAgeTextureFormat format) => format switch
    {
        RekallAgeTextureFormat.R8Unorm => 1,
        RekallAgeTextureFormat.Rg8Unorm => 2,
        RekallAgeTextureFormat.Rgba16Float => 8,
        RekallAgeTextureFormat.Depth32Float => 5,
        _ => 4
    };

    public static ulong SubresourceBytes(RekallAgeTextureDescriptor descriptor, int mipLevel)
    {
        var maximumMipLevels = MaximumMipLevels(descriptor);
        if (mipLevel < 0 || mipLevel >= descriptor.MipLevels || mipLevel >= maximumMipLevels) return 0;
        try
        {
            return checked(
                (ulong)Math.Max(1, descriptor.Width >> mipLevel)
                * (ulong)Math.Max(1, descriptor.Height >> mipLevel)
                * (ulong)Math.Max(1, descriptor.Depth >> mipLevel)
                * BytesPerPixel(descriptor.Format));
        }
        catch (OverflowException) { return ulong.MaxValue; }
    }

    public static ulong TotalBytes(RekallAgeTextureDescriptor descriptor)
    {
        if (descriptor.Width < 1 || descriptor.Height < 1 || descriptor.Depth < 1
            || descriptor.MipLevels < 1 || descriptor.ArrayLayers < 1 || descriptor.SampleCount < 1) return 0;
        if (descriptor.MipLevels > MaximumMipLevels(descriptor)) return ulong.MaxValue;
        var total = 0UL;
        for (var mip = 0; mip < descriptor.MipLevels; mip++)
            total = SaturatingAdd(total, SubresourceBytes(descriptor, mip));
        return SaturatingMultiply(SaturatingMultiply(total, (ulong)descriptor.ArrayLayers), (ulong)descriptor.SampleCount);
    }

    private static ulong SaturatingAdd(ulong left, ulong right) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    private static ulong SaturatingMultiply(ulong left, ulong right) => left != 0 && right > ulong.MaxValue / left ? ulong.MaxValue : left * right;
    private static int MaximumMipLevels(RekallAgeTextureDescriptor descriptor)
    {
        var dimension = Math.Max(descriptor.Width, Math.Max(descriptor.Height, descriptor.Depth));
        return dimension > 0 ? BitOperations.Log2((uint)dimension) + 1 : 0;
    }
}
