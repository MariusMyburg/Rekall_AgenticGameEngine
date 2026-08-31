using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioContentOpenResult(
    bool Opened,
    string Code,
    string Summary,
    string? WorkspaceId = null,
    string? SurfaceId = null);

internal interface IRekallAgeStudioContentOpenRouter
{
    bool CanOpen(RekallAgeContentBrowserItem item);
    ValueTask<RekallAgeStudioContentOpenResult> OpenAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken);
}

internal interface IRekallAgeStudioContentOpenTarget
{
    ValueTask SelectMeshAsync(RekallAgeContentBrowserItem item, CancellationToken cancellationToken);
    ValueTask SelectGraphAsync(RekallAgeContentBrowserItem item, CancellationToken cancellationToken);
    ValueTask SelectMaterialAsync(RekallAgeContentBrowserItem item, CancellationToken cancellationToken);
    ValueTask SelectModuleSourceAsync(RekallAgeContentBrowserItem item, CancellationToken cancellationToken);
    ValueTask OpenAssociatedAsync(RekallAgeContentBrowserItem item, CancellationToken cancellationToken);
}

internal interface IRekallAgeStudioExternalContentLauncher
{
    ValueTask OpenAsync(string path, CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioShellExternalContentLauncher : IRekallAgeStudioExternalContentLauncher
{
    public ValueTask OpenAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return ValueTask.CompletedTask;
    }
}

internal sealed class RekallAgeStudioValidatingExternalContentLauncher(Action<string> record)
    : IRekallAgeStudioExternalContentLauncher
{
    public ValueTask OpenAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(fullPath) || !File.Exists(fullPath))
            throw new FileNotFoundException("External content path is unavailable.");
        record(fullPath);
        return ValueTask.CompletedTask;
    }
}

internal sealed class RekallAgeStudioContentOpenRouter(IRekallAgeStudioContentOpenTarget target)
    : IRekallAgeStudioContentOpenRouter
{
    private readonly IRekallAgeStudioContentOpenTarget _target = target
        ?? throw new ArgumentNullException(nameof(target));

    public async ValueTask<RekallAgeStudioContentOpenResult> OpenAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanOpen(item))
            return Unavailable("No editor is available for the selected content type.");
        if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
            return Unavailable("The selected content is no longer available. Refresh the Content Browser and try again.");

        try
        {
            return item.EditorRouteId switch
            {
                "mesh-edit" => await OpenAsync(_target.SelectMeshAsync, item, "modeling", "mesh-edit", cancellationToken),
                "modeling-graph" => await OpenAsync(_target.SelectGraphAsync, item, "modeling", "node-contracts", cancellationToken),
                "material-graph" => await OpenAsync(_target.SelectMaterialAsync, item, "modeling", "material-graph", cancellationToken),
                "material-instance" => await OpenAsync(_target.SelectMaterialAsync, item, "modeling", "material-graph", cancellationToken),
                "module-source" => await OpenAsync(_target.SelectModuleSourceAsync, item, "code", "source-edit", cancellationToken),
                "shader-edit" => await OpenAsync(_target.OpenAssociatedAsync, item, "external", "shader-source", cancellationToken),
                "texture-preview" or "audio-preview" or "external" => await OpenAsync(
                    _target.OpenAssociatedAsync, item, "external", "associated-application", cancellationToken),
                _ => Unavailable("No editor is available for the selected content type.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
            or ArgumentException or JsonException or Win32Exception)
        {
            return new(false, "REKALL_CONTENT_OPEN_FAILED", "The selected content could not be opened.");
        }
    }

    private static async ValueTask<RekallAgeStudioContentOpenResult> OpenAsync(
        Func<RekallAgeContentBrowserItem, CancellationToken, ValueTask> open,
        RekallAgeContentBrowserItem item,
        string workspace,
        string surface,
        CancellationToken cancellationToken)
    {
        await open(item, cancellationToken).ConfigureAwait(false);
        return new(true, "REKALL_CONTENT_OPENED", $"Opened {item.DisplayName}.", workspace, surface);
    }

    private static bool IsKnownRoute(string route) => route is
        "mesh-edit" or "modeling-graph" or "material-graph" or "material-instance"
        or "module-source" or "shader-edit" or "texture-preview" or "audio-preview" or "external";

    public bool CanOpen(RekallAgeContentBrowserItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!IsKnownRoute(item.EditorRouteId)) return false;
        var capability = item.EditorRouteId is "shader-edit" or "texture-preview" or "audio-preview" or "external"
            ? RekallAgeContentCapability.OpenExternal
            : RekallAgeContentCapability.Open;
        return item.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);
    }

    private static RekallAgeStudioContentOpenResult Unavailable(string summary) =>
        new(false, "REKALL_CONTENT_OPEN_UNAVAILABLE", summary);
}
