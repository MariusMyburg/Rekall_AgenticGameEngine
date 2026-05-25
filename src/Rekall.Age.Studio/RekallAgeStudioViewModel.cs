using System.Collections.ObjectModel;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioViewModel
{
    public RekallAgeStudioViewModel(RekallAgeWorkbenchModel? model)
    {
        EntityNodes = new ObservableCollection<string>(
            model?.Scene.RootEntities.Select(FormatEntity) ?? ["No project loaded"]);
        InspectorLines = new ObservableCollection<string>(
            model?.Inspector.Components.SelectMany(component =>
                new[] { component.Type }.Concat(component.Properties.Select(property => $"  {property.Name}: {property.Value}")))
            ?? ["Select an entity"]);
        AssetLines = new ObservableCollection<string>(
            model?.Assets.Assets.Select(asset => $"{asset.Kind}: {asset.DisplayName} ({asset.AssetId})") ?? []);
        ValidationLines = new ObservableCollection<string>(
            model?.Diagnostics.Issues.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.Message}") ?? []);
        TransactionLines = new ObservableCollection<string>(
            model?.Transactions.Transactions.Select(transaction => $"{transaction.Name}: {transaction.ChangedResources.Count} changes") ?? []);
        ImportLines = new ObservableCollection<string>(
            model?.ImportQueue.Jobs.Select(job => $"{job.Status}: {job.SourcePath}") ?? []);
        ViewportTitle = model is null ? "Viewport" : $"{model.Scene.Name} Viewport";
        ViewportSummary = model is null
            ? "Open a Rekall AGE project to begin."
            : $"{model.Scene.RootEntities.Count} root entities, {model.Assets.Assets.Count} assets, {model.Diagnostics.Issues.Count} diagnostics";
    }

    public ObservableCollection<string> EntityNodes { get; }

    public ObservableCollection<string> InspectorLines { get; }

    public ObservableCollection<string> AssetLines { get; }

    public ObservableCollection<string> ValidationLines { get; }

    public ObservableCollection<string> TransactionLines { get; }

    public ObservableCollection<string> ImportLines { get; }

    public string ViewportTitle { get; }

    public string ViewportSummary { get; }

    private static string FormatEntity(RekallAgeSceneEntityNode node)
    {
        return node.Children.Count == 0
            ? node.Name
            : $"{node.Name} ({node.Children.Count})";
    }
}
