using Rekall.Age.AssetPipeline;
using Rekall.Age.AssetPipeline.Commands;
using System.IO;
using System.Collections.Concurrent;
using Rekall.Age.Assets;
using Rekall.Age.Editor;
using Rekall.Age.Core.Commands;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioContentImportSessionTests
{
    [Fact]
    public void PolicyClassifiesAcceptedExtensionsCaseInsensitivelyAndRoutesCsExplicitly()
    {
        var policy = new RekallAgeStudioContentImportPolicy();

        Assert.Equal(["model", "texture", "audio"],
            policy.Classify(["ship.GLB", "albedo.PNG", "theme.MP3"]).Select(x => x.Kind));
        Assert.All(policy.Classify([
            "ship.gltf", "albedo.jpg", "normal.JPEG", "surface.dds", "surface.KTX2", "voice.wav",
            "common.glsl", "vertex.vert", "pixel.frag", "compute.comp", "lighting.HLSL"
        ]), item => Assert.True(item.Accepted, item.SourcePath));
        var source = Assert.Single(policy.Classify(["Behaviour.cs"]));
        Assert.Equal("REKALL_CONTENT_IMPORT_MODULE_ROUTE_REQUIRED", source.Code);
        Assert.False(source.Accepted);
    }

    [Fact]
    public async Task RejectsRelativeDirectoriesDuplicatesAndUnsupportedWithoutImportingThem()
    {
        using var fixture = new ImportFixture();
        var model = fixture.File("ship.glb");
        var importer = new FakeImporter();
        var session = fixture.Session(importer);

        var jobs = await session.ImportAsync(fixture.Root,
            ["relative.png", fixture.Root, model, model.ToUpperInvariant(), fixture.File("notes.txt")],
            CancellationToken.None);

        Assert.Single(importer.Requests);
        Assert.Contains(jobs, x => x.Code == "REKALL_CONTENT_IMPORT_ABSOLUTE_PATH_REQUIRED");
        Assert.Contains(jobs, x => x.Code == "REKALL_CONTENT_IMPORT_DIRECTORY_UNSUPPORTED");
        Assert.Contains(jobs, x => x.Code == "REKALL_CONTENT_IMPORT_DUPLICATE");
        Assert.Contains(jobs, x => x.Code == "REKALL_CONTENT_IMPORT_UNSUPPORTED");
    }

    [Fact]
    public async Task ImportsPartialBatchWithCanonicalPerFileReportsAndOneRefreshAndInvalidation()
    {
        using var fixture = new ImportFixture();
        var paths = new[] { fixture.File("ship.glb"), fixture.File("albedo.png"), fixture.File("theme.mp3"), fixture.File("bad.wav"), fixture.File("readme.xyz") };
        var importer = new FakeImporter(failName: "bad.wav");
        var session = fixture.Session(importer);

        var jobs = await session.ImportAsync(fixture.Root, paths, CancellationToken.None);

        Assert.Equal(4, importer.Requests.Count);
        Assert.All(importer.Requests, x => Assert.Equal("rekall.asset.import_report", x.Command));
        Assert.Equal(1, fixture.RefreshCount);
        Assert.Equal(1, fixture.InvalidationCount);
        Assert.Equal(paths, jobs.Select(x => x.SourcePath));
        Assert.Equal(3, jobs.Count(x => x.Status == "Succeeded"));
        Assert.Contains(jobs, x => x.Status == "Failed" && x.SourcePath.EndsWith("bad.wav"));
        Assert.Contains(jobs, x => x.Code == "REKALL_CONTENT_IMPORT_UNSUPPORTED");
    }

    [Fact]
    public async Task BoundsConcurrencyToTwoAndKeepsStableJobs()
    {
        using var fixture = new ImportFixture();
        var importer = new FakeImporter(delay: TimeSpan.FromMilliseconds(30));
        var paths = Enumerable.Range(0, 5).Select(i => fixture.File($"texture-{i}.png")).ToArray();

        var jobs = await fixture.Session(importer).ImportAsync(fixture.Root, paths, CancellationToken.None);

        Assert.Equal(2, importer.MaximumConcurrency);
        Assert.Equal(paths, jobs.Select(x => x.SourcePath));
        Assert.All(jobs, x => Assert.Equal("Succeeded", x.Status));
    }

    [Fact]
    public async Task CancellationIsPreservedAndDoesNotRefresh()
    {
        using var fixture = new ImportFixture();
        using var cancellation = new CancellationTokenSource();
        var importer = new FakeImporter(waitForCancellation: true);
        var task = fixture.Session(importer).ImportAsync(fixture.Root, [fixture.File("one.png"), fixture.File("two.png")], cancellation.Token).AsTask();
        await importer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, fixture.RefreshCount);
        Assert.Equal(0, fixture.InvalidationCount);
    }

    [Fact]
    public async Task FailureSummaryRedactsExceptionAndPathDetails()
    {
        using var fixture = new ImportFixture();
        const string sentinel = "SECRET-SENTINEL";
        var session = fixture.Session(new FakeImporter(exception: new InvalidOperationException(sentinel)));

        var job = Assert.Single(await session.ImportAsync(fixture.Root, [fixture.File("secret.png")], CancellationToken.None));

        Assert.Equal("REKALL_CONTENT_IMPORT_FAILED", job.Code);
        Assert.DoesNotContain(sentinel, job.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, job.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionAdapterPersistsEverySuccessfulFileInOneCanonicalBatch()
    {
        using var fixture = new ImportFixture();
        var paths = Enumerable.Range(0, 5).Select(i => fixture.File($"audio-{i}.mp3", [(byte)i, 2, 3])).ToArray();
        var session = fixture.Session(new RekallAgeStudioAssetImportCommand());

        var jobs = await session.ImportAsync(fixture.Root, paths, CancellationToken.None);

        Assert.All(jobs, job => Assert.Equal("Succeeded", job.Status));
        var pipeline = await new RekallAgeAssetPipelineStore().LoadAsync(fixture.Root, CancellationToken.None);
        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(fixture.Root, CancellationToken.None);
        Assert.Equal(5, pipeline.Imported.Count);
        Assert.Equal(5, pipeline.Sources.Count);
        Assert.Equal(5, catalog.Assets.Count);
    }

    [Fact]
    public async Task PublishesAllObservableJobMutationsThroughDispatcherInStableOrder()
    {
        using var fixture = new ImportFixture();
        using var dispatcher = new DedicatedDispatcher();
        var paths = new[] { fixture.File("first.png"), fixture.File("second.png") };
        var session = fixture.Session(new FakeImporter(delay: TimeSpan.FromMilliseconds(10)), dispatcher);
        var observed = new List<(int Thread, string Path, string Status)>();
        session.Jobs.CollectionChanged += (_, args) =>
        {
            if (args.NewItems?[0] is RekallAgeStudioContentImportJob job)
                observed.Add((Environment.CurrentManagedThreadId, job.SourcePath, job.Status));
        };

        await Task.Run(async () => await session.ImportAsync(fixture.Root, paths, CancellationToken.None));

        Assert.NotEmpty(observed);
        Assert.All(observed, change => Assert.Equal(dispatcher.ThreadId, change.Thread));
        Assert.Equal(paths, session.Jobs.Select(job => job.SourcePath));
        Assert.All(session.Jobs, job => Assert.Equal("Succeeded", job.Status));
    }

    [Fact]
    public async Task OverlappingBatchIsRejectedWithoutResettingActiveJobs()
    {
        using var fixture = new ImportFixture();
        using var cancellation = new CancellationTokenSource();
        var importer = new FakeImporter(waitForCancellation: true);
        var session = fixture.Session(importer);
        var firstPath = fixture.File("first.png");
        var first = session.ImportAsync(fixture.Root, [firstPath], cancellation.Token).AsTask();
        await importer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var overlap = await session.ImportAsync(fixture.Root, [fixture.File("second.png")], CancellationToken.None);

        var rejected = Assert.Single(overlap);
        Assert.Equal("REKALL_CONTENT_IMPORT_ALREADY_ACTIVE", rejected.Code);
        Assert.Equal(firstPath, Assert.Single(session.Jobs).SourcePath);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }

    [Fact]
    public async Task ViewModelTreatsImportCancellationAsExpectedAndRetainsJobsWithoutValidationFailure()
    {
        using var fixture = new ImportFixture();
        var workbench = new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create());
        var created = await workbench.CreateProjectAsync(fixture.Root, "Import Test", "Main", [], [], "test", CancellationToken.None);
        Assert.True(created.Ok, created.Summary);
        using var cancellation = new CancellationTokenSource();
        var importer = new FakeImporter(waitForCancellation: true);
        var contentSession = fixture.Session(importer);
        await using var viewModel = new RekallAgeStudioViewModel(workbench, new RekallAgeStudioPreviewSession(), contentSession);
        var task = viewModel.ImportContentAsync([fixture.File("cancel.png")], cancellation.Token);
        await importer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        var jobs = await task;

        Assert.Equal("Cancelled", Assert.Single(jobs).Status);
        Assert.Contains("REKALL_CONTENT_IMPORT_CANCELLED", viewModel.ContentStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(viewModel.ValidationLines, line => line.Contains("REKALL_STUDIO_UNEXPECTED_FAILURE", StringComparison.Ordinal));
        Assert.False(viewModel.HasActiveContentImports);
    }

    private sealed class ImportFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "rekall-content-import-" + Guid.NewGuid().ToString("N"));
        public int RefreshCount { get; private set; }
        public int InvalidationCount { get; private set; }

        public ImportFixture() => Directory.CreateDirectory(Root);
        public string File(string name, byte[]? bytes = null) { var path = Path.Combine(Root, name); System.IO.File.WriteAllBytes(path, bytes ?? [1]); return path; }
        public RekallAgeStudioContentImportSession Session(IRekallAgeStudioAssetImportCommand importer,
            IRekallAgeStudioContentImportDispatcher? dispatcher = null) =>
            new(importer, _ => { RefreshCount++; return ValueTask.CompletedTask; }, _ => { InvalidationCount++; return ValueTask.CompletedTask; }, dispatcher: dispatcher);
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class FakeImporter(string? failName = null, TimeSpan? delay = null, bool waitForCancellation = false, Exception? exception = null)
        : IRekallAgeStudioAssetImportCommand
    {
        private int _concurrency;
        public List<(string Command, string Path, string Kind)> Requests { get; } = [];
        public int MaximumConcurrency { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ImportAssetWithReportResult> ImportAsync(string projectRoot, string sourcePath, string kind, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(("rekall.asset.import_report", sourcePath, kind));
            var current = Interlocked.Increment(ref _concurrency);
            MaximumConcurrency = Math.Max(MaximumConcurrency, current);
            Started.TrySetResult();
            try
            {
                if (waitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                if (delay is not null) await Task.Delay(delay.Value, cancellationToken);
                if (exception is not null) throw exception;
                if (Path.GetFileName(sourcePath).Equals(failName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("failed");
                var report = new RekallAgeAssetImportReport(true, "asset-" + Path.GetFileNameWithoutExtension(sourcePath), kind, sourcePath, "Assets/" + Path.GetFileName(sourcePath), []);
                return new ImportAssetWithReportResult(report, RekallAgeAssetPipelineDocument.Empty);
            }
            finally { Interlocked.Decrement(ref _concurrency); }
        }
    }

    private sealed class DedicatedDispatcher : IRekallAgeStudioContentImportDispatcher, IDisposable
    {
        private readonly BlockingCollection<(Action Action, TaskCompletionSource Completion)> _queue = [];
        private readonly Thread _thread;
        public int ThreadId { get; private set; }

        public DedicatedDispatcher()
        {
            _thread = new Thread(() =>
            {
                ThreadId = Environment.CurrentManagedThreadId;
                foreach (var work in _queue.GetConsumingEnumerable())
                {
                    try { work.Action(); work.Completion.SetResult(); }
                    catch (Exception exception) { work.Completion.SetException(exception); }
                }
            });
            _thread.Start();
            while (ThreadId == 0) Thread.Yield();
        }

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add((action, completion), cancellationToken);
            return new(completion.Task);
        }

        public void Dispose() { _queue.CompleteAdding(); _thread.Join(); _queue.Dispose(); }
    }
}
