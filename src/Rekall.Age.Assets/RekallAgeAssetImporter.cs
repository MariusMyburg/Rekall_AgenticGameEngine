using System.Security.Cryptography;

namespace Rekall.Age.Assets;

public static class RekallAgeAssetImporter
{
    public static async ValueTask<RekallAgeAssetDocument> ImportAsync(
        string projectRoot,
        string sourcePath,
        string kind,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Asset source file was not found.", sourcePath);
        }

        var normalizedKind = ToSlug(kind, "asset");
        var normalizedName = ToSlug(
            string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(sourcePath) : displayName,
            "asset");
        var hash = await HashFileAsync(sourcePath, cancellationToken);
        var id = $"asset_{normalizedName}_{hash[..8]}";
        var extension = Path.GetExtension(sourcePath);
        var importedDirectory = Path.Combine(projectRoot, "Assets", normalizedKind);
        Directory.CreateDirectory(importedDirectory);
        var importedPath = Path.Combine(importedDirectory, $"{id}{extension}");

        await using (var source = File.OpenRead(sourcePath))
        await using (var destination = File.Create(importedPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        return new RekallAgeAssetDocument(
            id,
            normalizedName,
            string.IsNullOrWhiteSpace(displayName) ? normalizedName : displayName.Trim(),
            normalizedKind,
            Path.GetFullPath(sourcePath),
            Path.GetFullPath(importedPath),
            hash);
    }

    private static async ValueTask<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ToSlug(string? value, string fallback)
    {
        var chars = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(item => char.IsLetterOrDigit(item) ? item : '-')
            .ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? fallback : slug;
    }
}
