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

            assemblies.Add(new RekallAgeProjectModuleLoadContext(assemblyPath)
                .LoadFromAssemblyPath(Path.GetFullPath(assemblyPath)));
        }

        return assemblies;
    }

    private static string GetDefaultAssemblyPath(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var moduleName = Path.GetFileNameWithoutExtension(projectPath);
        return Path.Combine(projectDirectory, "bin", "Debug", "net10.0", $"{moduleName}.dll");
    }

    private sealed class RekallAgeProjectModuleLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public RekallAgeProjectModuleLoadContext(string mainAssemblyPath)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(Path.GetFullPath(mainAssemblyPath));
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null
                && assemblyName.Name.StartsWith("Rekall.Age.", StringComparison.Ordinal))
            {
                return AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName.Name);
            }

            var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath is null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }
}
