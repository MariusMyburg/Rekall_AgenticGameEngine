using Rekall.Age.Rendering.Recovery;
using Rekall.Age.Core.Diagnostics;

namespace Rekall.Age.Tests.Rendering
{

public sealed class PlayerSessionSupervisorTests
{
    [Fact]
    public void ClassifierRecognizesOnlyTypedOrNarrowGraphicsLifecycleFailures()
    {
        var classifier = new RekallAgeGraphicsFailureClassifier();

        Assert.Equal(
            RekallAgeGraphicsFailureKinds.DeviceLost,
            classifier.Classify(new RekallAgeGraphicsDeviceLostException("lost")).Kind);
        Assert.Equal(
            RekallAgeGraphicsFailureKinds.SwapchainInvalid,
            classifier.Classify(new RekallAgeGraphicsDeviceLostException(
                "invalid",
                RekallAgeGraphicsFailureKinds.SwapchainInvalid)).Kind);
        Assert.Equal(
            RekallAgeGraphicsFailureKinds.DeviceLost,
            classifier.Classify(new Veldrid.VeldridException("vkQueuePresentKHR failed: VK_ERROR_DEVICE_LOST")).Kind);
        Assert.Equal(
            RekallAgeGraphicsFailureKinds.SwapchainInvalid,
            classifier.Classify(new Veldrid.VeldridException("vkAcquireNextImageKHR failed: VK_ERROR_OUT_OF_DATE_KHR")).Kind);

        Assert.False(classifier.Classify(new InvalidOperationException("VK_ERROR_DEVICE_LOST")).IsRecoverable);
        Assert.False(classifier.Classify(new InvalidOperationException(
            "outer",
            new InvalidOperationException("device lost"))).IsRecoverable);
        Assert.False(classifier.Classify(new Exception("module trust failed: VK_ERROR_DEVICE_LOST")).IsRecoverable);
    }

    [Fact]
    public async Task RecoverableFailureDisposesThenRecreatesAndPreservesFiniteFrameAccounting()
    {
        var events = new List<string>();
        var evidence = new RecordingEvidenceWriter();
        var factory = new ScriptedFactory(events,
            new ScriptedSession(events, run: _ => throw new RekallAgePlayerSessionRunException(
                completedFrames: 3,
                new RekallAgeGraphicsDeviceLostException("injected"))),
            new ScriptedSession(events, run: requested => new RekallAgePlayerSessionRunResult(requested ?? 0, false)));
        var supervisor = CreateSupervisor(factory, evidence);

        var result = await supervisor.RunAsync(10, CancellationToken.None);

        Assert.Equal(RekallAgePlayerSessionOutcomes.Recovered, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(1, result.RecoveryCount);
        Assert.Equal(10, result.CompletedFrames);
        Assert.Equal(["create:1", "run:10", "dispose", "create:2", "run:7", "dispose"], events);
        var report = Assert.Single(evidence.Items);
        Assert.Equal(RekallAgePlayerSessionOutcomes.Recovered, report.Outcome);
        Assert.Equal(RekallAgePlayerRecoveryModes.ColdSessionRestart, report.RecoveryMode);
    }

    [Fact]
    public async Task ContinuousRunRetainsContinuousRequestAcrossRecovery()
    {
        var requested = new List<long?>();
        var factory = new ScriptedFactory([],
            new ScriptedSession([], value =>
            {
                requested.Add(value);
                throw new RekallAgePlayerSessionRunException(12, new RekallAgeGraphicsDeviceLostException("lost"));
            }),
            new ScriptedSession([], value =>
            {
                requested.Add(value);
                return new RekallAgePlayerSessionRunResult(8, true);
            }));

        var result = await CreateSupervisor(factory).RunAsync(null, CancellationToken.None);

        Assert.Equal([null, null], requested);
        Assert.Equal(20, result.CompletedFrames);
        Assert.Equal(RekallAgePlayerSessionOutcomes.Recovered, result.Outcome);
    }

    [Fact]
    public async Task RecoveryIsBoundedAndExhaustionIsReported()
    {
        var evidence = new RecordingEvidenceWriter();
        var sessions = Enumerable.Range(0, 3)
            .Select(_ => new ScriptedSession([], _ => throw new RekallAgePlayerSessionRunException(
                1,
                new RekallAgeGraphicsDeviceLostException("still lost"))))
            .ToArray();
        var supervisor = CreateSupervisor(new ScriptedFactory([], sessions), evidence, maximumRecoveryAttempts: 2);

        var result = await supervisor.RunAsync(10, CancellationToken.None);

        Assert.Equal(RekallAgePlayerSessionOutcomes.Exhausted, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(2, result.RecoveryCount);
        Assert.Equal(3, result.CompletedFrames);
        Assert.Equal("REKALL_PLAYER_GRAPHICS_RECOVERY_EXHAUSTED", result.Code);
        Assert.Equal(RekallAgePlayerSessionOutcomes.Exhausted, Assert.Single(evidence.Items).Outcome);
    }

    [Fact]
    public async Task FatalRunAndInitializationFailuresAreNeverRetried()
    {
        var fatalFactory = new ScriptedFactory([], new ScriptedSession([], _ =>
            throw new RekallAgePlayerSessionRunException(4, new InvalidOperationException("module failed"))));
        var initializationFactory = new ThrowingFactory(new RekallAgeGraphicsDeviceLostException("during init"));

        var fatal = await CreateSupervisor(fatalFactory).RunAsync(10, CancellationToken.None);
        var initialization = await CreateSupervisor(initializationFactory).RunAsync(10, CancellationToken.None);

        Assert.Equal(RekallAgePlayerSessionOutcomes.Fatal, fatal.Outcome);
        Assert.Equal("REKALL_PLAYER_RUNTIME_FATAL", fatal.Code);
        Assert.Equal(1, fatalFactory.CreateCount);
        Assert.Equal(RekallAgePlayerSessionOutcomes.Fatal, initialization.Outcome);
        Assert.Equal("REKALL_PLAYER_INITIALIZATION_FAILED", initialization.Code);
        Assert.Equal(1, initializationFactory.CreateCount);
    }

    [Fact]
    public async Task CancellationEscapesWithoutFailureEvidence()
    {
        var evidence = new RecordingEvidenceWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CreateSupervisor(new ScriptedFactory([]), evidence).RunAsync(10, cancellation.Token));

        Assert.Empty(evidence.Items);
    }

    [Fact]
    public async Task EvidenceWriterFailureDoesNotHideRecoveredOutcome()
    {
        var factory = new ScriptedFactory([], new ScriptedSession([], _ =>
            throw new RekallAgePlayerSessionRunException(2, new RekallAgeGraphicsDeviceLostException("lost"))),
            new ScriptedSession([], requested => new RekallAgePlayerSessionRunResult(requested ?? 0, false)));
        var supervisor = CreateSupervisor(factory, new ThrowingEvidenceWriter());

        var result = await supervisor.RunAsync(5, CancellationToken.None);

        Assert.Equal(RekallAgePlayerSessionOutcomes.Recovered, result.Outcome);
        Assert.Equal(5, result.CompletedFrames);
        Assert.Equal("REKALL_PLAYER_FAILURE_REPORT_WRITE_FAILED", Assert.Single(result.EvidenceIssues));
    }

    [Fact]
    public async Task ProductionEvidenceWriterPersistsBoundedRecoveryReportAndReturnsPath()
    {
        var root = TestPaths.CreateTempDirectory();
        var writer = new RekallAgePlayerFailureReportWriter(
            new RekallAgeFailureReportStore(root),
            new RekallAgePlayerFailureReportContext("player.windows", "vulkan", "F:/Game", "Main"));
        var factory = new ScriptedFactory([], new ScriptedSession([], _ =>
            throw new RekallAgePlayerSessionRunException(2, new RekallAgeGraphicsDeviceLostException("lost"))),
            new ScriptedSession([], requested => new RekallAgePlayerSessionRunResult(requested ?? 0, false)));

        var result = await CreateSupervisor(factory, writer).RunAsync(5, CancellationToken.None);
        var inspection = await new RekallAgeFailureReportStore(root).ReadAsync(CancellationToken.None);

        Assert.True(File.Exists(Assert.Single(result.EvidencePaths)));
        var report = Assert.Single(inspection.Reports);
        Assert.Equal("player.windows", report.Component);
        Assert.Equal(RekallAgePlayerSessionOutcomes.Recovered, report.Outcome);
        Assert.Equal(RekallAgePlayerRecoveryModes.ColdSessionRestart, report.RecoveryMode);
        Assert.Equal(5, report.CompletedFrames);
        Assert.Contains(report.Limitations, value => value.Contains("not preserved", StringComparison.Ordinal));
    }

    private static RekallAgePlayerSessionSupervisor CreateSupervisor(
        IRekallAgePlayerSessionFactory factory,
        IRekallAgePlayerSessionEvidenceWriter? evidenceWriter = null,
        int maximumRecoveryAttempts = 2) =>
        new(
            factory,
            new RekallAgeGraphicsFailureClassifier(),
            evidenceWriter,
            new RekallAgePlayerSessionSupervisorOptions(maximumRecoveryAttempts, TimeSpan.Zero));

    private sealed class ScriptedFactory : IRekallAgePlayerSessionFactory
    {
        private readonly Queue<IRekallAgePlayerSession> _sessions;
        private readonly List<string> _events;

        public ScriptedFactory(List<string> events, params IRekallAgePlayerSession[] sessions)
        {
            _events = events;
            _sessions = new Queue<IRekallAgePlayerSession>(sessions);
        }

        public int CreateCount { get; private set; }

        public ValueTask<IRekallAgePlayerSession> CreateAsync(int attempt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            _events.Add($"create:{attempt}");
            return ValueTask.FromResult(_sessions.Dequeue());
        }
    }

    private sealed class ThrowingFactory(Exception exception) : IRekallAgePlayerSessionFactory
    {
        public int CreateCount { get; private set; }

        public ValueTask<IRekallAgePlayerSession> CreateAsync(int attempt, CancellationToken cancellationToken)
        {
            CreateCount++;
            return ValueTask.FromException<IRekallAgePlayerSession>(exception);
        }
    }

    private sealed class ScriptedSession(
        List<string> events,
        Func<long?, RekallAgePlayerSessionRunResult> run) : IRekallAgePlayerSession
    {
        public ValueTask<RekallAgePlayerSessionRunResult> RunAsync(long? requestedFrames, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add($"run:{requestedFrames?.ToString() ?? "continuous"}");
            return ValueTask.FromResult(run(requestedFrames));
        }

        public ValueTask DisposeAsync()
        {
            events.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingEvidenceWriter : IRekallAgePlayerSessionEvidenceWriter
    {
        public List<RekallAgePlayerSessionEvidence> Items { get; } = [];

        public ValueTask<string?> WriteAsync(RekallAgePlayerSessionEvidence evidence, CancellationToken cancellationToken)
        {
            Items.Add(evidence);
            return ValueTask.FromResult<string?>(null);
        }
    }

    private sealed class ThrowingEvidenceWriter : IRekallAgePlayerSessionEvidenceWriter
    {
        public ValueTask<string?> WriteAsync(RekallAgePlayerSessionEvidence evidence, CancellationToken cancellationToken) =>
            ValueTask.FromException<string?>(new IOException("disk unavailable"));
    }
}
}

namespace Veldrid
{
    internal sealed class VeldridException(string message) : Exception(message);
}
