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
        foreach (var moduleDirectory in Directory.EnumerateDirectories(modulesRoot)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var moduleName = Path.GetFileName(moduleDirectory);
            var portableAssembly = Path.Combine(
                moduleDirectory,
                "bin",
                "rekall",
                "net10.0",
                $"{moduleName}.dll");
            var projectPath = Directory.EnumerateFiles(moduleDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault();
            var assemblyPath = File.Exists(portableAssembly)
                ? portableAssembly
                : projectPath is null
                    ? string.Empty
                    : GetDefaultAssemblyPath(projectPath);
            if (!File.Exists(assemblyPath))
            {
                continue;
            }

            var loadContext = new RekallAgeProjectModuleLoadContext(assemblyPath);
            using var assemblyStream = new FileStream(
                Path.GetFullPath(assemblyPath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            assemblies.Add(loadContext.LoadFromStream(assemblyStream));
        }

        return assemblies;
    }

    private static string GetDefaultAssemblyPath(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var moduleName = Path.GetFileNameWithoutExtension(projectPath);
        var portableSdkBuild = Path.Combine(projectDirectory, "bin", "rekall", "net10.0", $"{moduleName}.dll");
        return File.Exists(portableSdkBuild)
            ? portableSdkBuild
            : Path.Combine(projectDirectory, "bin", "Debug", "net10.0", $"{moduleName}.dll");
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
