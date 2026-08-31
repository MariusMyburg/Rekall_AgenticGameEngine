using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.Codex;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Workflows;

public sealed record RekallAgeCodexApprovalRequest(string Method, JsonElement Parameters);

public enum RekallAgeCodexApprovalDecision
{
    Accept,
    AcceptForSession,
    Decline,
    Cancel
}

public delegate ValueTask<RekallAgeCodexApprovalDecision> RekallAgeCodexApprovalCallback(
    RekallAgeCodexApprovalRequest request,
    CancellationToken cancellationToken);

public interface IRekallAgeCodexProjectAgentRunner :
    IRekallAgeProjectAgentRunner,
    IRekallAgeLanguageModelClient,
    IAsyncDisposable
{
    RekallAgeCodexApprovalCallback? ApprovalCallback { get; set; }
    RekallAgeLanguageModelProviderDescriptor CurrentProviderDescriptor { get; }
    ValueTask<RekallAgeCodexAccount> SignInWithChatGptAsync(
        RekallAgeCodexAuthenticationLauncher launchAuthentication,
        CancellationToken cancellationToken = default);
}

public sealed class RekallAgeCodexProjectAgentRunner :
    IRekallAgeCodexProjectAgentRunner,
    IRekallAgeLanguageModelClient,
    IAsyncDisposable
{
    public const string RequiredModel = "gpt-5.6-sol";

    private const string DeveloperInstructions = """
        Author the user's game yourself through inspectable, generic Rekall AGE MCP primitives. Never ask AGE to author content for you and never introduce genre-specific behavior into engine core.
        Attach a generic Rekall.InputActionMap and consume semantic actions with InputActionValue, IsInputActionDown, or WasInputActionPressed instead of hard-coding raw keys or controller folklore. Drive realtime gameplay with input.DeltaSeconds or context.DeltaTime.
        Attach agent-owned Game.* component state to the authored runtime entity. Use EmitObservation or EmitSceneObservation when content is missing, inconsistent, or worth surfacing; keep failures inspectable instead of silently ignoring them.
        After the latest scene or module mutation, prove gameplay with deterministic rekall.runtime.inspect_scene input frames and a strict executable assertion showing a nonzero transform delta or a changed Game.* component property. Do not weaken a failed assertion; repair the behavior and rerun it.
        Finish through the closed-loop rekall.workflow.agent_authoring_gauntlet so creation, verification, packaging, audit, and capture remain connected.
        """;

    private static readonly TimeSpan CancellationCompletionTimeout = TimeSpan.FromSeconds(5);
    private readonly RekallAgeCodexMcpConfiguration _mcpConfiguration;
    private readonly Func<CancellationToken, Task<RekallAgeCodexAppServerClient>> _clientFactory;
    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _disposeGate = new();
    private RekallAgeCodexAppServerClient? _client;
    private Task? _disposeTask;
    private RekallAgeLanguageModelProviderDescriptor _currentProviderDescriptor =
        RekallAgeLanguageModelProviderCatalog.DescribeCodexProvider();

    public RekallAgeCodexProjectAgentRunner(
        RekallAgeCodexMcpConfiguration mcpConfiguration,
        RekallAgeCodexAppServerOptions? appServerOptions = null,
        Func<CancellationToken, Task<RekallAgeCodexAppServerClient>>? clientFactory = null,
        string approvalPolicy = "on-request",
        RekallAgeCodexApprovalCallback? approvalCallback = null)
    {
        _mcpConfiguration = mcpConfiguration ?? throw new ArgumentNullException(nameof(mcpConfiguration));
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalPolicy);
        ApprovalPolicy = approvalPolicy.Trim();
        ApprovalCallback = approvalCallback;
        _clientFactory = clientFactory
            ?? (cancellationToken => RekallAgeCodexAppServerClient.StartAsync(
                appServerOptions,
                cancellationToken: cancellationToken));
    }

    public string ProviderId => "codex";

    public string ApprovalPolicy { get; }

    public RekallAgeCodexApprovalCallback? ApprovalCallback { get; set; }

    public RekallAgeLanguageModelProviderDescriptor CurrentProviderDescriptor =>
        Volatile.Read(ref _currentProviderDescriptor);

    public async ValueTask<RekallAgeCodexAccount> ReadAccountAsync(
        bool refreshToken = false,
        CancellationToken cancellationToken = default)
    {
        var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        var account = await client.ReadAccountAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        Volatile.Write(
            ref _currentProviderDescriptor,
            RekallAgeLanguageModelProviderCatalog.DescribeCodexProvider(account));
        return account;
    }

    public async ValueTask<RekallAgeCodexAccount> SignInWithChatGptAsync(
        RekallAgeCodexAuthenticationLauncher launchAuthentication,
        CancellationToken cancellationToken = default)
    {
        var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        var account = await client.SignInWithChatGptAsync(launchAuthentication, cancellationToken).ConfigureAwait(false);
        Volatile.Write(
            ref _currentProviderDescriptor,
            RekallAgeLanguageModelProviderCatalog.DescribeCodexProvider(account));
        return account;
    }

    public async ValueTask<RekallAgeLanguageModelProviderDescriptor> DescribeProviderAsync(
        CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        var account = await client.ReadAccountAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RekallAgeCodexModel>? models = account.IsAuthenticated
            ? await client.ListModelsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
            : null;
        var descriptor = RekallAgeLanguageModelProviderCatalog.DescribeCodexProvider(account, models);
        Volatile.Write(ref _currentProviderDescriptor, descriptor);
        return descriptor;
    }

    public async ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        var account = await client.ReadAccountAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!account.IsAuthenticated)
        {
            Volatile.Write(
                ref _currentProviderDescriptor,
                RekallAgeLanguageModelProviderCatalog.DescribeCodexProvider(account));
            throw ProviderError(
                RekallAgeCodexErrorCodes.AuthenticationRequired,
                "Codex authentication is required. Sign in through Codex and retry.");
        }

        var models = await client.ListModelsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        Volatile.Write(
            ref _currentProviderDescriptor,
            RekallAgeLanguageModelProviderCatalog.DescribeCodexProvider(account, models));
        return models
            .Where(model => !model.Hidden)
            .Select(model => new RekallAgeLanguageModelInfo(model.Model, 0))
            .ToArray();
    }

    public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
        RekallAgeLanguageModelRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<RekallAgeLanguageModelResponse>(
            ProviderError(
                RekallAgeCodexErrorCodes.ProtocolUnsupported,
                "Codex App Server supports project-agent runs rather than direct chat requests."));

    public async ValueTask<RekallAgeProjectAgentSessionResult> RunAsync(
        RekallAgeProjectAgentSessionRequest request,
        IProgress<RekallAgeLanguageModelAgentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        await _runGate.WaitAsync(runCancellation.Token).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        var evidence = new RunEvidence(progress);
        RekallAgeCodexAppServerClient? client = null;
        RekallAgeCodexThread? thread = null;
        RekallAgeCodexTurn? turn = null;
        try
        {
            var projectRoot = RekallAgeProjectCommandScope.NormalizeProjectRoot(request.ProjectRoot);
            client = await EnsureClientAsync(runCancellation.Token).ConfigureAwait(false);
            var account = await client.ReadAccountAsync(cancellationToken: runCancellation.Token).ConfigureAwait(false);
            if (!account.IsAuthenticated)
            {
                throw ProviderError(
                    RekallAgeCodexErrorCodes.AuthenticationRequired,
                    "Codex authentication is required. Sign in through Codex and retry.");
            }

            var models = await client.ListModelsAsync(cancellationToken: runCancellation.Token).ConfigureAwait(false);
            Volatile.Write(
                ref _currentProviderDescriptor,
                RekallAgeLanguageModelProviderCatalog.DescribeCodexProvider(account, models));
            if (!models.Any(model =>
                    !model.Hidden
                    && string.Equals(model.Model, RequiredModel, StringComparison.Ordinal)))
            {
                throw new RekallAgeLanguageModelProviderException(
                    RekallAgeCodexErrorCodes.ModelUnavailable,
                    ProviderId,
                    "The exact Codex project model is unavailable.",
                    requestedValue: RequiredModel,
                    resolvedValue: string.Join(',', models.Where(model => !model.Hidden).Select(model => model.Model)));
            }

            var mcpServer = _mcpConfiguration.CreateValidatedServer(projectRoot);
            thread = await client.StartThreadAsync(
                new RekallAgeCodexThreadStartRequest(projectRoot, RequiredModel, DeveloperInstructions)
                {
                    ApprovalPolicy = ApprovalPolicy,
                    Ephemeral = true,
                    NetworkEnabled = false,
                    McpServers = [mcpServer]
                },
                runCancellation.Token).ConfigureAwait(false);
            turn = await client.StartTurnAsync(
                thread.Id,
                RekallAgeAgentTaskComposer.Compose(projectRoot, request.SceneName, request.Task),
                request.Think,
                runCancellation.Token).ConfigureAwait(false);

            using var pumpCancellation = new CancellationTokenSource();
            var terminalNotificationObserved = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var pumps = new[]
            {
                PumpNotificationsAsync(client, evidence, terminalNotificationObserved, pumpCancellation.Token),
                PumpServerRequestsAsync(client, evidence, pumpCancellation.Token),
                PumpDiagnosticsAsync(client, evidence, pumpCancellation.Token)
            };
            RekallAgeCodexTurnCompletion completion;
            try
            {
                completion = await WaitForTerminalCompletionAsync(
                    client,
                    turn,
                    runCancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                // The notification channel is FIFO. Observing the terminal notification proves every
                // preceding tool/message/usage fact has been projected before the pump is cancelled.
                try { await terminalNotificationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                catch (TimeoutException) { }
                pumpCancellation.Cancel();
                await ObservePumpCompletionAsync(pumps).ConfigureAwait(false);
            }

            stopwatch.Stop();
            var completedSuccessfully = completion.Status.Equals("completed", StringComparison.OrdinalIgnoreCase);
            if (!completedSuccessfully)
            {
                evidence.FailPendingTools(completion.Status);
            }
            return CreateResult(
                succeeded: completedSuccessfully,
                completed: completedSuccessfully,
                completion.Status,
                request,
                thread,
                turn,
                evidence,
                stopwatch.Elapsed);
        }
        catch (RekallAgeLanguageModelProviderException error)
        {
            stopwatch.Stop();
            evidence.FailPendingTools(error.Code);
            if (error.Code == RekallAgeCodexErrorCodes.Cancelled && client is not null)
            {
                await ResetOwnedClientAsync(client).ConfigureAwait(false);
            }

            return CreateResult(
                succeeded: false,
                completed: false,
                error.Code,
                request,
                thread,
                turn,
                evidence,
                stopwatch.Elapsed,
                error.Message);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            evidence.FailPendingTools(RekallAgeCodexErrorCodes.Cancelled);
            if (client is not null)
            {
                await ResetOwnedClientAsync(client).ConfigureAwait(false);
            }

            return CreateResult(
                succeeded: false,
                completed: false,
                RekallAgeCodexErrorCodes.Cancelled,
                request,
                thread,
                turn,
                evidence,
                stopwatch.Elapsed,
                "The Codex project run was cancelled.");
        }
        finally
        {
            _runGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is null)
            {
                _lifetimeCancellation.Cancel();
                _disposeTask = DisposeCoreAsync();
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task<RekallAgeCodexTurnCompletion> WaitForTerminalCompletionAsync(
        RekallAgeCodexAppServerClient client,
        RekallAgeCodexTurn turn,
        CancellationToken cancellationToken)
    {
        var completion = client.WaitForTurnCompletionAsync(turn, CancellationToken.None);
        if (!cancellationToken.CanBeCanceled)
        {
            return await completion.ConfigureAwait(false);
        }

        var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (await Task.WhenAny(completion, cancelled).ConfigureAwait(false) == completion)
        {
            return await completion.ConfigureAwait(false);
        }

        using var timeout = new CancellationTokenSource(CancellationCompletionTimeout);
        try
        {
            await client.InterruptTurnAsync(turn.ThreadId, turn.Id, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            throw ProviderError(
                RekallAgeCodexErrorCodes.Cancelled,
                "The Codex turn was cancelled before its interrupt acknowledgement arrived.");
        }

        try
        {
            return await completion.WaitAsync(CancellationCompletionTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw ProviderError(
                RekallAgeCodexErrorCodes.Cancelled,
                "The Codex turn was cancelled before terminal completion arrived.");
        }
    }

    private async Task PumpServerRequestsAsync(
        RekallAgeCodexAppServerClient client,
        RunEvidence evidence,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var serverRequest = await client.ReadServerRequestAsync(cancellationToken).ConfigureAwait(false);
                var decision = RekallAgeCodexApprovalDecision.Decline;
                var callback = ApprovalCallback;
                if (callback is not null)
                {
                    try
                    {
                        decision = await callback(
                            new RekallAgeCodexApprovalRequest(
                                serverRequest.Method,
                                serverRequest.Params.Clone()),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        evidence.AddDiagnostic(
                            RekallAgeCodexErrorCodes.TurnFailed,
                            "Approval callback failed; the request was denied.");
                        decision = RekallAgeCodexApprovalDecision.Decline;
                    }
                }

                var decisionValue = serverRequest.Method == "mcpServer/elicitation/request"
                    && decision == RekallAgeCodexApprovalDecision.AcceptForSession
                        ? "accept"
                        : ApprovalDecisionValue(decision);
                var response = serverRequest.Method == "mcpServer/elicitation/request"
                    ? new JsonObject { ["action"] = decisionValue }
                    : new JsonObject { ["decision"] = decisionValue };
                if (serverRequest.Method == "mcpServer/elicitation/request"
                    && decision is RekallAgeCodexApprovalDecision.Accept or RekallAgeCodexApprovalDecision.AcceptForSession)
                {
                    response["content"] = new JsonObject();
                }
                await client.RespondToServerRequestAsync(
                    serverRequest,
                    response,
                    cancellationToken).ConfigureAwait(false);
                var approvalFact = $"{serverRequest.Method}: {decisionValue}";
                evidence.AddDiagnostic("REKALL_CODEX_APPROVAL", approvalFact);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task PumpNotificationsAsync(
        RekallAgeCodexAppServerClient client,
        RunEvidence evidence,
        TaskCompletionSource terminalNotificationObserved,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var notification = await client.ReadNotificationAsync(cancellationToken).ConfigureAwait(false);
                evidence.Observe(notification);
                if (notification.Method.Equals("turn/completed", StringComparison.Ordinal))
                {
                    terminalNotificationObserved.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task PumpDiagnosticsAsync(
        RekallAgeCodexAppServerClient client,
        RunEvidence evidence,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var diagnostic = await client.ReadDiagnosticAsync(cancellationToken).ConfigureAwait(false);
                evidence.AddDiagnostic(diagnostic.Code, diagnostic.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<RekallAgeCodexAppServerClient> EnsureClientAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeTask) is not null, this);
        if (_client is not null)
        {
            return _client;
        }

        await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeTask) is not null, this);
            if (_client is null)
            {
                _mcpConfiguration.ValidateExecutable();
                _client = await _clientFactory(cancellationToken).ConfigureAwait(false);
            }

            return _client;
        }
        finally
        {
            _clientGate.Release();
        }
    }

    private async Task ResetOwnedClientAsync(RekallAgeCodexAppServerClient client)
    {
        if (ReferenceEquals(_client, client))
        {
            _client = null;
        }

        await client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        await _runGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client is not null)
            {
                await _client.DisposeAsync().ConfigureAwait(false);
                _client = null;
            }
        }
        finally
        {
            _runGate.Release();
            _lifetimeCancellation.Dispose();
            _clientGate.Dispose();
            _runGate.Dispose();
        }
    }

    private static RekallAgeProjectAgentSessionResult CreateResult(
        bool succeeded,
        bool completed,
        string stopReason,
        RekallAgeProjectAgentSessionRequest request,
        RekallAgeCodexThread? thread,
        RekallAgeCodexTurn? turn,
        RunEvidence evidence,
        TimeSpan elapsed,
        string? failureMessage = null)
    {
        var content = evidence.FinalContent;
        var usage = new RekallAgeLanguageModelUsage(
            evidence.InputTokens,
            evidence.OutputTokens,
            ElapsedNanoseconds(elapsed))
        {
            CachedInputTokens = evidence.CachedInputTokens,
            ReasoningTokens = evidence.ReasoningTokens
        };
        var toolExecutions = evidence.ToolExecutions;
        var agentResult = new RekallAgeLanguageModelAgentResult(
            completed,
            stopReason,
            content,
            turn is null ? 0 : 1,
            toolExecutions.Count,
            usage,
            [
                new RekallAgeLanguageModelMessage("user", request.Task),
                new RekallAgeLanguageModelMessage("assistant", content)
            ])
        {
            ResponseId = thread?.Id,
            ToolExecutions = toolExecutions
        };
        var identifiers = thread is null
            ? "before a thread started"
            : turn is null
                ? $"in thread {thread.Id} before a turn started"
                : $"in thread {thread.Id} turn {turn.Id}";
        var facts = $"tools={toolExecutions.Count}, inputTokens={usage.PromptTokens}, outputTokens={usage.CompletionTokens}, elapsed={elapsed.TotalSeconds:F2}s";
        var diagnostics = evidence.Diagnostics.Count == 0
            ? string.Empty
            : " Diagnostics: " + string.Join(" | ", evidence.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        var summary = succeeded
            ? $"Codex completed {identifiers}; {facts}.{diagnostics}"
            : $"Codex stopped {identifiers} with {stopReason}; {facts}. {Bound(failureMessage ?? "The Codex project run did not complete.", 1_000)}{diagnostics}";
        return new RekallAgeProjectAgentSessionResult(succeeded, Bound(summary, 8_192), agentResult);
    }

    private static async Task ObservePumpCompletionAsync(IEnumerable<Task> pumps)
    {
        foreach (var pump in pumps)
        {
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static void ValidateRequest(RekallAgeProjectAgentSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SceneName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Task);
        if (!string.Equals(request.Model, RequiredModel, StringComparison.Ordinal))
        {
            throw new RekallAgeLanguageModelProviderException(
                RekallAgeCodexErrorCodes.ModelUnavailable,
                "codex",
                "The Codex project runner requires the exact supported model.",
                requestedValue: request.Model,
                resolvedValue: RequiredModel);
        }
    }

    private static string ApprovalDecisionValue(RekallAgeCodexApprovalDecision decision) => decision switch
    {
        RekallAgeCodexApprovalDecision.Accept => "accept",
        RekallAgeCodexApprovalDecision.AcceptForSession => "acceptForSession",
        RekallAgeCodexApprovalDecision.Decline => "decline",
        RekallAgeCodexApprovalDecision.Cancel => "cancel",
        _ => "decline"
    };

    private static RekallAgeLanguageModelProviderException ProviderError(string code, string message) =>
        new(code, "codex", message);

    private static long ElapsedNanoseconds(TimeSpan elapsed) =>
        Math.Max(1, checked((long)(elapsed.TotalMilliseconds * 1_000_000d)));

    private static string Bound(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : value[..maximumCharacters] + "…";

    private sealed class RunEvidence(IProgress<RekallAgeLanguageModelAgentProgress>? progress)
    {
        private readonly object _gate = new();
        private readonly StringBuilder _finalContent = new();
        private readonly List<RekallAgeLanguageModelToolExecution> _toolExecutions = [];
        private readonly Dictionary<string, (string ToolName, JsonObject Arguments)> _pendingTools = new(StringComparer.Ordinal);
        private readonly List<RekallAgeCodexDiagnostic> _diagnostics = [];
        private int _inputTokens;
        private int _outputTokens;
        private int? _cachedInputTokens;
        private int? _reasoningTokens;

        public string FinalContent
        {
            get
            {
                lock (_gate) return _finalContent.ToString();
            }
        }

        public IReadOnlyList<RekallAgeLanguageModelToolExecution> ToolExecutions
        {
            get
            {
                lock (_gate) return _toolExecutions.ToArray();
            }
        }

        public IReadOnlyList<RekallAgeCodexDiagnostic> Diagnostics
        {
            get
            {
                lock (_gate) return _diagnostics.ToArray();
            }
        }

        public int InputTokens => Volatile.Read(ref _inputTokens);

        public int OutputTokens => Volatile.Read(ref _outputTokens);

        public int? CachedInputTokens
        {
            get
            {
                lock (_gate) return _cachedInputTokens;
            }
        }

        public int? ReasoningTokens
        {
            get
            {
                lock (_gate) return _reasoningTokens;
            }
        }

        public void Observe(RekallAgeCodexNotification notification)
        {
            ObserveUsage(notification.Params);
            if (!notification.Params.TryGetProperty("item", out var item)
                || item.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var type = StringProperty(item, "type");
            if (string.Equals(type, "agentMessage", StringComparison.Ordinal)
                && notification.Method.EndsWith("completed", StringComparison.Ordinal)
                && StringProperty(item, "text") is { Length: > 0 } text)
            {
                lock (_gate)
                {
                    if (_finalContent.Length > 0) _finalContent.AppendLine();
                    _finalContent.Append(Bound(text, 8_192));
                }
                Report("agent.message", text);
                return;
            }

            if (type is not ("mcpToolCall" or "commandExecution"))
            {
                return;
            }

            var toolName = StringProperty(item, "tool")
                ?? StringProperty(item, "toolName")
                ?? StringProperty(item, "name")
                ?? type;
            if (!notification.Method.EndsWith("completed", StringComparison.Ordinal))
            {
                var itemId = StringProperty(item, "id");
                if (itemId is not null)
                {
                    var startedArguments = item.TryGetProperty("arguments", out var startedArgumentElement)
                        && startedArgumentElement.ValueKind == JsonValueKind.Object
                            ? JsonNode.Parse(startedArgumentElement.GetRawText()) as JsonObject ?? new JsonObject()
                            : new JsonObject();
                    lock (_gate) _pendingTools[itemId] = (toolName, startedArguments);
                }
                Report("tool.started", toolName);
                return;
            }

            var error = ReadPayload(item, "error");
            var status = StringProperty(item, "status");
            var succeeded = error.Length == 0 && !string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);
            var preview = error.Length > 0
                ? error
                : FirstNonEmpty(
                    ReadPayload(item, "result"),
                    ReadPayload(item, "output"),
                    status ?? "completed");
            var arguments = item.TryGetProperty("arguments", out var argumentElement)
                && argumentElement.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(argumentElement.GetRawText()) as JsonObject ?? new JsonObject()
                    : new JsonObject();
            RekallAgeLanguageModelToolExecution execution;
            lock (_gate)
            {
                if (StringProperty(item, "id") is { } itemId) _pendingTools.Remove(itemId);
                execution = new RekallAgeLanguageModelToolExecution(
                    _toolExecutions.Count + 1,
                    toolName,
                    arguments,
                    succeeded,
                    Bound(preview, 4_000));
                _toolExecutions.Add(execution);
            }
            Report(
                succeeded ? "tool.completed" : "tool.failed",
                succeeded ? $"{toolName} completed." : $"{toolName}: {preview}",
                execution);
        }

        public void FailPendingTools(string turnStatus)
        {
            RekallAgeLanguageModelToolExecution[] failed;
            lock (_gate)
            {
                failed = _pendingTools.Values.Select((pending, index) => new RekallAgeLanguageModelToolExecution(
                    _toolExecutions.Count + index + 1,
                    pending.ToolName,
                    pending.Arguments,
                    false,
                    $"Codex turn ended with status '{turnStatus}' before this tool returned a completion receipt.")).ToArray();
                _toolExecutions.AddRange(failed);
                _pendingTools.Clear();
            }
            foreach (var execution in failed)
            {
                Report("tool.failed", $"{execution.Name}: {execution.ResultPreview}", execution);
            }
        }

        public void AddDiagnostic(string code, string message)
        {
            var bounded = new RekallAgeCodexDiagnostic(Bound(code, 128), Bound(message, 2_000));
            lock (_gate)
            {
                if (_diagnostics.Count < 64) _diagnostics.Add(bounded);
            }
            Report("codex.diagnostic", $"{bounded.Code}: {bounded.Message}");
        }

        public void Report(
            string phase,
            string message,
            RekallAgeLanguageModelToolExecution? execution = null) =>
            progress?.Report(new RekallAgeLanguageModelAgentProgress(
                1,
                phase,
                Bound(message, 2_000),
                execution));

        private void ObserveUsage(JsonElement parameters)
        {
            var usage = parameters.TryGetProperty("tokenUsage", out var tokenUsage)
                && tokenUsage.ValueKind == JsonValueKind.Object
                    ? tokenUsage
                    : parameters;
            if (usage.TryGetProperty("total", out var total)
                && total.ValueKind == JsonValueKind.Object)
            {
                usage = total;
            }
            if (IntProperty(usage, "inputTokens", "input_tokens") is { } input)
            {
                Interlocked.Exchange(ref _inputTokens, input);
            }
            if (IntProperty(usage, "outputTokens", "outputTokenCount", "output_tokens") is { } output)
            {
                Interlocked.Exchange(ref _outputTokens, output);
            }
            lock (_gate)
            {
                _cachedInputTokens = IntProperty(usage, "cachedInputTokens", "cached_input_tokens")
                    ?? _cachedInputTokens;
                _reasoningTokens = IntProperty(
                    usage,
                    "reasoningOutputTokens",
                    "reasoningTokens",
                    "reasoning_output_tokens",
                    "reasoning_tokens")
                    ?? _reasoningTokens;
            }
        }

        private static int? IntProperty(JsonElement value, params string[] names)
        {
            foreach (var name in names)
            {
                if (value.TryGetProperty(name, out var property)
                    && property.ValueKind == JsonValueKind.Number
                    && property.TryGetInt32(out var result))
                {
                    return result;
                }
            }
            return null;
        }

        private static string? StringProperty(JsonElement value, string name) =>
            value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static string ReadPayload(JsonElement value, string name)
        {
            if (!value.TryGetProperty(name, out var property)
                || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return string.Empty;
            }
            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }
            if (property.ValueKind == JsonValueKind.Object
                && StringProperty(property, "message") is { Length: > 0 } message)
            {
                return message;
            }
            return property.GetRawText();
        }

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(value => value.Length > 0) ?? string.Empty;
    }
}
