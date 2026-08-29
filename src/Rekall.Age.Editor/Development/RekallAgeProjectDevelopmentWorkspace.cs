using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    string VisualStudioSolutionLaunchPath,
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
        var cliPath = RequireExistingFile(request.CliExecutablePath, nameof(request.CliExecutablePath));
        var manifest = await new RekallAgeProjectStore().LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);

        var workspaceRoot = Path.Combine(projectRoot, ".rekall", "ide");
        var ideRoot = Path.Combine(workspaceRoot, "Rekall.Game.Debug");
        var debugProjectPath = Path.Combine(ideRoot, "Rekall.Game.Debug.csproj");
        var programPath = Path.Combine(ideRoot, "Program.cs");
        var launchSettingsPath = Path.Combine(ideRoot, "Properties", "launchSettings.json");
        var vscodeRoot = Path.Combine(projectRoot, ".vscode");
        var vscodeLaunchPath = Path.Combine(vscodeRoot, "launch.json");
        var vscodeTasksPath = Path.Combine(vscodeRoot, "tasks.json");
        var solutionPath = Path.Combine(workspaceRoot, $"{ToSafeFileName(manifest.Name)}.slnx");
        var solutionLaunchPath = Path.ChangeExtension(solutionPath, ".slnLaunch");

        var modulesRoot = Path.Combine(projectRoot, "Modules");
        var moduleFiles = Directory.Exists(modulesRoot)
            ? Directory.EnumerateFiles(modulesRoot, "*", SafeRecursiveEnumeration)
                .Where(path => !ContainsGeneratedSegment(projectRoot, path))
                .Where(path => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        var vscodeLaunch = await MergeNamedArrayAsync(
            vscodeLaunchPath,
            "configurations",
            "name",
            "0.2.0",
            cancellationToken,
            ("Rekall AGE: Play Game", JsonSerializer.SerializeToNode(CreateVsCodeLaunchConfiguration(playerPath, projectRoot, sceneName))!)).ConfigureAwait(false);
        var vscodeTasks = await MergeNamedArrayAsync(
            vscodeTasksPath,
            "tasks",
            "label",
            "2.0.0",
            cancellationToken,
            ("Rekall AGE: Refresh Module SDK", JsonSerializer.SerializeToNode(CreateVsCodeSdkTask(cliPath, projectRoot))!),
            ("Rekall AGE: Build Modules", JsonSerializer.SerializeToNode(CreateVsCodeTask(cliPath, projectRoot))!)).ConfigureAwait(false);

        await WriteAsync(debugProjectPath, CreateDebugProject(cliPath, projectRoot, ideRoot, moduleFiles), cancellationToken).ConfigureAwait(false);
        await WriteAsync(programPath, CreateDebugProgram(), cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            launchSettingsPath,
            JsonSerializer.Serialize(CreateVisualStudioLaunchSettings(playerPath, projectRoot, sceneName), JsonOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            vscodeLaunchPath,
            vscodeLaunch,
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            vscodeTasksPath,
            vscodeTasks,
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(solutionPath, CreateSolution(Path.GetDirectoryName(solutionPath)!, debugProjectPath), cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            solutionLaunchPath,
            JsonSerializer.Serialize(CreateVisualStudioSolutionLaunch(
                Path.GetDirectoryName(solutionPath)!,
                debugProjectPath), JsonOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);

        return new RekallAgeProjectDevelopmentWorkspaceResult(
            solutionPath,
            solutionLaunchPath,
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

    private static object[] CreateVisualStudioSolutionLaunch(string solutionDirectory, string debugProjectPath) =>
    [
        new
        {
            Name = "Rekall AGE Game",
            Projects = new[]
            {
                new
                {
                    Path = Path.GetRelativePath(solutionDirectory, debugProjectPath).Replace('/', '\\'),
                    Action = "Start"
                }
            }
        }
    ];

    private static object CreateVsCodeLaunchConfiguration(string playerPath, string projectRoot, string sceneName) => new
    {
        name = "Rekall AGE: Play Game",
        type = "coreclr",
        request = "launch",
        program = playerPath,
        args = new[] { projectRoot, sceneName, "--graphics", "--backend", "vulkan" },
        cwd = projectRoot,
        preLaunchTask = "Rekall AGE: Build Modules"
    };

    private static object CreateVsCodeTask(string cliPath, string projectRoot) => new
    {
        label = "Rekall AGE: Build Modules",
        type = "process",
        command = cliPath,
        args = new[] { "build", "modules", projectRoot },
        options = new { cwd = projectRoot },
        problemMatcher = "$msCompile",
        dependsOn = new[] { "Rekall AGE: Refresh Module SDK" }
    };

    private static object CreateVsCodeSdkTask(string cliPath, string projectRoot) => new
    {
        label = "Rekall AGE: Refresh Module SDK",
        type = "process",
        command = cliPath,
        args = new[] { "module", "install-sdk", projectRoot },
        options = new { cwd = projectRoot },
        problemMatcher = Array.Empty<string>()
    };

    private static string CreateSolution(
        string solutionDirectory,
        string debugProjectPath)
    {
        var path = Path.GetRelativePath(solutionDirectory, debugProjectPath).Replace('/', '\\');
        return "<Solution>\n"
            + $"  <Project Path=\"{SecurityElement.Escape(path)}\" />\n"
            + "</Solution>\n";
    }

    private static string CreateDebugProject(
        string cliPath,
        string projectRoot,
        string debugProjectDirectory,
        IEnumerable<string> moduleFiles)
    {
        var installCommand = SecurityElement.Escape($"\"{cliPath}\" module install-sdk \"{projectRoot}\"");
        var command = SecurityElement.Escape($"\"{cliPath}\" build modules \"{projectRoot}\"");
        var linkedFiles = string.Concat(moduleFiles.Select(path =>
        {
            var include = Path.GetRelativePath(debugProjectDirectory, path).Replace('/', '\\');
            var projectRelative = Path.GetRelativePath(projectRoot, path).Replace('/', '\\');
            var link = projectRelative.StartsWith("Modules\\", StringComparison.OrdinalIgnoreCase)
                ? $"Game Modules\\{projectRelative["Modules\\".Length..]}"
                : $"Game Modules\\{Path.GetFileName(path)}";
            return $"    <None Include=\"{SecurityElement.Escape(include)}\" Link=\"{SecurityElement.Escape(link)}\" />\n";
        }));
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>
              </PropertyGroup>
              <ItemGroup>
            {linkedFiles}  </ItemGroup>
              <Target Name="BuildRekallModules" BeforeTargets="Build">
                <Exec Command="{installCommand}" />
                <Exec Command="{command}" />
              </Target>
            </Project>

            """;
    }

    private static string CreateDebugProgram() => """
        Console.WriteLine("Use the 'Rekall AGE Game' launch profile or press F5 to start the production Player.");

        """;

    private static async ValueTask WriteAsync(string path, string contents, CancellationToken cancellationToken) =>
        await RekallAgeAtomicFile.WriteAllTextAsync(path, contents, MaximumGeneratedFileBytes, cancellationToken).ConfigureAwait(false);

    private static async ValueTask<string> MergeNamedArrayAsync(
        string path,
        string arrayName,
        string identityName,
        string defaultVersion,
        CancellationToken cancellationToken,
        params (string IdentityValue, JsonNode GeneratedItem)[] generatedItems)
    {
        JsonObject document;
        if (File.Exists(path))
        {
            var existing = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            document = JsonNode.Parse(
                existing,
                documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                ?? throw new InvalidDataException($"IDE configuration '{path}' must contain a JSON object.");
        }
        else
        {
            document = new JsonObject { ["version"] = defaultVersion };
        }

        var items = document[arrayName] as JsonArray ?? new JsonArray();
        document[arrayName] = items;
        var identities = generatedItems.Select(item => item.IdentityValue).ToHashSet(StringComparer.Ordinal);
        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (items[index]?[identityName]?.GetValue<string>() is { } identity && identities.Contains(identity))
            {
                items.RemoveAt(index);
            }
        }
        foreach (var generatedItem in generatedItems)
        {
            items.Add(generatedItem.GeneratedItem);
        }
        return document.ToJsonString(JsonOptions) + Environment.NewLine;
    }

    private static bool ContainsGeneratedSegment(string root, string path)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".rekall", StringComparison.OrdinalIgnoreCase));
    }

    private static EnumerationOptions SafeRecursiveEnumeration => new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false
    };

    private static string RequireExistingDirectory(string path, string parameterName)
    {
        var fullPath = Path.GetFullPath(RequireValue(path, parameterName));
        return Directory.Exists(fullPath)
            ? fullPath
            : throw new DirectoryNotFoundException($"Project directory '{fullPath}' does not exist.");
    }

    private static string RequireExistingFile(string? path, string parameterName)
    {
        var fullPath = Path.GetFullPath(RequireValue(path, parameterName));
        return File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException($"Required executable '{fullPath}' does not exist.", fullPath);
    }

    private static string RequireValue(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", parameterName) : value.Trim();

    private static string ToSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "RekallGame" : safe;
    }
}
