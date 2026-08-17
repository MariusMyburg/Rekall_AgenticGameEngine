namespace Rekall.Age.Core.Product;

public sealed record RekallAgeDistributionPaths(
    string Root,
    string Manifest,
    string Cli,
    string Studio,
    string HeadlessPlayerPayload,
    string WindowsPlayerPayload,
    string ModuleSdk,
    string Documentation);

public static class RekallAgeDistributionLayout
{
    public const string ManifestFileName = "rekall.distribution.json";

    public static bool TryFind(string startPath, out RekallAgeDistributionPaths paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startPath);
        var fullStart = Path.GetFullPath(startPath);
        var directory = new DirectoryInfo(File.Exists(fullStart)
            ? Path.GetDirectoryName(fullStart)!
            : fullStart);
        while (directory is not null)
        {
            var manifest = Path.Combine(directory.FullName, ManifestFileName);
            if (File.Exists(manifest))
            {
                paths = Create(directory.FullName);
                return true;
            }

            directory = directory.Parent;
        }

        paths = null!;
        return false;
    }

    public static RekallAgeDistributionPaths Create(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        return new RekallAgeDistributionPaths(
            fullRoot,
            Path.Combine(fullRoot, ManifestFileName),
            Path.Combine(fullRoot, "tools", "cli"),
            Path.Combine(fullRoot, "tools", "studio"),
            Path.Combine(fullRoot, "players", "headless"),
            Path.Combine(fullRoot, "players", "windows"),
            Path.Combine(
                fullRoot,
                "sdk",
                RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            Path.Combine(fullRoot, "docs"));
    }
}
