namespace Rekall.Age.Rendering;

public sealed record RekallAgeRuntimeViewportAssetSet(
    IReadOnlyDictionary<string, RekallAgeRgbaImage> Images,
    IReadOnlyList<RekallAgeRuntimeViewportAssetIssue> Issues)
{
    public static RekallAgeRuntimeViewportAssetSet Empty { get; } = new(
        new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal),
        Array.Empty<RekallAgeRuntimeViewportAssetIssue>());
}

public sealed record RekallAgeRuntimeViewportAssetIssue(
    string AssetId,
    string Code,
    string Message);
