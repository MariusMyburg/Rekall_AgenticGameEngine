using System.Reflection;
using System.Runtime.Loader;

namespace Rekall.Age.Modules;

public static class RekallAgeProjectModuleAssemblyLoader
{
    public static IReadOnlyList<Assembly> LoadBuiltModuleAssemblies(string projectRoot)
    {
        var modulesRoot = Path.Combine(projectRoot, "Modules");
        if (!Directory.Exists(modulesRoot))
        {
            return Array.Empty<Assembly>();
        }

        var assemblies = new List<Assembly>();
        foreach (var projectPath in Directory.EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var assemblyPath = GetDefaultAssemblyPath(projectPath);
            if (!File.Exists(assemblyPath))
            {
                continue;
            }

            assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath)));
        }

        return assemblies;
    }

    private static string GetDefaultAssemblyPath(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var moduleName = Path.GetFileNameWithoutExtension(projectPath);
        return Path.Combine(projectDirectory, "bin", "Debug", "net10.0", $"{moduleName}.dll");
    }
}
