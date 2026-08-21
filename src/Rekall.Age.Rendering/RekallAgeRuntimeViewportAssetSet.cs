namespace Rekall.Age.Rendering;

public sealed record RekallAgeRuntimeViewportAssetSet(
    IReadOnlyDictionary<string, RekallAgeRgbaImage> Images,
    IReadOnlyDictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>> Models,
    IReadOnlyList<RekallAgeRuntimeViewportAssetIssue> Issues)
{
    public IReadOnlyDictionary<string, RekallAgeRuntimeTextureAsset> Textures { get; init; } =
        new Dictionary<string, RekallAgeRuntimeTextureAsset>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, RekallAgeRuntimeFontAsset> Fonts { get; init; } =
        new Dictionary<string, RekallAgeRuntimeFontAsset>(StringComparer.Ordinal);

    public static RekallAgeRuntimeViewportAssetSet Empty { get; } = new(
        new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal),
        Array.Empty<RekallAgeRuntimeViewportAssetIssue>());
}

public sealed record RekallAgeRuntimeFontAsset(string AssetId, string Path);

public sealed record RekallAgeRuntimeTextureAsset(
    string AssetId,
    string Container,
    int Width,
    int Height,
    int MipLevelCount,
    string? Format,
    string? Supercompression,
    bool GpuCompressed,
    IReadOnlyList<RekallAgeRuntimeTextureMipLevel> MipLevels);

public sealed record RekallAgeRuntimeTextureMipLevel(
    int Level,
    int Width,
    int Height,
    byte[] Bytes);

public sealed record RekallAgeRuntimeViewportAssetIssue(
    string AssetId,
    string Code,
    string Message);
