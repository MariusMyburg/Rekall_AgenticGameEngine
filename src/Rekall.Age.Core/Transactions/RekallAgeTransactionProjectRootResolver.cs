namespace Rekall.Age.Core.Transactions;

public static class RekallAgeTransactionProjectRootResolver
{
    private const string ManifestFileName = "rekall.project.json";

    public static string? Resolve(IReadOnlyList<string> changedResources)
    {
        foreach (var resource in changedResources)
        {
            var fullPath = Path.GetFullPath(resource);
            var start = Directory.Exists(fullPath)
                ? fullPath
                : Path.GetDirectoryName(fullPath);
            var directory = start is null ? null : new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, ManifestFileName)))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
