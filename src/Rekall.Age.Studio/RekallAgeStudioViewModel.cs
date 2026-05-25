using System.Collections.ObjectModel;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioViewModel
{
    public RekallAgeStudioViewModel(RekallAgeWorkbenchModel? model)
        : this(
            model,
            model is null ? ["No project loaded"] : null,
            model is null ? ["Select an entity"] : null,
            null,
            model is null ? "Open a Rekall AGE project to begin." : null)
    {
    }

    private RekallAgeStudioViewModel(
        RekallAgeWorkbenchModel? model,
        IEnumerable<string>? entityFallbackLines,
        IEnumerable<string>? inspectorFallbackLines,
        IEnumerable<string>? validationFallbackLines,
        string? viewportFallbackSummary)
    {
        EntityNodes = new ObservableCollection<string>(
            model?.Scene.RootEntities.Select(FormatEntity) ?? entityFallbackLines ?? []);
        InspectorLines = new ObservableCollection<string>(
            model?.Inspector.Components.SelectMany(component =>
                new[] { component.Type }.Concat(component.Properties.Select(property => $"  {property.Name}: {property.Value}")))
            ?? inspectorFallbackLines ?? []);
        AssetLines = new ObservableCollection<string>(
            model?.Assets.Assets.Select(asset => $"{asset.Kind}: {asset.DisplayName} ({asset.AssetId})") ?? []);
        ValidationLines = new ObservableCollection<string>(
            model?.Diagnostics.Issues.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.Message}")
            ?? validationFallbackLines
            ?? []);
        TransactionLines = new ObservableCollection<string>(
            model?.Transactions.Transactions.Select(transaction => $"{transaction.Name}: {transaction.ChangedResources.Count} changes") ?? []);
        ImportLines = new ObservableCollection<string>(
            model?.ImportQueue.Jobs.Select(job => $"{job.Status}: {job.SourcePath}") ?? []);
        ViewportTitle = model is null ? "Viewport" : $"{model.Scene.Name} Viewport";
        ViewportSummary = model is null
            ? viewportFallbackSummary ?? "Open a Rekall AGE project to begin."
            : $"Frame {model.Runtime.FrameIndex}, camera {model.Runtime.ActiveCameraName ?? "none"}, {model.Runtime.RenderableCount} renderables, {model.Runtime.Observations.Count} observations";
        ViewportCaptureTool = model?.Runtime.ViewportCaptureTool ?? string.Empty;
    }

    public static RekallAgeStudioViewModel ForLoadFailure(string logDirectory)
    {
        return new RekallAgeStudioViewModel(
            null,
            ["Project failed to load"],
            ["The project could not be loaded. Full exception details were written to the Studio log."],
            [$"error: STUDIO_WORKBENCH_LOAD_FAILED - Could not load project. See logs: {logDirectory}"],
            "Could not load the project. See the Studio log for details.");
    }

    public ObservableCollection<string> EntityNodes { get; }

    public ObservableCollection<string> InspectorLines { get; }

    public ObservableCollection<string> AssetLines { get; }

    public ObservableCollection<string> ValidationLines { get; }

    public ObservableCollection<string> TransactionLines { get; }

    public ObservableCollection<string> ImportLines { get; }

    public string ViewportTitle { get; }

    public string ViewportSummary { get; }

    public string ViewportCaptureTool { get; }

    private static string FormatEntity(RekallAgeSceneEntityNode node)
    {
        return node.Children.Count == 0
            ? node.Name
            : $"{node.Name} ({node.Children.Count})";
    }
}
