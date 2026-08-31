using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Rendering;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioContentCardModel : INotifyPropertyChanged
{
    private RekallAgeContentBrowserItem _item;
    private ImageSource? _thumbnail;
    private string _previewHealth;
    private string? _previewSummary;
    private long _generation;

    internal RekallAgeStudioContentCardModel(RekallAgeContentBrowserItem item)
    {
        _item = item;
        _previewHealth = item.Health;
    }

    internal RekallAgeContentBrowserItem Item => _item;
    internal RekallAgeStudioContentKey Key => RekallAgeStudioContentKey.From(_item);
    public string Id => _item.Id;
    public string DisplayName => _item.DisplayName;
    public string Family => _item.Family;
    public string Kind => _item.Kind;
    public string Origin => _item.Origin;
    public string? Path => _item.Path;
    public string Health => _item.Health;
    public string? Diagnostic => _item.Diagnostic;
    public ImageSource? Thumbnail { get => _thumbnail; private set => Set(ref _thumbnail, value); }
    public string PreviewHealth { get => _previewHealth; private set => Set(ref _previewHealth, value); }
    public string? PreviewSummary { get => _previewSummary; private set => Set(ref _previewSummary, value); }

    internal void Update(RekallAgeContentBrowserItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (RekallAgeStudioContentKey.From(item) != Key)
            throw new ArgumentException("A content card identity cannot change.", nameof(item));
        var revisionChanged = !string.Equals(item.Revision, _item.Revision, StringComparison.Ordinal);
        _item = item;
        Interlocked.Increment(ref _generation);
        if (revisionChanged)
        {
            Thumbnail = null;
            PreviewHealth = item.Health;
            PreviewSummary = null;
        }
        OnPropertyChanged(string.Empty);
    }

    internal async Task LoadPreviewAsync(
        IRekallAgeStudioContentPreviewService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        var item = _item;
        var generation = Volatile.Read(ref _generation);
        var preview = await service.GetAsync(item, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != Volatile.Read(ref _generation)
            || !string.Equals(item.Revision, _item.Revision, StringComparison.Ordinal)) return;
        Thumbnail = preview.Thumbnail;
        PreviewHealth = preview.Health;
        PreviewSummary = preview.Summary;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new(name));
}

internal readonly record struct RekallAgeStudioContentKey(string Id, string Kind, string Origin)
{
    internal static RekallAgeStudioContentKey From(RekallAgeContentBrowserItem item) =>
        new(item.Id, item.Kind.ToLowerInvariant(), item.Origin.ToLowerInvariant());
}

public sealed record RekallAgeStudioContentPreview(
    string ContentId,
    string Revision,
    ImageSource? Thumbnail,
    string IconKey,
    string Health,
    string? Summary);

internal interface IRekallAgeStudioContentPreviewService
{
    ValueTask<RekallAgeStudioContentPreview> GetAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken);
}

internal interface IRekallAgeStudioContentImageDecoder
{
    ValueTask<ImageSource> DecodeAsync(string path, int maximumDimension, CancellationToken cancellationToken);
}

internal interface IRekallAgeStudioContentModelPreviewAdapter
{
    ValueTask<ImageSource> RenderAsync(
        RekallAgeContentBrowserItem item, int maximumDimension, CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioContentPreviewService : IRekallAgeStudioContentPreviewService
{
    private const int ThumbnailDimension = 192;
    private readonly IRekallAgeStudioContentImageDecoder _decoder;
    private readonly IRekallAgeStudioContentModelPreviewAdapter _models;
    private readonly int _capacity;
    private readonly object _sync = new();
    private readonly Dictionary<PreviewKey, CacheEntry> _cache = [];
    private readonly LinkedList<PreviewKey> _usage = [];
    private readonly Dictionary<PreviewKey, Task<RekallAgeStudioContentPreview>> _inflight = [];
    private readonly SemaphoreSlim _workGate;

    public RekallAgeStudioContentPreviewService(
        IRekallAgeStudioContentImageDecoder decoder,
        IRekallAgeStudioContentModelPreviewAdapter models,
        int capacity = 96,
        int maximumConcurrency = 3)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
        _workGate = new(maximumConcurrency > 0 ? maximumConcurrency : throw new ArgumentOutOfRangeException(nameof(maximumConcurrency)));
    }

    public static RekallAgeStudioContentPreviewService CreateDefault() =>
        new(new RekallAgeStudioContentImageDecoder(), new RekallAgeStudioImportedModelPreviewAdapter());

    internal int CachedCount { get { lock (_sync) return _cache.Count; } }

    public async ValueTask<RekallAgeStudioContentPreview> GetAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        var revision = item.Revision ?? string.Empty;
        var identity = RekallAgeStudioContentKey.From(item);
        var key = new PreviewKey(identity.Id, identity.Kind, identity.Origin, revision);
        Task<RekallAgeStudioContentPreview> work;
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                _usage.Remove(cached.Node);
                _usage.AddFirst(cached.Node);
                return cached.Preview;
            }
            if (!_inflight.TryGetValue(key, out work!))
            {
                work = GenerateAsync(item, key);
                _inflight[key] = work;
            }
        }

        return await work.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<RekallAgeStudioContentPreview> GenerateAsync(
        RekallAgeContentBrowserItem item, PreviewKey key)
    {
        // Ensure the task is registered in _inflight before even a synchronous adapter can complete.
        await Task.Yield();
        await _workGate.WaitAsync().ConfigureAwait(false);
        try
        {
            RekallAgeStudioContentPreview preview;
            try
            {
                ImageSource? image = item.Family.ToLowerInvariant() switch
                {
                    "texture" or "image" when !string.IsNullOrWhiteSpace(item.Path) =>
                        await _decoder.DecodeAsync(item.Path, ThumbnailDimension, CancellationToken.None),
                    "model" or "mesh" when !string.IsNullOrWhiteSpace(item.Path) =>
                        await _models.RenderAsync(item, ThumbnailDimension, CancellationToken.None),
                    _ => null
                };
                if (image is { CanFreeze: true, IsFrozen: false }) image.Freeze();
                preview = new(item.Id, key.Revision, image, IconFor(item.Family), item.Health, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or NotSupportedException or ArgumentException)
            {
                preview = new(item.Id, key.Revision, null, IconFor(item.Family), "Warning",
                    "REKALL_CONTENT_PREVIEW_FAILED · Preview unavailable.");
            }

            lock (_sync)
            {
                if (_cache.TryGetValue(key, out var existing)) return existing.Preview;
                var node = _usage.AddFirst(key);
                _cache[key] = new(preview, node);
                while (_cache.Count > _capacity)
                {
                    var last = _usage.Last!;
                    _usage.RemoveLast();
                    _cache.Remove(last.Value);
                }
            }
            return preview;
        }
        finally
        {
            lock (_sync) _inflight.Remove(key);
            _workGate.Release();
        }
    }

    private static string IconFor(string family) => family.ToLowerInvariant() switch
    {
        "model" or "mesh" => "IconContentModel",
        "texture" or "image" => "IconContentImage",
        "audio" => "IconContentAudio",
        "shader" => "IconContentShader",
        "module" or "module-source" => "IconContentCode",
        _ => "IconContentFile"
    };

    private sealed record CacheEntry(
        RekallAgeStudioContentPreview Preview,
        LinkedListNode<PreviewKey> Node);

    private readonly record struct PreviewKey(string Id, string Kind, string Origin, string Revision);
}

internal sealed class RekallAgeStudioContentImageDecoder : IRekallAgeStudioContentImageDecoder
{
    internal const long MaximumEncodedBytes = 32 * 1024 * 1024;
    public async ValueTask<ImageSource> DecodeAsync(
        string path, int maximumDimension, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumEncodedBytes)
            throw new InvalidDataException("Encoded image exceeds the preview size limit.");
        return await Task.Run<ImageSource>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            if (metadata.PixelWidth >= metadata.PixelHeight) image.DecodePixelWidth = maximumDimension;
            else image.DecodePixelHeight = maximumDimension;
            image.EndInit();
            BitmapSource result = image;
            result.Freeze();
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }
}

// Uses the same GLB scene loader as the Vulkan viewport and projects its real geometry into a compact card preview.
internal sealed class RekallAgeStudioImportedModelPreviewAdapter : IRekallAgeStudioContentModelPreviewAdapter
{
    public async ValueTask<ImageSource> RenderAsync(
        RekallAgeContentBrowserItem item, int maximumDimension, CancellationToken cancellationToken)
    {
        var path = item.Path ?? throw new InvalidDataException("Model path is unavailable.");
        var meshes = await new RekallAgeGlbMeshLoader().LoadAsync(item.Id, path, cancellationToken).ConfigureAwait(false);
        if (meshes.Count == 0) throw new InvalidDataException("Model has no renderable geometry.");
        return await Task.Run<ImageSource>(() => Render(item, meshes, maximumDimension, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private static BitmapSource Render(
        RekallAgeContentBrowserItem item,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        int size,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var topology = RekallAgeStudioImportedModelPublisher.ToTopology(meshes);
        var asset = RekallAgeMeshAsset.Create(item.Id, item.DisplayName, topology);
        return new RekallAgeStudioMeshViewportRenderer().Render(
            asset, RekallAgeGeometryDomain.Face, [], size, size, preview: false,
            style: RekallAgeStudioViewportRenderStyle.SmoothShaded).Image;
    }
}
