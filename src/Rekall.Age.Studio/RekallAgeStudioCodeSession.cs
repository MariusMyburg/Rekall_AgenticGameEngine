using System.Diagnostics;
using System.IO;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor.Development;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Studio;

internal interface IRekallAgeStudioExternalLauncher
{
    void OpenAssociated(string path);

    void OpenVsCode(string projectRoot);
}

internal sealed class RekallAgeStudioShellLauncher : IRekallAgeStudioExternalLauncher
{
    public void OpenAssociated(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void OpenVsCode(string projectRoot)
    {
        var startInfo = new ProcessStartInfo("code") { UseShellExecute = true };
        startInfo.ArgumentList.Add(projectRoot);
        Process.Start(startInfo);
    }
}

internal sealed class RekallAgeStudioCodeSession
{
    private readonly IRekallAgeStudioExternalLauncher _launcher;
    private readonly RekallAgeProjectDevelopmentWorkspace _developmentWorkspace;
    private string? _projectRoot;
    private string _sourceText = string.Empty;
    private string _savedSourceText = string.Empty;

    public RekallAgeStudioCodeSession(
        IRekallAgeStudioExternalLauncher? launcher = null,
        RekallAgeProjectDevelopmentWorkspace? developmentWorkspace = null)
    {
        _launcher = launcher ?? new RekallAgeStudioShellLauncher();
        _developmentWorkspace = developmentWorkspace ?? new RekallAgeProjectDevelopmentWorkspace();
    }

    public RekallAgeModuleSourceInfo? SelectedSource { get; private set; }

    public string? SelectedProjectPath => SelectedSource is null
        ? null
        : Path.Combine(
            Path.GetDirectoryName(SelectedSource.SourcePath)!,
            $"{SelectedSource.ModuleName}.csproj");

    public string SourceText
    {
        get => _sourceText;
        set => _sourceText = value ?? string.Empty;
    }

    public bool IsDirty => SelectedSource is not null
        && !SourceText.Equals(_savedSourceText, StringComparison.Ordinal);

    public RekallAgeProjectDevelopmentWorkspaceResult? DevelopmentWorkspace { get; private set; }

    public async ValueTask<IReadOnlyList<RekallAgeModuleSourceInfo>> RefreshAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        var result = await new ListModuleSourcesCommand().ExecuteAsync(
            new ListModuleSourcesRequest(_projectRoot),
            CreateContext("List Studio code sources", cancellationToken)).ConfigureAwait(false);
        if (!result.Ok)
        {
            throw new InvalidOperationException(result.Summary);
        }

        return result.Value.Sources;
    }

    public async ValueTask OpenAsync(
        RekallAgeModuleSourceInfo source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_projectRoot is null || !IsReturnedProjectSource(_projectRoot, source.SourcePath))
        {
            throw new InvalidOperationException("The source file was not returned for the active project.");
        }

        var allowed = await RefreshAsync(_projectRoot, cancellationToken).ConfigureAwait(false);
        var selected = allowed.FirstOrDefault(candidate =>
            Path.GetFullPath(candidate.SourcePath).Equals(Path.GetFullPath(source.SourcePath), PathComparison));
        if (selected is null)
        {
            throw new InvalidOperationException("The source file is no longer available in the active project.");
        }

        var text = await File.ReadAllTextAsync(selected.SourcePath, cancellationToken).ConfigureAwait(false);
        SelectedSource = selected;
        _sourceText = text;
        _savedSourceText = text;
    }

    public async ValueTask SaveAsync(string projectRoot, CancellationToken cancellationToken)
    {
        if (SelectedSource is null || _projectRoot is null
            || !Path.GetFullPath(projectRoot).Equals(_projectRoot, PathComparison))
        {
            throw new InvalidOperationException("The selected source does not belong to the active project.");
        }

        var result = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(
                _projectRoot,
                SelectedSource.ModuleName,
                SelectedSource.FileName,
                SourceText),
            CreateContext("Save Studio code source", cancellationToken)).ConfigureAwait(false);
        if (!result.Ok)
        {
            throw new InvalidOperationException(result.Summary);
        }

        _savedSourceText = SourceText;
    }

    public async ValueTask<RekallAgeProjectDevelopmentWorkspaceResult> GenerateDevelopmentWorkspaceAsync(
        string projectRoot,
        string sceneName,
        string playerExecutablePath,
        string? cliExecutablePath,
        CancellationToken cancellationToken)
    {
        DevelopmentWorkspace = await _developmentWorkspace.GenerateAsync(
            new RekallAgeProjectDevelopmentWorkspaceRequest(
                projectRoot,
                sceneName,
                playerExecutablePath,
                cliExecutablePath),
            cancellationToken).ConfigureAwait(false);
        return DevelopmentWorkspace;
    }

    public void OpenSelectedFile() => _launcher.OpenAssociated(
        SelectedSource?.SourcePath ?? throw new InvalidOperationException("Select a C# source file first."));

    public void OpenSelectedProject() => _launcher.OpenAssociated(
        SelectedProjectPath ?? throw new InvalidOperationException("Select a module source file first."));

    public void OpenSolution() => _launcher.OpenAssociated(
        DevelopmentWorkspace?.SolutionPath ?? throw new InvalidOperationException("Generate the development workspace first."));

    public void OpenInVsCode() => _launcher.OpenVsCode(
        _projectRoot ?? throw new InvalidOperationException("Open a project first."));

    private static RekallAgeCommandContext CreateContext(string name, CancellationToken cancellationToken) =>
        new("studio-code", RekallAgeTransaction.Begin(name), cancellationToken);

    private static bool IsReturnedProjectSource(string projectRoot, string path)
    {
        var modulesRoot = Path.Combine(projectRoot, "Modules")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(modulesRoot, PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
