namespace Rekall.Age.Modules.Commands;

internal static class RekallAgeModuleSourcePaths
{
    public static string GetModulesRoot(string projectRoot)
    {
        return Path.GetFullPath(Path.Combine(projectRoot, "Modules"));
    }

    public static string GetSourcePath(string projectRoot, string moduleName, string fileName)
    {
        return Path.GetFullPath(Path.Combine(GetModulesRoot(projectRoot), moduleName, fileName));
    }

    public static bool IsSafeDirectModuleSourcePath(string projectRoot, string moduleName, string fileName, string sourcePath)
    {
        return IsSimplePathSegment(moduleName) &&
            IsSimplePathSegment(fileName) &&
            IsInsideDirectory(sourcePath, GetModulesRoot(projectRoot));
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, comparison);
    }

    private static bool IsSimplePathSegment(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !Path.IsPathRooted(value) &&
            value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0 &&
            !value.Equals(".", StringComparison.Ordinal) &&
            !value.Equals("..", StringComparison.Ordinal);
    }
}
