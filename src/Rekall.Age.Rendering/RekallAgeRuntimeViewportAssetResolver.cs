using Rekall.Age.Assets;
using Rekall.Age.Rendering.Abstractions;
using StbImageSharp;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeRuntimeViewportAssetResolver
{
    private readonly RekallAgeAssetCatalogStore _assetStore;

    public RekallAgeRuntimeViewportAssetResolver()
        : this(new RekallAgeAssetCatalogStore())
    {
    }

    public RekallAgeRuntimeViewportAssetResolver(RekallAgeAssetCatalogStore assetStore)
    {
        _assetStore = assetStore;
    }

    public async ValueTask<RekallAgeRuntimeViewportAssetSet> ResolveAsync(
        string projectRoot,
        RekallAgeRuntimeViewportFrame frame,
        CancellationToken cancellationToken)
    {
        var spriteAssetIds = frame.Renderables
            .Where(renderable => renderable.Kind.Equals("sprite", StringComparison.Ordinal))
            .Select(renderable => renderable.AssetId)
            .Where(assetId => !string.IsNullOrWhiteSpace(assetId))
            .Select(assetId => assetId!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(assetId => assetId, StringComparer.Ordinal)
            .ToArray();
        var textureAssetIds = frame.Renderables
            .Where(renderable => renderable.Kind.Equals("mesh", StringComparison.Ordinal))
            .Select(renderable => renderable.TextureAssetId)
            .Where(assetId => !string.IsNullOrWhiteSpace(assetId))
            .Select(assetId => assetId!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(assetId => assetId, StringComparer.Ordinal)
            .ToArray();
        var modelAssetIds = frame.Renderables
            .Where(renderable => renderable.Kind.Equals("mesh", StringComparison.Ordinal))
            .Select(renderable => renderable.AssetId)
            .Where(assetId => !string.IsNullOrWhiteSpace(assetId))
            .Select(assetId => assetId!)
            .Where(IsImportedModelAssetId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(assetId => assetId, StringComparer.Ordinal)
            .ToArray();
        if (spriteAssetIds.Length == 0 && textureAssetIds.Length == 0 && modelAssetIds.Length == 0)
        {
            return RekallAgeRuntimeViewportAssetSet.Empty;
        }

        var catalog = await _assetStore.LoadAsync(projectRoot, cancellationToken);
        var catalogById = catalog.Assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        var images = new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal);
        var textures = new Dictionary<string, RekallAgeRuntimeTextureAsset>(StringComparer.Ordinal);
        var models = new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal);
        var issues = new List<RekallAgeRuntimeViewportAssetIssue>();

        foreach (var assetId in spriteAssetIds.Concat(textureAssetIds).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalogById.TryGetValue(assetId, out var asset))
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_MISSING",
                    "Image asset was not found in the project catalog."));
                continue;
            }

            if (!asset.Kind.Equals("sprite", StringComparison.Ordinal)
                && !asset.Kind.Equals("texture", StringComparison.Ordinal)
                && !asset.Kind.Equals("image", StringComparison.Ordinal))
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_UNSUPPORTED",
                    $"Asset kind '{asset.Kind}' cannot be drawn as an image texture."));
                continue;
            }

            if (!File.Exists(asset.ImportedPath))
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_MISSING",
                    "Image asset imported file was not found."));
                continue;
            }

            if (asset.TextureMetadata is { } textureMetadata
                && (textureMetadata.Container.Equals("ktx2", StringComparison.Ordinal)
                    || textureMetadata.Container.Equals("dds", StringComparison.Ordinal)
                    || textureMetadata.GpuCompressed))
            {
                var texture = await RekallAgeRuntimeTexturePayloadReader.ReadAsync(
                    assetId,
                    asset.ImportedPath,
                    textureMetadata,
                    cancellationToken);
                if (texture is not null)
                {
                    textures[assetId] = texture;
                    continue;
                }

                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_TEXTURE_COMPRESSED_UNTRANSCODED",
                    $"Texture asset is {textureMetadata.Container.ToUpperInvariant()} {textureMetadata.Width}x{textureMetadata.Height} {textureMetadata.Format ?? "unknown format"}"
                    + (textureMetadata.Supercompression is null ? string.Empty : $" with {textureMetadata.Supercompression} supercompression")
                    + "; runtime transcoding or direct compressed GPU upload is not available yet."));
                continue;
            }

            try
            {
                images[assetId] = await ReadImageTextureAsync(asset.ImportedPath, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or ArgumentException)
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_UNSUPPORTED",
                    ex.Message));
            }
        }

        foreach (var assetId in modelAssetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalogById.TryGetValue(assetId, out var asset))
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_MISSING",
                    "Model asset was not found in the project catalog."));
                continue;
            }

            if (!asset.Kind.Equals("model", StringComparison.Ordinal))
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_UNSUPPORTED",
                    $"Asset kind '{asset.Kind}' cannot be drawn as a model."));
                continue;
            }

            if (!File.Exists(asset.ImportedPath))
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_MISSING",
                    "Model asset imported file was not found."));
                continue;
            }

            try
            {
                var meshes = await new RekallAgeGlbMeshLoader()
                    .LoadAsync(assetId, asset.ImportedPath, cancellationToken);
                if (meshes.Count == 0)
                {
                    issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                        assetId,
                        "REKALL_RENDER_ASSET_UNSUPPORTED",
                        "Model asset did not contain drawable triangle meshes."));
                    continue;
                }

                models[assetId] = meshes;
            }
            catch (Exception ex) when (ex is IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or ArgumentException
                or System.Text.Json.JsonException)
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_UNSUPPORTED",
                    ex.Message));
            }
        }

        return new RekallAgeRuntimeViewportAssetSet(images, models, issues.ToArray())
        {
            Textures = textures
        };
    }

    private static async ValueTask<RekallAgeRgbaImage> ReadImageTextureAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var decoded = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            return new RekallAgeRgbaImage(decoded.Width, decoded.Height, decoded.Data);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or NotSupportedException
            or ArgumentException)
        {
            throw new InvalidDataException("Texture asset is not a decodable PNG/JPEG/TGA/BMP image.", ex);
        }
    }

    private static bool IsImportedModelAssetId(string assetId)
    {
        var normalized = assetId.Trim().ToLowerInvariant();
        return !normalized.StartsWith("rekall.geometry.", StringComparison.Ordinal)
            && !normalized.StartsWith("rekall.planet.", StringComparison.Ordinal)
            && !normalized.StartsWith("rekall.primitive.", StringComparison.Ordinal);
    }
}
