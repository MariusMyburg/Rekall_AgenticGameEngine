using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Assets;

public sealed class RekallAgeModelAssetAppendOnlyResourceClassifier
    : IRekallAgeAppendOnlyResourceClassifier
{
    public bool IsAppendOnly(string projectRoot, string confinedResourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(confinedResourcePath);
        var root = Path.GetFullPath(projectRoot);
        var candidate = RekallAgeConfinedPath.Resolve(
            root,
            confinedResourcePath,
            "Model Asset restoration classification path");
        var compiledRoot = RekallAgeConfinedPath.Resolve(
            root,
            Path.Combine(root, "Assets", "Models", "Compiled"),
            "Compiled Model Asset root");
        var relative = Path.GetRelativePath(compiledRoot, candidate);
        return relative == "."
               || (!Path.IsPathRooted(relative)
                   && relative != ".."
                   && !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison));
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
