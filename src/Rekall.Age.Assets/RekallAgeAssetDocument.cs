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

    public RekallAgeTextureMetadata? TextureMetadata { get; init; }

    public RekallAgeAssetProvenance? Provenance { get; init; }
}

public sealed record RekallAgeAssetProvenance(
    string OriginalUrl,
    string FinalUrl,
    DateTimeOffset RetrievedAtUtc,
    string? MediaType,
    long ByteCount,
    string Sha256,
    string? Attribution,
    string? License,
    string? LicenseUrl);

public sealed record RekallAgeTextureMetadata(
    string Container,
    int Width,
    int Height,
    int MipLevelCount,
    string? Format,
    string? Supercompression,
    bool GpuCompressed);

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
    IReadOnlyList<RekallAgeGlbAnimationMetadata> Animations)
{
    public int SkinCount { get; init; }

    public IReadOnlyList<RekallAgeGlbSkinMetadata> Skins { get; init; } =
        Array.Empty<RekallAgeGlbSkinMetadata>();

    public IReadOnlyList<string> SupportedMorphTargetSemantics { get; init; } =
        ["POSITION", "NORMAL"];

    public IReadOnlyList<string> MorphTargetLimitations { get; init; } =
    [
        "Maximum 64 compatible ordered targets per rendered asset.",
        "Float VEC3 POSITION and optional NORMAL deltas only; TANGENT, sparse, and quantized accessors are unsupported.",
        "Native glTF weights animation channels are not executed; use Rekall.MorphWeights with generic Rekall AGE animation contracts."
    ];
}

public sealed record RekallAgeGlbSceneMetadata(string? Name, int NodeCount);

public sealed record RekallAgeGlbNodeMetadata(string? Name, int? MeshIndex)
{
    public int? SkinIndex { get; init; }

    public int ChildCount { get; init; }

    public IReadOnlyList<double> MorphWeights { get; init; } = Array.Empty<double>();
}

public sealed record RekallAgeGlbMeshMetadata(string? Name, int PrimitiveCount)
{
    public int MorphTargetCount { get; init; }

    public IReadOnlyList<string> MorphTargetNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<double> DefaultMorphWeights { get; init; } = Array.Empty<double>();
}

public sealed record RekallAgeGlbMaterialMetadata(string? Name);

public sealed record RekallAgeGlbImageMetadata(string? Name, string? MimeType, string? Uri);

public sealed record RekallAgeGlbSkinMetadata(
    string? Name,
    int JointCount,
    int? SkeletonNodeIndex,
    int? InverseBindMatricesAccessorIndex);

public sealed record RekallAgeGlbAnimationMetadata(string? Name)
{
    public int SamplerCount { get; init; }

    public int ChannelCount { get; init; }

    public IReadOnlyList<RekallAgeGlbAnimationTargetMetadata> Targets { get; init; } =
        Array.Empty<RekallAgeGlbAnimationTargetMetadata>();
}

public sealed record RekallAgeGlbAnimationTargetMetadata(int? NodeIndex, string? Path);
