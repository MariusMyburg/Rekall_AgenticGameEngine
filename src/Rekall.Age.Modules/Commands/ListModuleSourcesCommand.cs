using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Commands;

public sealed record ListModuleSourcesRequest(string ProjectRoot);

public sealed record RekallAgeModuleSourceInfo(
    string ModuleName,
    string FileName,
    string SourcePath,
    string? ProjectPath,
    long Bytes);

public sealed record ListModuleSourcesResult(IReadOnlyList<RekallAgeModuleSourceInfo> Sources);

public sealed class ListModuleSourcesCommand
    : IRekallAgeCommand<ListModuleSourcesRequest, ListModuleSourcesResult>
{
    public string Name => "rekall.module.list_sources";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Lists C# module source files under a Rekall AGE project.",
        typeof(ListModuleSourcesRequest).FullName!,
        typeof(ListModuleSourcesResult).FullName!);

    public ValueTask<RekallAgeCommandResult<ListModuleSourcesResult>> ExecuteAsync(
        ListModuleSourcesRequest request,
        RekallAgeCommandContext context)
    {
        var modulesRoot = RekallAgeModuleSourcePaths.GetModulesRoot(request.ProjectRoot);
        if (!Directory.Exists(modulesRoot))
        {
            return ValueTask.FromResult(RekallAgeCommandResult<ListModuleSourcesResult>.Success(
                new ListModuleSourcesResult([]),
                "No module sources found."));
        }

        var sources = Directory.EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsGeneratedDirectory(modulesRoot, path))
            .Where(path => !RekallAgeModuleSourcePaths.ContainsReparsePoint(modulesRoot, path))
            .Where(path => Path.GetRelativePath(modulesRoot, path).IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(modulesRoot, path);
                var segments = relative.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);
                var moduleName = segments[0];
                var moduleRoot = Path.Combine(modulesRoot, moduleName);
                var projectPath = Directory.EnumerateFiles(moduleRoot, "*.csproj", SearchOption.TopDirectoryOnly)
                    .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
                    .Select(Path.GetFullPath)
                    .FirstOrDefault();
                return new RekallAgeModuleSourceInfo(
                    moduleName,
                    Path.GetRelativePath(moduleRoot, path),
                    Path.GetFullPath(path),
                    projectPath,
                    new FileInfo(path).Length);
            })
            .OrderBy(source => source.ModuleName, StringComparer.Ordinal)
            .ThenBy(source => source.FileName, StringComparer.Ordinal)
            .ToArray();

        return ValueTask.FromResult(RekallAgeCommandResult<ListModuleSourcesResult>.Success(
            new ListModuleSourcesResult(sources),
            $"Found {sources.Length} module source file(s)."));
    }

    private static bool ContainsGeneratedDirectory(string modulesRoot, string path)
    {
        var relative = Path.GetRelativePath(modulesRoot, path);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
