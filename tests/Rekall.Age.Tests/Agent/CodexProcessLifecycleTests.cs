using System.ComponentModel;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.Codex;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class CodexProcessLifecycleTests
{
    [Fact]
    public async Task MissingExecutableReturnsStableActionWithoutProviderExceptionText()
    {
        var factory = new FakeCodexProcessFactory(new Win32Exception("sensitive executable lookup detail"));

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            RekallAgeCodexAppServerClient.StartAsync(TestOptions(), factory));

        Assert.Equal(RekallAgeCodexErrorCodes.RuntimeMissing, error.Code);
        Assert.Equal("codex", error.ProviderId);
        Assert.Contains("Install or update Codex", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive executable", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncompatibleInitializeResponseReturnsStableUpdateActionAndCleansOwnedProcess()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        var start = RekallAgeCodexAppServerClient.StartAsync(
            TestOptions(),
            new FakeCodexProcessFactory(process),
            timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);

        await process.WriteServerLineAsync("""{"id":1,"result":{"userAgent":"old"}}""");
        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => start);

        Assert.Equal(RekallAgeCodexErrorCodes.ProtocolUnsupported, error.Code);
        Assert.Contains("Update Codex", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, process.InputCloseCount);
        Assert.Equal(1, process.KillCount);
        Assert.True(process.LastKillEntireProcessTree);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public async Task MalformedJsonDuringInitializeRemainsProtocolInvalid()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        var start = RekallAgeCodexAppServerClient.StartAsync(
            TestOptions(),
            new FakeCodexProcessFactory(process),
            timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);

        await process.WriteServerRawAsync("not-json\n");
        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => start);

        Assert.Equal(RekallAgeCodexErrorCodes.ProtocolInvalid, error.Code);
    }

    [Fact]
    public async Task MalformedJsonFailsEveryPendingRequestExactlyOnce()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        var account = client.ReadAccountAsync(cancellationToken: timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);
        var models = client.ListModelsAsync(cancellationToken: timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);

        await process.WriteServerRawAsync("not-json\n");

        var accountError = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => account);
        var modelError = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => models);
        Assert.Same(accountError, modelError);
        Assert.Equal(RekallAgeCodexErrorCodes.ProtocolInvalid, accountError.Code);
        Assert.Equal(0, client.PendingRequestCount);
    }

    [Fact]
    public async Task OversizedJsonlLineFailsPendingRequestWithoutRetainingPayload()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        var options = WithMaximumLine(TestOptions(), 256);
        await using var client = await StartInitializedClientAsync(process, options, timeout.Token);
        var account = client.ReadAccountAsync(cancellationToken: timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);

        await process.WriteServerRawAsync(new string('x', 257) + "\n");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => account);
        Assert.Equal(RekallAgeCodexErrorCodes.ProtocolInvalid, error.Code);
        Assert.DoesNotContain(new string('x', 32), error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedOutboundJsonlLineIsRejectedBeforeStdinChanges()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        var options = WithMaximumLine(TestOptions(), 256);
        await using var client = await StartInitializedClientAsync(process, options, timeout.Token);
        var baselineWrites = process.ClientLinesWritten;

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.StartTurnAsync("thread-1", new string('x', 300), cancellationToken: timeout.Token));

        Assert.Equal(RekallAgeCodexErrorCodes.ProtocolInvalid, error.Code);
        Assert.Equal(baselineWrites, process.ClientLinesWritten);
        Assert.DoesNotContain(new string('x', 32), error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrematureEofFailsPendingRequestWithProtocolCode()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        var account = client.ReadAccountAsync(cancellationToken: timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);

        process.CompleteStdout();

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => account);
        Assert.Equal(RekallAgeCodexErrorCodes.ProtocolInvalid, error.Code);
        Assert.Equal(0, client.PendingRequestCount);
    }

    [Fact]
    public async Task NonzeroOwnedProcessExitFailsPendingRequestWithoutStderrDisclosure()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        await process.WriteStderrAsync("Authorization: Bearer private-provider-material\n");
        var account = client.ReadAccountAsync(cancellationToken: timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);

        process.Exit(23);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => account);
        Assert.Equal(RekallAgeCodexErrorCodes.ProcessExited, error.Code);
        Assert.Equal("Exit code 23.", error.ProviderDetail);
        Assert.DoesNotContain("private-provider-material", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationBeforeTurnStartWritesNothingAndUsesStableCode()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        var baselineWrites = process.ClientLinesWritten;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.StartTurnAsync("thread-1", "task", cancellationToken: cancelled.Token));

        Assert.Equal(RekallAgeCodexErrorCodes.Cancelled, error.Code);
        Assert.Equal(baselineWrites, process.ClientLinesWritten);
    }

    [Fact]
    public async Task CancellationAfterTurnStartInterruptsTheExactTurn()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        var turn = await StartTurnAsync(client, process, timeout.Token);
        using var cancelled = new CancellationTokenSource();
        var completion = client.WaitForTurnCompletionAsync(turn, cancelled.Token);

        cancelled.Cancel();
        var interrupt = await process.ReadClientLineAsync(timeout.Token);
        AssertJson(interrupt, """{"id":3,"method":"turn/interrupt","params":{"threadId":"thread-1","turnId":"turn-1"}}""");
        await process.WriteServerLineAsync("""{"id":3,"result":{}}""");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => completion);
        Assert.Equal(RekallAgeCodexErrorCodes.Cancelled, error.Code);
    }

    [Fact]
    public async Task CancellationWhileTurnStartIsPendingInterruptsTurnWhenLateIdArrives()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        using var cancelled = new CancellationTokenSource();
        var startTurn = client.StartTurnAsync("thread-1", "task", cancellationToken: cancelled.Token);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":2,"method":"turn/start","params":{"threadId":"thread-1","input":[{"type":"text","text":"task"}]}}""");

        cancelled.Cancel();
        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => startTurn);
        Assert.Equal(RekallAgeCodexErrorCodes.Cancelled, error.Code);
        await process.WriteServerLineAsync(
            """{"id":2,"result":{"turn":{"id":"late-turn","status":"inProgress","items":[]}}}""");

        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":3,"method":"turn/interrupt","params":{"threadId":"thread-1","turnId":"late-turn"}}""");
        await process.WriteServerLineAsync("""{"id":3,"result":{}}""");
    }

    [Fact]
    public async Task TurnCompletedImmediatelyAfterStartResponseIsNotLost()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        var startTurn = client.StartTurnAsync("thread-1", "task", cancellationToken: timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);

        await process.WriteServerRawAsync(
            "{\"id\":2,\"result\":{\"turn\":{\"id\":\"turn-1\",\"status\":\"inProgress\",\"items\":[]}}}\n"
            + "{\"method\":\"turn/completed\",\"params\":{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\",\"items\":[]}}}\n");

        var turn = await startTurn;
        using var completionTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var completion = await client.WaitForTurnCompletionAsync(turn, completionTimeout.Token);
        Assert.Equal("completed", completion.Status);
    }

    [Fact]
    public async Task FailedTurnUsesStableCodeWithoutProviderControlledErrorPayload()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        var turn = await StartTurnAsync(client, process, timeout.Token);
        var completion = client.WaitForTurnCompletionAsync(turn, timeout.Token);

        await process.WriteServerLineAsync(
            """{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"failed","items":[],"error":{"message":"Authorization: Bearer private-provider-material"}}}}""");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => completion);
        Assert.Equal(RekallAgeCodexErrorCodes.TurnFailed, error.Code);
        Assert.DoesNotContain("private-provider-material", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThreadStartRefusesSilentModelFallbackWithStableFacts()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        var projectRoot = Path.GetFullPath("codex-model-fixture");
        var startThread = client.StartThreadAsync(
            new RekallAgeCodexThreadStartRequest(projectRoot, "requested-model", "Use AGE primitives."),
            timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);
        await process.WriteServerLineAsync(
            "{\"id\":2,\"result\":{\"thread\":{\"id\":\"thread-1\"},\"model\":\"fallback-model\",\"cwd\":"
            + JsonValue.Create(projectRoot)!.ToJsonString()
            + "}}");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => startThread);
        Assert.Equal(RekallAgeCodexErrorCodes.ModelUnavailable, error.Code);
        Assert.Equal("requested-model", error.RequestedValue);
        Assert.Equal("fallback-model", error.ResolvedValue);
    }

    [Fact]
    public async Task UnresponsiveShutdownInterruptsClosesWaitsThenKillsOnlyOwnedTree()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);
        var turn = await StartTurnAsync(client, process, timeout.Token);
        var completion = client.WaitForTurnCompletionAsync(turn, timeout.Token);

        var dispose = client.DisposeAsync().AsTask();
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":3,"method":"turn/interrupt","params":{"threadId":"thread-1","turnId":"turn-1"}}""");
        await dispose;

        var completionError = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() => completion);
        Assert.Equal(RekallAgeCodexErrorCodes.Cancelled, completionError.Code);
        Assert.Equal(1, process.InputCloseCount);
        Assert.Equal(1, process.KillCount);
        Assert.True(process.LastKillEntireProcessTree);
        Assert.Equal(1, process.DisposeCount);
        var events = process.LifecycleEvents.ToArray();
        Assert.True(Array.IndexOf(events, "close-stdin") < Array.IndexOf(events, "wait-for-exit"));
        Assert.True(Array.IndexOf(events, "wait-for-exit") < Array.IndexOf(events, "kill:True"));
        Assert.True(Array.IndexOf(events, "kill:True") < Array.IndexOf(events, "dispose-process"));
    }

    [Fact]
    public async Task StderrFloodIsFullyDrainedBoundedAndRedactedAcrossChunkBoundaries()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        var options = WithMaximumStderr(TestOptions(), 256);
        await using var client = await StartInitializedClientAsync(process, options, timeout.Token);
        var flood = new string('z', 8_192) + "\n";
        await process.WriteStderrAsync(flood);
        await process.WriteStderrAsync("account-name@example");
        await process.WriteStderrAsync(".invalid\nAuthorization: Bearer private-");
        await process.WriteStderrAsync("provider-material\n");
        var expectedRead = flood.Length
            + "account-name@example".Length
            + ".invalid\nAuthorization: Bearer private-".Length
            + "provider-material\n".Length;
        await WaitUntilAsync(() => client.StderrCharactersRead >= expectedRead, timeout.Token);

        Assert.InRange(client.RetainedStderrCharacterCount, 1, 256);
        Assert.DoesNotContain("account-name@example.invalid", client.SanitizedStderrSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-provider-material", client.SanitizedStderrSnapshot, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", client.SanitizedStderrSnapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentDisposalClosesKillsAndDisposesExactlyOnce()
    {
        using var timeout = TestTimeout();
        var process = new FakeCodexProcess();
        var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);

        var disposals = Enumerable.Range(0, 32)
            .Select(_ => client.DisposeAsync().AsTask())
            .ToArray();
        await Task.WhenAll(disposals);

        Assert.Equal(1, process.InputCloseCount);
        Assert.Equal(1, process.KillCount);
        Assert.Equal(1, process.DisposeCount);
    }

    private static async Task<RekallAgeCodexAppServerClient> StartInitializedClientAsync(
        FakeCodexProcess process,
        RekallAgeCodexAppServerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var start = RekallAgeCodexAppServerClient.StartAsync(
            options ?? TestOptions(),
            new FakeCodexProcessFactory(process),
            cancellationToken);
        _ = await process.ReadClientLineAsync(cancellationToken);
        await process.WriteServerLineAsync(
            """{"id":1,"result":{"userAgent":"codex-cli/0.130.0","platformFamily":"windows","platformOs":"windows","codexHome":"C:\\bounded"}}""");
        _ = await process.ReadClientLineAsync(cancellationToken);
        return await start;
    }

    private static async Task<RekallAgeCodexTurn> StartTurnAsync(
        RekallAgeCodexAppServerClient client,
        FakeCodexProcess process,
        CancellationToken cancellationToken)
    {
        var turn = client.StartTurnAsync("thread-1", "task", cancellationToken: cancellationToken);
        AssertJson(
            await process.ReadClientLineAsync(cancellationToken),
            """{"id":2,"method":"turn/start","params":{"threadId":"thread-1","input":[{"type":"text","text":"task"}]}}""");
        await process.WriteServerLineAsync(
            """{"id":2,"result":{"turn":{"id":"turn-1","status":"inProgress","items":[]}}}""");
        return await turn;
    }

    private static RekallAgeCodexAppServerOptions TestOptions() => new()
    {
        ExecutablePath = "codex-test",
        ClientVersion = "test",
        InterruptTimeout = TimeSpan.FromMilliseconds(40),
        ShutdownTimeout = TimeSpan.FromMilliseconds(40)
    };

    private static CancellationTokenSource TestTimeout() =>
        new(TimeSpan.FromSeconds(10));

    private static RekallAgeCodexAppServerOptions WithMaximumLine(
        RekallAgeCodexAppServerOptions source,
        int maximum) => new()
    {
        ExecutablePath = source.ExecutablePath,
        ClientVersion = source.ClientVersion,
        MaximumJsonLineCharacters = maximum,
        InterruptTimeout = source.InterruptTimeout,
        ShutdownTimeout = source.ShutdownTimeout
    };

    private static RekallAgeCodexAppServerOptions WithMaximumStderr(
        RekallAgeCodexAppServerOptions source,
        int maximum) => new()
    {
        ExecutablePath = source.ExecutablePath,
        ClientVersion = source.ClientVersion,
        MaximumStderrCharacters = maximum,
        InterruptTimeout = source.InterruptTimeout,
        ShutdownTimeout = source.ShutdownTimeout
    };

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private static void AssertJson(string actual, string expected)
    {
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)),
            $"Expected JSON:{Environment.NewLine}{expected}{Environment.NewLine}Actual JSON:{Environment.NewLine}{actual}");
    }
}
