namespace Rekall.Age.Assets;

public sealed record RekallAgeAssetDocument(
    string Id,
    string Name,
    string DisplayName,
    string Kind,
    string SourcePath,
    string ImportedPath,
    string ContentHash);
