using System.IO;
using System.Text.Json;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Editor;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio;

public sealed record RekallAgeStudioAutomationOptions(
    string ProjectRoot,
    string ProjectName,
    string SceneName,
    string Model,
    string Task,
    string EvidencePath)
{
    public bool TreatGauntletAsTerminalSuccess { get; init; } = true;

    public int? MaxTurns { get; init; }
}

public sealed record RekallAgeStudioAutomationResult(
    bool Succeeded,
    string Status,
    string ProjectRoot,
    string SceneName,
    bool NonblankViewport,
    bool VisuallyInformativeViewport,
    string ViewportSummary,
    int ViewportRenderableCount,
    string PackageArchivePath,
    IReadOnlyList<string> Validation,
    IReadOnlyList<string> AgentTranscript)
{
    public IReadOnlyList<RekallAgeLanguageModelToolExecution> AgentToolExecutions { get; init; } = [];
}

public static class RekallAgeStudioAutomation
{
    public const string AutomationSwitch = "--studio-agent-automation";

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out RekallAgeStudioAutomationOptions? options,
        out string? error)
    {
        options = null;
        error = null;
        if (!arguments.Contains(AutomationSwitch, StringComparer.Ordinal))
        {
            error = $"Missing {AutomationSwitch}.";
            return false;
        }

        string? Read(string name)
        {
            var index = arguments.IndexOf(name);
            return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
        }

        var projectRoot = Read("--project");
        var projectName = Read("--project-name");
        var sceneName = Read("--scene") ?? "Main";
        var model = Read("--model");
        var task = Read("--task");
        var evidencePath = Read("--evidence");
        var maxTurnsText = Read("--max-turns");
        var missing = new[]
        {
            ("--project", projectRoot),
            ("--project-name", projectName),
            ("--model", model),
            ("--task", task),
            ("--evidence", evidencePath)
        }.Where(item => string.IsNullOrWhiteSpace(item.Item2)).Select(item => item.Item1).ToArray();
        if (missing.Length > 0)
        {
            error = $"Missing required Studio automation arguments: {string.Join(", ", missing)}.";
            return false;
        }

        int? maxTurns = null;
        if (maxTurnsText is not null)
        {
            if (!int.TryParse(maxTurnsText, out var parsedMaxTurns) || parsedMaxTurns is < 1 or > 64)
            {
                error = "--max-turns must be an integer from 1 through 64.";
                return false;
            }
            maxTurns = parsedMaxTurns;
        }

        options = new RekallAgeStudioAutomationOptions(
            projectRoot!, projectName!, sceneName, model!, task!, evidencePath!)
        {
            TreatGauntletAsTerminalSuccess = !arguments.Contains("--require-task-specific-completion", StringComparer.Ordinal),
            MaxTurns = maxTurns
        };
        return true;
    }

    public static async Task<RekallAgeStudioAutomationResult> RunAsync(
        RekallAgeStudioAutomationOptions options,
        IRekallAgeLanguageModelClient? languageModelClient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var projectRoot = Path.GetFullPath(options.ProjectRoot);
        var evidencePath = Path.GetFullPath(options.EvidencePath);
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            languageModelClient);
        viewModel.ProjectPathInput = projectRoot;
        viewModel.ProjectNameInput = options.ProjectName;
        viewModel.SceneNameInput = options.SceneName;
        viewModel.SelectedOllamaModel = options.Model;
        viewModel.AgentTaskInput = options.Task;
        viewModel.TreatGauntletAsTerminalSuccess = options.TreatGauntletAsTerminalSuccess;
        viewModel.AgentMaxTurns = options.MaxTurns;

        var projectCommand = File.Exists(Path.Combine(projectRoot, "rekall.project.json"))
            ? viewModel.OpenCommand
            : viewModel.CreateCommand;
        await ((RekallAgeAsyncCommand)projectCommand).ExecuteAsync(null);
        cancellationToken.ThrowIfCancellationRequested();
        await ((RekallAgeAsyncCommand)viewModel.RunAgentCommand).ExecuteAsync(null);

        var packageArchivePath = ResolvePackageArchivePath(projectRoot);
        var nonblankViewport = viewModel.ViewportImage is { PixelWidth: > 0, PixelHeight: > 0 }
            && viewModel.ViewportRenderableCount > 0;
        var result = new RekallAgeStudioAutomationResult(
            IsSuccessful(
                viewModel.StatusText,
                nonblankViewport,
                viewModel.ViewportVisuallyInformative,
                !options.TreatGauntletAsTerminalSuccess,
                packageArchivePath),
            viewModel.StatusText,
            projectRoot,
            options.SceneName,
            nonblankViewport,
            viewModel.ViewportVisuallyInformative,
            viewModel.ViewportSummary,
            viewModel.ViewportRenderableCount,
            packageArchivePath ?? string.Empty,
            viewModel.ValidationLines.ToArray(),
            viewModel.AgentLines.ToArray())
        {
            AgentToolExecutions = viewModel.LastAgentToolExecutions
        };

        var evidenceDirectory = Path.GetDirectoryName(evidencePath)
            ?? throw new ArgumentException("Evidence path must have a parent directory.", nameof(options));
        Directory.CreateDirectory(evidenceDirectory);
        var temporaryPath = evidencePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            File.Move(temporaryPath, evidencePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        return result;
    }

    internal static bool IsSuccessful(
        string status,
        bool nonblankViewport,
        bool visuallyInformativeViewport,
        bool requireVisuallyInformativeViewport,
        string? packageArchivePath) =>
        status.StartsWith("AI authoring completed", StringComparison.Ordinal)
        && nonblankViewport
        && (!requireVisuallyInformativeViewport || visuallyInformativeViewport)
        && packageArchivePath is not null
        && File.Exists(packageArchivePath);

    internal static string? ResolvePackageArchivePath(string projectRoot)
    {
        if (!Directory.Exists(projectRoot)) return null;
        const int maximumDirectories = 1_024;
        const int maximumArchives = 256;
        var directories = new Stack<string>();
        var archives = new List<string>();
        directories.Push(projectRoot);
        var visitedDirectories = 0;
        while (directories.Count > 0
            && visitedDirectories < maximumDirectories
            && archives.Count < maximumArchives)
        {
            var directory = directories.Pop();
            visitedDirectories++;
            try
            {
                archives.AddRange(Directory.EnumerateFiles(directory, "*.zip", SearchOption.TopDirectoryOnly)
                    .Take(maximumArchives - archives.Count));
                foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        directories.Push(child);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Package evidence discovery is best-effort and never follows inaccessible paths.
            }
        }

        return archives
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}

internal static class RekallAgeArgumentListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index].Equals(value, StringComparison.Ordinal)) return index;
        }
        return -1;
    }
}
