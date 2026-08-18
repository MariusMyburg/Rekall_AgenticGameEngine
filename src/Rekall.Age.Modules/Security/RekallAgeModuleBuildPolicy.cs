using Rekall.Age.Modules.Sdk;

namespace Rekall.Age.Modules.Security;

public sealed record RekallAgeModuleBuildPolicyLimits(
    int MaximumModules = 256,
    int MaximumSourcesPerModule = 256,
    long MaximumSourceBytes = 4L * 1024 * 1024,
    long MaximumTotalSourceBytesPerModule = 32L * 1024 * 1024,
    long MaximumProjectBytes = 1024L * 1024,
    int MaximumOutputEntriesPerModule = 4_096);

public sealed record RekallAgeModuleBuildCandidate(
    string ModuleName,
    string ModuleDirectory,
    string ProjectPath,
    string OutputDirectory,
    IReadOnlyList<string> SourcePaths);

public sealed record RekallAgeModuleBuildPolicyIssue(
    string Message,
    string Target);

public sealed record RekallAgeModuleBuildPolicyResult(
    bool Ready,
    IReadOnlyList<RekallAgeModuleBuildCandidate> Candidates,
    IReadOnlyList<RekallAgeModuleBuildPolicyIssue> Issues);

public sealed class RekallAgeModuleBuildPolicy
{
    private static readonly string[] AllowedGeneratedDirectories = ["bin", "obj"];
    private readonly RekallAgeModuleBuildPolicyLimits _limits;
    private readonly Func<string, FileAttributes> _readAttributes;

    public RekallAgeModuleBuildPolicy(
        RekallAgeModuleBuildPolicyLimits? limits = null,
        Func<string, FileAttributes>? readAttributes = null)
    {
        _limits = limits ?? new RekallAgeModuleBuildPolicyLimits();
        _readAttributes = readAttributes ?? File.GetAttributes;
        ValidateLimits(_limits);
    }

    public RekallAgeModuleBuildPolicyResult Inspect(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var root = Path.GetFullPath(projectRoot);
        var modulesRoot = Path.Combine(root, "Modules");
        var issues = new List<RekallAgeModuleBuildPolicyIssue>();
        var candidates = new List<RekallAgeModuleBuildCandidate>();
        if (!Directory.Exists(modulesRoot))
        {
            return new RekallAgeModuleBuildPolicyResult(true, candidates, issues);
        }

        if (!IsSafeExistingPath(root, root, issues) || !IsSafeExistingPath(modulesRoot, root, issues))
        {
            return Failed(candidates, issues);
        }

        var moduleDirectories = Directory.EnumerateDirectories(modulesRoot, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(_limits.MaximumModules + 1)
            .ToArray();
        if (moduleDirectories.Length > _limits.MaximumModules)
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                $"Project modules exceed the {_limits.MaximumModules} module limit.",
                modulesRoot));
            return Failed(candidates, issues);
        }

        foreach (var moduleDirectory in moduleDirectories)
        {
            InspectModule(root, moduleDirectory, candidates, issues);
        }

        return issues.Count == 0
            ? new RekallAgeModuleBuildPolicyResult(true, candidates, issues)
            : Failed(candidates, issues);
    }

    private void InspectModule(
        string projectRoot,
        string moduleDirectory,
        ICollection<RekallAgeModuleBuildCandidate> candidates,
        ICollection<RekallAgeModuleBuildPolicyIssue> issues)
    {
        if (!IsSafeExistingPath(moduleDirectory, projectRoot, issues))
        {
            return;
        }

        var moduleName = Path.GetFileName(moduleDirectory);
        var childDirectories = Directory.EnumerateDirectories(moduleDirectory, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(AllowedGeneratedDirectories.Length + 1)
            .ToArray();
        if (childDirectories.Length > AllowedGeneratedDirectories.Length)
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                $"Module '{moduleName}' contains more direct subdirectories than the canonical bin/obj layout permits.",
                moduleDirectory));
        }

        foreach (var childDirectory in childDirectories)
        {
            var childName = Path.GetFileName(childDirectory);
            if (!AllowedGeneratedDirectories.Contains(childName, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new RekallAgeModuleBuildPolicyIssue(
                    $"Module '{moduleName}' contains unsupported nested directory '{childName}'. Module source files must be direct children.",
                    childDirectory));
                continue;
            }

            IsSafeExistingPath(childDirectory, moduleDirectory, issues);
        }

        var projects = Directory.EnumerateFiles(moduleDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (projects.Length != 1)
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                $"Module '{moduleName}' must contain exactly one direct C# project file.",
                moduleDirectory));
            return;
        }

        var projectPath = projects[0];
        if (!Path.GetFileNameWithoutExtension(projectPath).Equals(moduleName, StringComparison.Ordinal)
            || !IsSafeExistingPath(projectPath, moduleDirectory, issues))
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                $"Module project filename must match its direct module directory '{moduleName}'.",
                projectPath));
            return;
        }

        var projectInfo = new FileInfo(projectPath);
        if (projectInfo.Length > _limits.MaximumProjectBytes)
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                $"Module project exceeds the {_limits.MaximumProjectBytes} byte limit.",
                projectPath));
            return;
        }

        var actualProject = NormalizeProject(File.ReadAllText(projectPath));
        var canonicalProject = NormalizeProject(RekallAgeModuleProjectFile.Create(moduleName));
        if (!actualProject.Equals(canonicalProject, StringComparison.Ordinal))
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                $"Module '{moduleName}' project is not the canonical engine-generated Rekall AGE project. Author C# source files instead of build targets, imports, or references.",
                projectPath));
            return;
        }

        var sources = Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(_limits.MaximumSourcesPerModule + 1)
            .ToArray();
        if (sources.Length == 0 || sources.Length > _limits.MaximumSourcesPerModule)
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                sources.Length == 0
                    ? $"Module '{moduleName}' must contain at least one direct C# source file."
                    : $"Module '{moduleName}' exceeds the {_limits.MaximumSourcesPerModule} source-file limit.",
                moduleDirectory));
            return;
        }

        long totalSourceBytes = 0;
        foreach (var source in sources)
        {
            if (!IsSafeExistingPath(source, moduleDirectory, issues))
            {
                continue;
            }

            var length = new FileInfo(source).Length;
            totalSourceBytes = checked(totalSourceBytes + length);
            if (length > _limits.MaximumSourceBytes)
            {
                issues.Add(new RekallAgeModuleBuildPolicyIssue(
                    $"Module source exceeds the {_limits.MaximumSourceBytes} byte per-file limit.",
                    source));
            }
        }

        if (totalSourceBytes > _limits.MaximumTotalSourceBytesPerModule)
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                $"Module '{moduleName}' sources exceed the {_limits.MaximumTotalSourceBytesPerModule} byte total limit.",
                moduleDirectory));
        }

        var outputDirectory = Path.Combine(moduleDirectory, "bin", "rekall", "net10.0");
        InspectOutputTree(outputDirectory, moduleDirectory, issues);
        if (issues.Count > 0)
        {
            return;
        }

        candidates.Add(new RekallAgeModuleBuildCandidate(
            moduleName,
            moduleDirectory,
            projectPath,
            outputDirectory,
            sources));
    }

    private void InspectOutputTree(
        string outputDirectory,
        string moduleDirectory,
        ICollection<RekallAgeModuleBuildPolicyIssue> issues)
    {
        var current = outputDirectory;
        var existingAncestors = new Stack<string>();
        while (!Path.GetFullPath(current).Equals(Path.GetFullPath(moduleDirectory), PathComparison))
        {
            if (Directory.Exists(current))
            {
                existingAncestors.Push(current);
            }

            current = Path.GetDirectoryName(current)!;
        }

        foreach (var ancestor in existingAncestors)
        {
            if (!IsSafeExistingPath(ancestor, moduleDirectory, issues))
            {
                return;
            }
        }

        if (!Directory.Exists(outputDirectory))
        {
            return;
        }

        var pending = new Queue<string>();
        pending.Enqueue(outputDirectory);
        var entries = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                entries++;
                if (entries > _limits.MaximumOutputEntriesPerModule)
                {
                    issues.Add(new RekallAgeModuleBuildPolicyIssue(
                        $"Module output exceeds the {_limits.MaximumOutputEntriesPerModule} entry inspection limit.",
                        outputDirectory));
                    return;
                }

                if (!IsSafeExistingPath(entry, outputDirectory, issues))
                {
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    pending.Enqueue(entry);
                }
            }
        }
    }

    private bool IsSafeExistingPath(
        string path,
        string expectedRoot,
        ICollection<RekallAgeModuleBuildPolicyIssue> issues)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.Equals(fullRoot, comparison)
            && !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                "Module build path escapes its expected root.",
                fullPath));
            return false;
        }

        if ((_readAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            issues.Add(new RekallAgeModuleBuildPolicyIssue(
                "Module build paths cannot use symbolic links, junctions, or other reparse points.",
                fullPath));
            return false;
        }

        return true;
    }

    private static string NormalizeProject(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static RekallAgeModuleBuildPolicyResult Failed(
        IReadOnlyList<RekallAgeModuleBuildCandidate> candidates,
        IReadOnlyList<RekallAgeModuleBuildPolicyIssue> issues)
    {
        return new RekallAgeModuleBuildPolicyResult(false, candidates, issues);
    }

    private static void ValidateLimits(RekallAgeModuleBuildPolicyLimits limits)
    {
        if (limits.MaximumModules < 1
            || limits.MaximumSourcesPerModule < 1
            || limits.MaximumSourceBytes < 1
            || limits.MaximumTotalSourceBytesPerModule < 1
            || limits.MaximumProjectBytes < 1
            || limits.MaximumOutputEntriesPerModule < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "All module build policy limits must be positive.");
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
