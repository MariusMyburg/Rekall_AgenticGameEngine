namespace Rekall.Age.Rendering.Recovery;

public static class RekallAgePlayerSessionOutcomes
{
    public const string Completed = "completed";
    public const string Recovered = "recovered";
    public const string Exhausted = "exhausted";
    public const string Fatal = "fatal";
}

public static class RekallAgePlayerRecoveryModes
{
    public const string None = "none";
    public const string ColdSessionRestart = "cold-session-restart";
}

public sealed record RekallAgePlayerSessionRunResult(long CompletedFrames, bool UserRequestedExit);

public sealed class RekallAgePlayerSessionRunException : Exception
{
    public RekallAgePlayerSessionRunException(long completedFrames, Exception innerException)
        : base("The player session failed after completing part of its run.", innerException)
    {
        if (completedFrames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedFrames));
        }

        CompletedFrames = completedFrames;
    }

    public long CompletedFrames { get; }
}

public interface IRekallAgePlayerSession : IAsyncDisposable
{
    ValueTask<RekallAgePlayerSessionRunResult> RunAsync(
        long? requestedFrames,
        CancellationToken cancellationToken);
}

public interface IRekallAgePlayerSessionFactory
{
    ValueTask<IRekallAgePlayerSession> CreateAsync(int attempt, CancellationToken cancellationToken);
}

public sealed record RekallAgePlayerSessionEvidence(
    string Outcome,
    string Code,
    string Category,
    string RecoveryMode,
    int Attempts,
    int RecoveryCount,
    long CompletedFrames,
    long? RequestedFrames,
    Exception Exception);

public interface IRekallAgePlayerSessionEvidenceWriter
{
    ValueTask<string?> WriteAsync(RekallAgePlayerSessionEvidence evidence, CancellationToken cancellationToken);
}

public sealed record RekallAgePlayerSessionSupervisorOptions(
    int MaximumRecoveryAttempts = 2,
    TimeSpan RecoveryDelay = default);

public sealed record RekallAgePlayerSessionSupervisorResult(
    string Outcome,
    string Code,
    string RecoveryMode,
    int Attempts,
    int RecoveryCount,
    long CompletedFrames,
    long? RequestedFrames,
    RekallAgeGraphicsFailureClassification? LastFailure,
    IReadOnlyList<string> EvidenceIssues,
    IReadOnlyList<string> EvidencePaths)
{
    public bool Succeeded => Outcome is RekallAgePlayerSessionOutcomes.Completed or RekallAgePlayerSessionOutcomes.Recovered;
}

public sealed class RekallAgePlayerSessionSupervisor
{
    private readonly IRekallAgePlayerSessionFactory _factory;
    private readonly RekallAgeGraphicsFailureClassifier _classifier;
    private readonly IRekallAgePlayerSessionEvidenceWriter? _evidenceWriter;
    private readonly RekallAgePlayerSessionSupervisorOptions _options;

    public RekallAgePlayerSessionSupervisor(
        IRekallAgePlayerSessionFactory factory,
        RekallAgeGraphicsFailureClassifier classifier,
        IRekallAgePlayerSessionEvidenceWriter? evidenceWriter = null,
        RekallAgePlayerSessionSupervisorOptions? options = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _evidenceWriter = evidenceWriter;
        _options = options ?? new RekallAgePlayerSessionSupervisorOptions();
        if (_options.MaximumRecoveryAttempts < 0 || _options.RecoveryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    public async ValueTask<RekallAgePlayerSessionSupervisorResult> RunAsync(
        long? requestedFrames,
        CancellationToken cancellationToken = default)
    {
        if (requestedFrames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedFrames));
        }

        var completedFrames = 0L;
        var attempts = 0;
        var recoveryCount = 0;
        var evidenceIssues = new List<string>();
        var evidencePaths = new List<string>();
        RekallAgeGraphicsFailureClassification? lastRecoverableFailure = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            IRekallAgePlayerSession session;
            try
            {
                session = await _factory.CreateAsync(attempts, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var classification = new RekallAgeGraphicsFailureClassification(
                    RekallAgeGraphicsFailureKinds.Fatal,
                    "REKALL_PLAYER_INITIALIZATION_FAILED",
                    false,
                    exception);
                var result = Result(
                    RekallAgePlayerSessionOutcomes.Fatal,
                    classification.Code,
                    RekallAgePlayerRecoveryModes.None,
                    attempts,
                    recoveryCount,
                    completedFrames,
                    requestedFrames,
                    classification,
                    evidenceIssues,
                    evidencePaths);
                await TryWriteEvidenceAsync(result, classification, evidenceIssues, evidencePaths, cancellationToken).ConfigureAwait(false);
                return result with { EvidenceIssues = evidenceIssues.ToArray(), EvidencePaths = evidencePaths.ToArray() };
            }

            try
            {
                await using (session.ConfigureAwait(false))
                {
                    long? remainingFrames = requestedFrames is null
                        ? null
                        : Math.Max(0, requestedFrames.Value - completedFrames);
                    var run = await session.RunAsync(remainingFrames, cancellationToken).ConfigureAwait(false);
                    if (run.CompletedFrames < 0 || remainingFrames is not null && run.CompletedFrames > remainingFrames)
                    {
                        throw new InvalidOperationException("The player session returned invalid frame accounting.");
                    }

                    completedFrames += run.CompletedFrames;
                }

                var outcome = recoveryCount == 0
                    ? RekallAgePlayerSessionOutcomes.Completed
                    : RekallAgePlayerSessionOutcomes.Recovered;
                var result = Result(
                    outcome,
                    outcome == RekallAgePlayerSessionOutcomes.Completed
                        ? "REKALL_PLAYER_COMPLETED"
                        : "REKALL_PLAYER_GRAPHICS_RECOVERED",
                    recoveryCount == 0 ? RekallAgePlayerRecoveryModes.None : RekallAgePlayerRecoveryModes.ColdSessionRestart,
                    attempts,
                    recoveryCount,
                    completedFrames,
                    requestedFrames,
                    lastRecoverableFailure,
                    evidenceIssues,
                    evidencePaths);
                if (lastRecoverableFailure is not null)
                {
                    await TryWriteEvidenceAsync(result, lastRecoverableFailure, evidenceIssues, evidencePaths, cancellationToken)
                        .ConfigureAwait(false);
                }

                return result with { EvidenceIssues = evidenceIssues.ToArray(), EvidencePaths = evidencePaths.ToArray() };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var completedInFailedSession = exception is RekallAgePlayerSessionRunException partial
                    ? partial.CompletedFrames
                    : 0;
                Exception failure = exception is RekallAgePlayerSessionRunException { InnerException: not null } wrapped
                    ? wrapped.InnerException!
                    : exception;
                var remaining = requestedFrames is null ? long.MaxValue : requestedFrames.Value - completedFrames;
                completedFrames += Math.Min(completedInFailedSession, Math.Max(0, remaining));
                var classification = _classifier.Classify(failure);
                if (!classification.IsRecoverable)
                {
                    var result = Result(
                        RekallAgePlayerSessionOutcomes.Fatal,
                        classification.Code,
                        RekallAgePlayerRecoveryModes.None,
                        attempts,
                        recoveryCount,
                        completedFrames,
                        requestedFrames,
                        classification,
                        evidenceIssues,
                        evidencePaths);
                    await TryWriteEvidenceAsync(result, classification, evidenceIssues, evidencePaths, cancellationToken)
                        .ConfigureAwait(false);
                    return result with { EvidenceIssues = evidenceIssues.ToArray(), EvidencePaths = evidencePaths.ToArray() };
                }

                lastRecoverableFailure = classification;
                if (recoveryCount >= _options.MaximumRecoveryAttempts)
                {
                    var result = Result(
                        RekallAgePlayerSessionOutcomes.Exhausted,
                        "REKALL_PLAYER_GRAPHICS_RECOVERY_EXHAUSTED",
                        RekallAgePlayerRecoveryModes.ColdSessionRestart,
                        attempts,
                        recoveryCount,
                        completedFrames,
                        requestedFrames,
                        classification,
                        evidenceIssues,
                        evidencePaths);
                    await TryWriteEvidenceAsync(result, classification, evidenceIssues, evidencePaths, cancellationToken)
                        .ConfigureAwait(false);
                    return result with { EvidenceIssues = evidenceIssues.ToArray(), EvidencePaths = evidencePaths.ToArray() };
                }

                recoveryCount++;
                if (_options.RecoveryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_options.RecoveryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask TryWriteEvidenceAsync(
        RekallAgePlayerSessionSupervisorResult result,
        RekallAgeGraphicsFailureClassification classification,
        List<string> issues,
        List<string> paths,
        CancellationToken cancellationToken)
    {
        if (_evidenceWriter is null)
        {
            return;
        }

        try
        {
            var path = await _evidenceWriter.WriteAsync(
                new RekallAgePlayerSessionEvidence(
                    result.Outcome,
                    result.Code,
                    classification.Kind,
                    result.RecoveryMode,
                    result.Attempts,
                    result.RecoveryCount,
                    result.CompletedFrames,
                    result.RequestedFrames,
                    classification.Exception),
                cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            issues.Add("REKALL_PLAYER_FAILURE_REPORT_WRITE_FAILED");
        }
    }

    private static RekallAgePlayerSessionSupervisorResult Result(
        string outcome,
        string code,
        string recoveryMode,
        int attempts,
        int recoveryCount,
        long completedFrames,
        long? requestedFrames,
        RekallAgeGraphicsFailureClassification? failure,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> paths) =>
        new(
            outcome,
            code,
            recoveryMode,
            attempts,
            recoveryCount,
            completedFrames,
            requestedFrames,
            failure,
            issues,
            paths);
}
