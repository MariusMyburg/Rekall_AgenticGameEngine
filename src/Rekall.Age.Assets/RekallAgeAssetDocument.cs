namespace Rekall.Age.Assets;

public sealed record RekallAgeAssetDocument(
    string Id,
    string Name,
    string DisplayName,
    string Kind,
    string SourcePath,
    string ImportedPath,
    string ContentHash)
{
    public RekallAgeGlbMetadata? GlbMetadata { get; init; }
}

public sealed record RekallAgeGlbMetadata(
    int SceneCount,
    int NodeCount,
    int MeshCount,
    int MaterialCount,
    int ImageCount,
    int AnimationCount,
    IReadOnlyList<RekallAgeGlbSceneMetadata> Scenes,
    IReadOnlyList<RekallAgeGlbNodeMetadata> Nodes,
    IReadOnlyList<RekallAgeGlbMeshMetadata> Meshes,
    IReadOnlyList<RekallAgeGlbMaterialMetadata> Materials,
    IReadOnlyList<RekallAgeGlbImageMetadata> Images,
    IReadOnlyList<RekallAgeGlbAnimationMetadata> Animations);

public sealed record RekallAgeGlbSceneMetadata(string? Name, int NodeCount);

public sealed record RekallAgeGlbNodeMetadata(string? Name, int? MeshIndex);

public sealed record RekallAgeGlbMeshMetadata(string? Name, int PrimitiveCount);

public sealed record RekallAgeGlbMaterialMetadata(string? Name);

public sealed record RekallAgeGlbImageMetadata(string? Name, string? MimeType, string? Uri);

public sealed record RekallAgeGlbAnimationMetadata(string? Name);
