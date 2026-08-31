namespace Rekall.Age.Studio;

internal interface IRekallAgeStudioPreviewSession : IAsyncDisposable
{
    RekallAgeStudioViewportMetrics Metrics => default;

    bool IsDisposalComplete { get; }

    ValueTask<RekallAgeStudioPreviewFrame> ResetAsync(
        string projectRoot,
        string sceneName,
        int width,
        int height,
        CancellationToken cancellationToken);

    ValueTask<RekallAgeStudioPreviewFrame> StepAsync(
        int frameCount,
        CancellationToken cancellationToken);

    ValueTask<RekallAgeStudioPreviewFrame> PresentCurrentAsync(
        int width,
        int height,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<RekallAgeStudioPreviewFrame>(
            new InvalidOperationException("Studio preview must be reset before it can present."));

    ValueTask<RekallAgeStudioPreviewFrame?> RefreshExternalDependenciesAsync(
        int width,
        int height,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<RekallAgeStudioPreviewFrame?>(null);

    ValueTask ClearAsync(CancellationToken cancellationToken);

    ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    ValueTask InvalidateShadersAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    void SetRenderStyle(RekallAgeStudioViewportRenderStyle style) { }

    void SetEditorRenderables(IReadOnlyList<Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportRenderable> renderables) { }

    void SetSelectedEntity(string? entityId) { }
}

/// <summary>
/// Keeps non-windowed ViewModel consumers explicit: the normal World preview requires an injected
/// Vulkan host presenter and never creates a hidden bitmap renderer.
/// </summary>
internal sealed class RekallAgeStudioPreviewSession : IRekallAgeStudioPreviewSession
{
    private const string HostRequired =
        "REKALL_STUDIO_VULKAN_UNAVAILABLE: The Studio World viewport host is not attached.";
    private bool _disposalComplete;

    public bool IsDisposalComplete => _disposalComplete;

    public ValueTask<RekallAgeStudioPreviewFrame> ResetAsync(
        string projectRoot,
        string sceneName,
        int width,
        int height,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<RekallAgeStudioPreviewFrame>(new InvalidOperationException(HostRequired));

    public ValueTask<RekallAgeStudioPreviewFrame> StepAsync(
        int frameCount,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<RekallAgeStudioPreviewFrame>(new InvalidOperationException(HostRequired));

    public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _disposalComplete = true;
        return ValueTask.CompletedTask;
    }
}
