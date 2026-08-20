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
    string EvidencePath);

public sealed record RekallAgeStudioAutomationResult(
    bool Succeeded,
    string Status,
    string ProjectRoot,
    string SceneName,
    bool NonblankViewport,
    string ViewportSummary,
    string PackageArchivePath,
    IReadOnlyList<string> Validation,
    IReadOnlyList<string> AgentTranscript);

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

        options = new RekallAgeStudioAutomationOptions(
            projectRoot!, projectName!, sceneName, model!, task!, evidencePath!);
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

        await ((RekallAgeAsyncCommand)viewModel.CreateCommand).ExecuteAsync(null);
        cancellationToken.ThrowIfCancellationRequested();
        await ((RekallAgeAsyncCommand)viewModel.RunAgentCommand).ExecuteAsync(null);

        var packageArchivePath = Path.Combine(projectRoot, "Builds", "AgentAuthoringGauntlet.zip");
        var nonblankViewport = viewModel.ViewportImage is { PixelWidth: > 0, PixelHeight: > 0 };
        var result = new RekallAgeStudioAutomationResult(
            viewModel.StatusText.StartsWith("AI authoring completed", StringComparison.Ordinal)
                && nonblankViewport
                && File.Exists(packageArchivePath),
            viewModel.StatusText,
            projectRoot,
            options.SceneName,
            nonblankViewport,
            viewModel.ViewportSummary,
            packageArchivePath,
            viewModel.ValidationLines.ToArray(),
            viewModel.AgentLines.ToArray());

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
