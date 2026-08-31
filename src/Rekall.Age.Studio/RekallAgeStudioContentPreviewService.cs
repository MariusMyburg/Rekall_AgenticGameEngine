using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Rendering;

namespace Rekall.Age.Studio;

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
    private readonly Dictionary<(string Id, string Revision), CacheEntry> _cache = [];
    private readonly LinkedList<(string Id, string Revision)> _usage = [];

    public RekallAgeStudioContentPreviewService(
        IRekallAgeStudioContentImageDecoder decoder,
        IRekallAgeStudioContentModelPreviewAdapter models,
        int capacity = 96)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
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
        var key = (item.Id, revision);
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                _usage.Remove(cached.Node);
                _usage.AddFirst(cached.Node);
                return cached.Preview;
            }
        }

        RekallAgeStudioContentPreview preview;
        try
        {
            ImageSource? image = item.Family.ToLowerInvariant() switch
            {
                "texture" or "image" when !string.IsNullOrWhiteSpace(item.Path) =>
                    await _decoder.DecodeAsync(item.Path, ThumbnailDimension, cancellationToken),
                "model" or "mesh" when !string.IsNullOrWhiteSpace(item.Path) =>
                    await _models.RenderAsync(item, ThumbnailDimension, cancellationToken),
                _ => null
            };
            cancellationToken.ThrowIfCancellationRequested();
            if (image is { CanFreeze: true, IsFrozen: false }) image.Freeze();
            preview = new(item.Id, revision, image, IconFor(item.Family), item.Health, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or NotSupportedException or ArgumentException)
        {
            preview = new(item.Id, revision, null, IconFor(item.Family), "Warning",
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
        LinkedListNode<(string Id, string Revision)> Node);
}

internal sealed class RekallAgeStudioContentImageDecoder : IRekallAgeStudioContentImageDecoder
{
    public async ValueTask<ImageSource> DecodeAsync(
        string path, int maximumDimension, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return await Task.Run<ImageSource>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var source = decoder.Frames[0];
            var largest = Math.Max(source.PixelWidth, source.PixelHeight);
            BitmapSource result = largest > maximumDimension
                ? new TransformedBitmap(source, new ScaleTransform(
                    maximumDimension / (double)largest, maximumDimension / (double)largest))
                : source;
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
        return await Task.Run<ImageSource>(() => Render(meshes, maximumDimension, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private static BitmapSource Render(
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes, int size, CancellationToken cancellationToken)
    {
        var points = meshes.SelectMany(mesh => mesh.Vertices)
            .Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z)).ToArray();
        if (points.Length == 0) throw new InvalidDataException("Model has no vertices.");
        var minX = points.Min(point => point.X - point.Z * .35f);
        var maxX = points.Max(point => point.X - point.Z * .35f);
        var minY = points.Min(point => -point.Y + point.Z * .2f);
        var maxY = points.Max(point => -point.Y + point.Z * .2f);
        var span = Math.Max(Math.Max(maxX - minX, maxY - minY), .001f);
        Point Project(Vector3 point) => new(
            12 + ((point.X - point.Z * .35f - minX) / span) * (size - 24),
            12 + ((-point.Y + point.Z * .2f - minY) / span) * (size - 24));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 22, 29)), null, new Rect(0, 0, size, size));
            var fill = new SolidColorBrush(Color.FromRgb(72, 171, 206));
            var edge = new Pen(new SolidColorBrush(Color.FromRgb(151, 220, 239)), .7);
            foreach (var mesh in meshes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
                {
                    var geometry = new StreamGeometry();
                    using (var stream = geometry.Open())
                    {
                        var a = Project(new(mesh.Vertices[(int)mesh.Indices[i]].X, mesh.Vertices[(int)mesh.Indices[i]].Y, mesh.Vertices[(int)mesh.Indices[i]].Z));
                        var b = Project(new(mesh.Vertices[(int)mesh.Indices[i + 1]].X, mesh.Vertices[(int)mesh.Indices[i + 1]].Y, mesh.Vertices[(int)mesh.Indices[i + 1]].Z));
                        var c = Project(new(mesh.Vertices[(int)mesh.Indices[i + 2]].X, mesh.Vertices[(int)mesh.Indices[i + 2]].Y, mesh.Vertices[(int)mesh.Indices[i + 2]].Z));
                        stream.BeginFigure(a, true, true); stream.LineTo(b, true, false); stream.LineTo(c, true, false);
                    }
                    geometry.Freeze();
                    context.DrawGeometry(fill, edge, geometry);
                }
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }
}
