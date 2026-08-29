using System.Security;
using System.Text.Json;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Project;

namespace Rekall.Age.Editor.Development;

public sealed record RekallAgeProjectDevelopmentWorkspaceRequest(
    string ProjectRoot,
    string SceneName,
    string PlayerExecutablePath,
    string? CliExecutablePath = null);

public sealed record RekallAgeProjectDevelopmentWorkspaceResult(
    string SolutionPath,
    string DebugProjectPath,
    string VisualStudioLaunchSettingsPath,
    string VsCodeLaunchPath,
    string VsCodeTasksPath);

public sealed class RekallAgeProjectDevelopmentWorkspace
{
    private const long MaximumGeneratedFileBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async ValueTask<RekallAgeProjectDevelopmentWorkspaceResult> GenerateAsync(
        RekallAgeProjectDevelopmentWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var projectRoot = RequireExistingDirectory(request.ProjectRoot, nameof(request.ProjectRoot));
        var sceneName = RequireValue(request.SceneName, nameof(request.SceneName));
        var playerPath = RequireExistingFile(request.PlayerExecutablePath, nameof(request.PlayerExecutablePath));
        var cliPath = string.IsNullOrWhiteSpace(request.CliExecutablePath)
            ? null
            : RequireExistingFile(request.CliExecutablePath, nameof(request.CliExecutablePath));
        var manifest = await new RekallAgeProjectStore().LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);

        var ideRoot = Path.Combine(projectRoot, ".rekall", "ide", "Rekall.Game.Debug");
        var debugProjectPath = Path.Combine(ideRoot, "Rekall.Game.Debug.csproj");
        var programPath = Path.Combine(ideRoot, "Program.cs");
        var launchSettingsPath = Path.Combine(ideRoot, "Properties", "launchSettings.json");
        var vscodeRoot = Path.Combine(projectRoot, ".vscode");
        var vscodeLaunchPath = Path.Combine(vscodeRoot, "launch.json");
        var vscodeTasksPath = Path.Combine(vscodeRoot, "tasks.json");
        var solutionPath = Path.Combine(projectRoot, $"{ToSafeFileName(manifest.Name)}.slnx");

        var moduleProjects = Directory.Exists(Path.Combine(projectRoot, "Modules"))
            ? Directory.EnumerateFiles(Path.Combine(projectRoot, "Modules"), "*.csproj", SearchOption.AllDirectories)
                .Where(path => !ContainsGeneratedSegment(projectRoot, path))
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        await WriteAsync(debugProjectPath, CreateDebugProject(), cancellationToken).ConfigureAwait(false);
        await WriteAsync(programPath, CreateDebugProgram(), cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            launchSettingsPath,
            JsonSerializer.Serialize(CreateVisualStudioLaunchSettings(playerPath, projectRoot, sceneName), JsonOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            vscodeLaunchPath,
            JsonSerializer.Serialize(CreateVsCodeLaunch(playerPath, projectRoot, sceneName), JsonOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            vscodeTasksPath,
            JsonSerializer.Serialize(CreateVsCodeTasks(cliPath, solutionPath, projectRoot), JsonOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(solutionPath, CreateSolution(projectRoot, debugProjectPath, moduleProjects), cancellationToken).ConfigureAwait(false);

        return new RekallAgeProjectDevelopmentWorkspaceResult(
            solutionPath,
            debugProjectPath,
            launchSettingsPath,
            vscodeLaunchPath,
            vscodeTasksPath);
    }

    private static object CreateVisualStudioLaunchSettings(string playerPath, string projectRoot, string sceneName) =>
        new
        {
            profiles = new Dictionary<string, object>
            {
                ["Rekall AGE Game"] = new
                {
                    commandName = "Executable",
                    executablePath = playerPath,
                    commandLineArgs = $"\"{projectRoot}\" \"{sceneName}\" --graphics --backend vulkan",
                    workingDirectory = projectRoot
                }
            }
        };

    private static object CreateVsCodeLaunch(string playerPath, string projectRoot, string sceneName) => new
    {
        version = "0.2.0",
        configurations = new[]
        {
            new
            {
                name = "Rekall AGE: Play Game",
                type = "coreclr",
                request = "launch",
                program = playerPath,
                args = new[] { projectRoot, sceneName, "--graphics", "--backend", "vulkan" },
                cwd = projectRoot,
                preLaunchTask = "Rekall AGE: Build Modules"
            }
        }
    };

    private static object CreateVsCodeTasks(string? cliPath, string solutionPath, string projectRoot) => new
    {
        version = "2.0.0",
        tasks = new[]
        {
            cliPath is null
                ? new
                {
                    label = "Rekall AGE: Build Modules",
                    type = "process",
                    command = "dotnet",
                    args = new[] { "build", solutionPath },
                    options = new { cwd = projectRoot },
                    problemMatcher = "$msCompile"
                }
                : new
                {
                    label = "Rekall AGE: Build Modules",
                    type = "process",
                    command = cliPath,
                    args = new[] { "build", "modules", projectRoot },
                    options = new { cwd = projectRoot },
                    problemMatcher = "$msCompile"
                }
        }
    };

    private static string CreateSolution(
        string projectRoot,
        string debugProjectPath,
        IEnumerable<string> moduleProjects)
    {
        var paths = new[] { debugProjectPath }.Concat(moduleProjects)
            .Select(path => Path.GetRelativePath(projectRoot, path).Replace('/', '\\'));
        return "<Solution>\n"
            + string.Concat(paths.Select(path => $"  <Project Path=\"{SecurityElement.Escape(path)}\" />\n"))
            + "</Solution>\n";
    }

    private static string CreateDebugProject() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>

        """;

    private static string CreateDebugProgram() => """
        Console.WriteLine("Use the 'Rekall AGE Game' launch profile or press F5 to start the production Player.");

        """;

    private static async ValueTask WriteAsync(string path, string contents, CancellationToken cancellationToken) =>
        await RekallAgeAtomicFile.WriteAllTextAsync(path, contents, MaximumGeneratedFileBytes, cancellationToken).ConfigureAwait(false);

    private static bool ContainsGeneratedSegment(string root, string path)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".rekall", StringComparison.OrdinalIgnoreCase));
    }

    private static string RequireExistingDirectory(string path, string parameterName)
    {
        var fullPath = Path.GetFullPath(RequireValue(path, parameterName));
        return Directory.Exists(fullPath)
            ? fullPath
            : throw new DirectoryNotFoundException($"Project directory '{fullPath}' does not exist.");
    }

    private static string RequireExistingFile(string path, string parameterName)
    {
        var fullPath = Path.GetFullPath(RequireValue(path, parameterName));
        return File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException($"Required executable '{fullPath}' does not exist.", fullPath);
    }

    private static string RequireValue(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", parameterName) : value.Trim();

    private static string ToSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "RekallGame" : safe;
    }
}
