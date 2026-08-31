using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

[Collection(WpfApplicationTestCollection.Name)]
public sealed class StudioContentPreviewServiceTests
{
    [Fact]
    public void ProductionModelPreviewReusesTheModelingViewportRenderer()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "RekallAgeStudioContentPreviewService.cs"));
        Assert.Contains("RekallAgeStudioMeshViewportRenderer", source, StringComparison.Ordinal);
        Assert.Contains("RekallAgeStudioImportedModelPublisher.ToTopology", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawingVisual", source, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ImagesDecodeToFrozenThumbnailsAndCacheByIdentityRevision()
    {
        var decoder = new RecordingDecoder();
        var service = new RekallAgeStudioContentPreviewService(decoder, new RecordingModelPreview(), 4);
        var item = Item("texture", "rev-1", "image.png");

        var first = await service.GetAsync(item, CancellationToken.None);
        var second = await service.GetAsync(item, CancellationToken.None);

        Assert.Same(first, second);
        Assert.NotNull(first.Thumbnail);
        Assert.True(first.Thumbnail!.IsFrozen);
        Assert.Equal("Healthy", first.Health);
        Assert.Equal(1, decoder.CallCount);
    }

    [Fact]
    public async Task ModelsUseExistingPreviewAdapterAndUnsupportedKindsUseTypeFallback()
    {
        var models = new RecordingModelPreview();
        var service = new RekallAgeStudioContentPreviewService(new RecordingDecoder(), models, 4);

        var model = await service.GetAsync(Item("model", "r1", "ship.glb"), CancellationToken.None);
        var audio = await service.GetAsync(Item("audio", "r1", "theme.mp3", "audio"), CancellationToken.None);

        Assert.NotNull(model.Thumbnail);
        Assert.Equal(1, models.CallCount);
        Assert.Null(audio.Thumbnail);
        Assert.Equal("IconContentAudio", audio.IconKey);
        Assert.Equal("Healthy", audio.Health);
    }

    [Fact]
    public async Task ModelAdapterCancellationPropagatesWithoutProducingFallback()
    {
        var models = new RecordingModelPreview(cancel: true);
        var service = new RekallAgeStudioContentPreviewService(new RecordingDecoder(), models, 4);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.GetAsync(Item("model", "r1", "ship.glb"), cancellation.Token).AsTask());
        Assert.Equal(0, models.CallCount);
    }

    [Fact]
    public async Task PreviewFailureReturnsRedactedFallbackWithoutRemovingContent()
    {
        var service = new RekallAgeStudioContentPreviewService(
            new RecordingDecoder(new IOException("sentinel-private-path")), new RecordingModelPreview(), 4);
        var item = Item("texture", "r1", "broken.png");

        var result = await service.GetAsync(item, CancellationToken.None);

        Assert.Equal(item.Id, result.ContentId);
        Assert.Null(result.Thumbnail);
        Assert.Equal("Warning", result.Health);
        Assert.Equal("REKALL_CONTENT_PREVIEW_FAILED · Preview unavailable.", result.Summary);
        Assert.DoesNotContain("sentinel", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationPropagatesAndIsNotCachedAsFailure()
    {
        var decoder = new RecordingDecoder(cancel: true);
        var service = new RekallAgeStudioContentPreviewService(decoder, new RecordingModelPreview(), 4);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.GetAsync(Item("texture", "r1", "cancel.png"), cancellation.Token).AsTask());
        Assert.Equal(0, decoder.CallCount);
    }

    [Fact]
    public async Task RevisionChangesInvalidateAndLeastRecentlyUsedEntryIsEvicted()
    {
        var decoder = new RecordingDecoder();
        var service = new RekallAgeStudioContentPreviewService(decoder, new RecordingModelPreview(), 2);
        var a1 = Item("texture", "r1", "a.png", "a");
        var b = Item("texture", "r1", "b.png", "b");
        var c = Item("texture", "r1", "c.png", "c");

        await service.GetAsync(a1, CancellationToken.None);
        await service.GetAsync(b, CancellationToken.None);
        await service.GetAsync(a1, CancellationToken.None); // a is most recent
        await service.GetAsync(c, CancellationToken.None);  // b is evicted
        await service.GetAsync(b, CancellationToken.None);
        await service.GetAsync(a1 with { Revision = "r2" }, CancellationToken.None);

        Assert.Equal(5, decoder.CallCount);
        Assert.True(service.CachedCount <= 2);
    }

    [Fact]
    public async Task CardsLoadImageAndModelPreviewsWithoutSelectionAndIgnoreStaleRevisionCompletion()
    {
        var previews = new DeferredPreviewService();
        var image = new RekallAgeStudioContentCardModel(Item("texture", "r1", "image.png", "image"));
        var model = new RekallAgeStudioContentCardModel(Item("model", "r1", "ship.glb", "model"));

        var staleLoad = image.LoadPreviewAsync(previews, CancellationToken.None);
        image.Update(Item("texture", "r2", "image.png", "image"));
        previews.Complete("image", "r1");
        await staleLoad;
        Assert.Null(image.Thumbnail);

        var imageLoad = image.LoadPreviewAsync(previews, CancellationToken.None);
        var modelLoad = model.LoadPreviewAsync(previews, CancellationToken.None);
        previews.Complete("image", "r2");
        previews.Complete("model", "r1");
        await Task.WhenAll(imageLoad, modelLoad);

        Assert.NotNull(image.Thumbnail);
        Assert.NotNull(model.Thumbnail);
        Assert.Equal("Healthy", image.PreviewHealth);
        Assert.Equal("Healthy", model.PreviewHealth);
    }

    [Fact]
    public async Task UnloadedCardRealizationCancelsAndCannotPublishLateThumbnail()
    {
        var previews = new DeferredPreviewService();
        var card = new RekallAgeStudioContentCardModel(Item("texture", "r1", "image.png", "image"));
        using var realization = new CancellationTokenSource();

        var load = ContentBrowser.LoadRealizedPreviewAsync(
            token => card.LoadPreviewAsync(previews, token), realization.Token);
        realization.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        Assert.Null(card.Thumbnail);
    }

    [Fact]
    public async Task ConcurrentRequestsCoalesceByCompositeIdentityAndRespectGlobalBound()
    {
        var decoder = new BlockingDecoder();
        var service = new RekallAgeStudioContentPreviewService(decoder, new RecordingModelPreview(), 32);
        var same = Item("texture", "r1", "same.png", "same");
        var coalesced = Enumerable.Range(0, 6).Select(_ => service.GetAsync(same, CancellationToken.None).AsTask()).ToArray();
        await decoder.WaitForCallsAsync(1);
        Assert.Equal(1, decoder.CallCount);

        var burst = Enumerable.Range(0, 8)
            .Select(index => service.GetAsync(Item("texture", "r1", $"{index}.png", $"asset-{index}"), CancellationToken.None).AsTask())
            .ToArray();
        await decoder.WaitForCallsAsync(2);
        Assert.True(decoder.MaximumConcurrency <= 2);
        decoder.Release();
        await Task.WhenAll(coalesced.Concat(burst));
        Assert.True(decoder.MaximumConcurrency <= 2);
        Assert.Equal(2, RekallAgeStudioContentPreviewService.DefaultMaximumConcurrency);
    }

    [Fact]
    public async Task CancellingOneCoalescedCallerDoesNotCancelSharedPreview()
    {
        var decoder = new BlockingDecoder();
        var service = new RekallAgeStudioContentPreviewService(decoder, new RecordingModelPreview(), 4, 2);
        var item = Item("texture", "r1", "same.png", "same");
        using var cancellation = new CancellationTokenSource();
        var cancelled = service.GetAsync(item, cancellation.Token).AsTask();
        var survivor = service.GetAsync(item, CancellationToken.None).AsTask();
        await decoder.WaitForCallsAsync(1);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        decoder.Release();
        Assert.NotNull((await survivor).Thumbnail);
        Assert.Equal(1, decoder.CallCount);
    }

    [Fact]
    public async Task CompositeKindIdentityPreventsMeshAndModelAssetCollision()
    {
        var decoder = new RecordingDecoder();
        var models = new RecordingModelPreview();
        var service = new RekallAgeStudioContentPreviewService(decoder, models, 8);
        var mesh = Item("model", "r1", "same.glb", "shared") with { Kind = "mesh", Origin = "Authored" };
        var modelAsset = mesh with { Kind = "model-asset", Path = "same.model.json" };
        var cards = new[] { new RekallAgeStudioContentCardModel(mesh), new RekallAgeStudioContentCardModel(modelAsset) };

        Assert.Equal(2, cards.ToDictionary(card => card.Key).Count);
        await service.GetAsync(mesh, CancellationToken.None);
        await service.GetAsync(modelAsset, CancellationToken.None);
        Assert.Equal(2, models.CallCount);
    }

    [Fact]
    public async Task ImageDecoderRejectsOversizedEncodedFileBeforeDecode()
    {
        var path = Path.Combine(Path.GetTempPath(), "rekall-large-preview-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await using (var stream = File.Create(path)) stream.SetLength(RekallAgeStudioContentImageDecoder.MaximumEncodedBytes + 1);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new RekallAgeStudioContentImageDecoder().DecodeAsync(path, 192, CancellationToken.None).AsTask());
        }
        finally { File.Delete(path); }
    }

    private static RekallAgeContentBrowserItem Item(string family, string revision, string path, string id = "asset") =>
        new(id, id, family, family, "Imported", path, path, revision, "external", [], "Healthy", null, new());

    private sealed class RecordingDecoder(Exception? failure = null, bool cancel = false) : IRekallAgeStudioContentImageDecoder
    {
        public int CallCount { get; private set; }
        public ValueTask<ImageSource> DecodeAsync(string path, int maximumDimension, CancellationToken cancellationToken)
        {
            CallCount++;
            if (cancel) return ValueTask.FromCanceled<ImageSource>(cancellationToken);
            if (failure is not null) return ValueTask.FromException<ImageSource>(failure);
            var image = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
            image.Freeze();
            return ValueTask.FromResult<ImageSource>(image);
        }
    }

    private sealed class RecordingModelPreview(bool cancel = false) : IRekallAgeStudioContentModelPreviewAdapter
    {
        public int CallCount { get; private set; }
        public ValueTask<ImageSource> RenderAsync(RekallAgeContentBrowserItem item, int maximumDimension, CancellationToken cancellationToken)
        {
            CallCount++;
            if (cancel) return ValueTask.FromCanceled<ImageSource>(cancellationToken);
            var image = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
            image.Freeze();
            return ValueTask.FromResult<ImageSource>(image);
        }
    }

    private sealed class DeferredPreviewService : IRekallAgeStudioContentPreviewService
    {
        private readonly Dictionary<(string, string), TaskCompletionSource<RekallAgeStudioContentPreview>> _pending = [];
        public ValueTask<RekallAgeStudioContentPreview> GetAsync(RekallAgeContentBrowserItem item, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<RekallAgeStudioContentPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[(item.Id, item.Revision!)] = completion;
            return new(completion.Task.WaitAsync(cancellationToken));
        }

        public void Complete(string id, string revision)
        {
            var image = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null); image.Freeze();
            _pending[(id, revision)].SetResult(new(id, revision, image, "IconContentFile", "Healthy", null));
        }
    }

    private sealed class BlockingDecoder : IRekallAgeStudioContentImageDecoder
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        private int _active;
        public int CallCount => Volatile.Read(ref _calls);
        public int MaximumConcurrency { get; private set; }
        public async ValueTask<ImageSource> DecodeAsync(string path, int maximumDimension, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrency = Math.Max(MaximumConcurrency, active);
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                var image = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null); image.Freeze();
                return image;
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public async Task WaitForCallsAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (CallCount < count) await Task.Delay(10, timeout.Token);
        }
        public void Release() => _release.TrySetResult();
    }
}
