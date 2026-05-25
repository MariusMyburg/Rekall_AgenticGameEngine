using Rekall.Age.Assets;
using Rekall.Age.Rendering.Abstractions;

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
        if (spriteAssetIds.Length == 0)
        {
            return RekallAgeRuntimeViewportAssetSet.Empty;
        }

        var catalog = await _assetStore.LoadAsync(projectRoot, cancellationToken);
        var catalogById = catalog.Assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        var images = new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal);
        var issues = new List<RekallAgeRuntimeViewportAssetIssue>();

        foreach (var assetId in spriteAssetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalogById.TryGetValue(assetId, out var asset))
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_MISSING",
                    "Sprite asset was not found in the project catalog."));
                continue;
            }

            if (!asset.Kind.Equals("sprite", StringComparison.Ordinal))
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_UNSUPPORTED",
                    $"Asset kind '{asset.Kind}' cannot be drawn as a sprite."));
                continue;
            }

            if (!File.Exists(asset.ImportedPath))
            {
                issues.Add(new RekallAgeRuntimeViewportAssetIssue(
                    assetId,
                    "REKALL_RENDER_ASSET_MISSING",
                    "Sprite asset imported file was not found."));
                continue;
            }

            try
            {
                images[assetId] = await RekallAgePngReader.ReadRgbaAsync(asset.ImportedPath, cancellationToken);
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

        return new RekallAgeRuntimeViewportAssetSet(images, issues.ToArray());
    }
}
