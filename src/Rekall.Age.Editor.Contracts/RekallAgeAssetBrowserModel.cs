namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeAssetBrowserModel(
    IReadOnlyList<RekallAgeAssetBrowserItem> Assets);

public sealed record RekallAgeAssetBrowserItem(
    string AssetId,
    string DisplayName,
    string Kind,
    string ImportedPath,
    string ContentHash);
