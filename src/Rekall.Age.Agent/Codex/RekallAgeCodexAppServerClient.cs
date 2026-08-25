using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Agent.Codex;

public sealed partial class RekallAgeCodexAppServerClient : IAsyncDisposable
{
    private const string ProviderId = "codex";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IRekallAgeCodexProcess _process;
    private readonly RekallAgeCodexAppServerOptions _options;
    private readonly ConcurrentDictionary<long, PendingRequest> _pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RekallAgeCodexTurnCompletion>> _turnCompletions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RekallAgeCodexTurnCompletion> _earlyTurnCompletions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _pendingSlots;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly CancellationTokenSource _drainCancellation = new();
    private readonly Channel<RekallAgeCodexNotification> _notifications;
    private readonly Channel<RekallAgeCodexServerRequest> _serverRequests;
    private readonly Channel<RekallAgeCodexDiagnostic> _diagnostics;
    private readonly Task _stdoutDrain;
    private readonly Task _stderrDrain;
    private readonly object _disposeGate = new();
    private readonly object _lifecycleGate = new();
    private readonly object _activeTurnGate = new();
    private readonly object _stderrGate = new();
    private readonly StringBuilder _stderrHistory = new();
    private readonly StringBuilder _stderrLine = new();
    private Task? _disposeTask;
    private RekallAgeLanguageModelProviderException? _terminalError;
    private string? _activeThreadId;
    private string? _activeTurnId;
    private long _nextRequestId;
    private long _stderrCharactersRead;
    private int _disposing;
    private int _turnStartPending;
    private bool _stderrLineOverflow;

    private RekallAgeCodexAppServerClient(
        IRekallAgeCodexProcess process,
        RekallAgeCodexAppServerOptions options)
    {
        _process = process;
        _options = options;
        _pendingSlots = new SemaphoreSlim(options.MaximumPendingRequests, options.MaximumPendingRequests);
        _notifications = CreateBoundedChannel<RekallAgeCodexNotification>(options.NotificationCapacity);
        _serverRequests = CreateBoundedChannel<RekallAgeCodexServerRequest>(options.ServerRequestCapacity);
        _diagnostics = CreateBoundedChannel<RekallAgeCodexDiagnostic>(options.DiagnosticCapacity);

        // Start both drains before the first protocol write so neither redirected pipe can block startup.
        _stdoutDrain = DrainStdoutAsync(_drainCancellation.Token);
        _stderrDrain = DrainStderrAsync(_drainCancellation.Token);
    }

    internal int PendingRequestCount => _pending.Count;

    internal int RetainedStderrCharacterCount
    {
        get
        {
            lock (_stderrGate)
            {
                return _stderrHistory.Length;
            }
        }
    }

    internal long StderrCharactersRead => Interlocked.Read(ref _stderrCharactersRead);

    internal string SanitizedStderrSnapshot
    {
        get
        {
            lock (_stderrGate)
            {
                return _stderrHistory.ToString();
            }
        }
    }

    public RekallAgeCodexInitializeResult InitializeResult { get; private set; } = null!;

    public static async Task<RekallAgeCodexAppServerClient> StartAsync(
        RekallAgeCodexAppServerOptions? options = null,
        IRekallAgeCodexProcessFactory? processFactory = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RekallAgeCodexAppServerOptions();
        options.Validate();
        processFactory ??= new RekallAgeCodexProcessFactory();

        var executable = !string.IsNullOrWhiteSpace(options.ExecutablePath)
            ? options.ExecutablePath
            : Environment.GetEnvironmentVariable("REKALL_AGE_CODEX_PATH");
        executable = string.IsNullOrWhiteSpace(executable) ? "codex" : executable;

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");

        IRekallAgeCodexProcess process;
        try
        {
            process = processFactory.Start(startInfo);
        }
        catch (Exception error) when (error is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
        {
            throw ProviderError(
                RekallAgeCodexErrorCodes.RuntimeMissing,
                "Codex App Server is unavailable. Install or update Codex, then verify the configured executable path.");
        }

        var client = new RekallAgeCodexAppServerClient(process, options);
        try
        {
            await client.InitializeAsync(cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task<RekallAgeCodexAccount> ReadAccountAsync(
        bool refreshToken = false,
        CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync(
            "account/read",
            new JsonObject { ["refreshToken"] = refreshToken },
            cancellationToken);
        if (!TryGetBoolean(result, "requiresOpenaiAuth", out var requiresOpenAiAuth))
        {
            throw ProtocolInvalid("Codex returned an invalid account/read response.");
        }

        string? authenticationType = null;
        var isAuthenticated = false;
        if (result.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
        {
            isAuthenticated = true;
            authenticationType = GetRequiredString(account, "type", "account/read");
        }

        return new RekallAgeCodexAccount(authenticationType, requiresOpenAiAuth, isAuthenticated);
    }

    public async Task<IReadOnlyList<RekallAgeCodexModel>> ListModelsAsync(
        bool includeHidden = false,
        CancellationToken cancellationToken = default)
    {
        var models = new List<RekallAgeCodexModel>();
        string? cursor = null;
        for (var page = 0; page < _options.MaximumModelPages; page++)
        {
            var parameters = new JsonObject
            {
                ["includeHidden"] = includeHidden,
                ["limit"] = _options.ModelPageSize
            };
            if (cursor is not null)
            {
                parameters["cursor"] = cursor;
            }
            var result = await RequestAsync("model/list", parameters, cancellationToken);
            if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                throw ProtocolInvalid("Codex returned an invalid model/list response.");
            }

            foreach (var model in data.EnumerateArray())
            {
                models.Add(ParseModel(model));
            }

            cursor = result.TryGetProperty("nextCursor", out var nextCursor)
                && nextCursor.ValueKind == JsonValueKind.String
                    ? nextCursor.GetString()
                    : null;
            if (string.IsNullOrEmpty(cursor))
            {
                return models;
            }
        }

        throw ProtocolInvalid("Codex model pagination exceeded the configured bound.");
    }

    public async Task<RekallAgeCodexThread> StartThreadAsync(
        RekallAgeCodexThreadStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.ProjectRoot))
        {
            throw new ArgumentException("The Codex project root must be an absolute path.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Model) || string.IsNullOrWhiteSpace(request.DeveloperInstructions))
        {
            throw new ArgumentException("The Codex model and developer instructions are required.", nameof(request));
        }

        var projectRoot = NormalizeProjectRoot(request.ProjectRoot);
        var config = CreateThreadConfig(request, projectRoot);

        var result = await RequestAsync(
            "thread/start",
            new JsonObject
            {
                ["approvalPolicy"] = request.ApprovalPolicy,
                ["config"] = config,
                ["cwd"] = projectRoot,
                ["developerInstructions"] = request.DeveloperInstructions,
                ["model"] = request.Model,
                ["sandbox"] = "workspace-write"
            },
            cancellationToken);
        if (!result.TryGetProperty("thread", out var thread) || thread.ValueKind != JsonValueKind.Object)
        {
            throw ProtocolInvalid("Codex returned an invalid thread/start response.");
        }

        var threadId = GetRequiredString(thread, "id", "thread/start");
        var resolvedModel = GetRequiredString(result, "model", "thread/start");
        var resolvedProjectRoot = GetRequiredString(result, "cwd", "thread/start");
        if (!string.Equals(request.Model, resolvedModel, StringComparison.Ordinal))
        {
            throw new RekallAgeLanguageModelProviderException(
                RekallAgeCodexErrorCodes.ModelUnavailable,
                ProviderId,
                "Codex did not select the exact requested model.",
                requestedValue: request.Model,
                resolvedValue: resolvedModel);
        }

        if (!ProjectRootsMatch(projectRoot, resolvedProjectRoot))
        {
            throw ProtocolInvalid("Codex returned a working directory outside the requested project root.");
        }

        return new RekallAgeCodexThread(threadId, resolvedModel, projectRoot);
    }

    private static JsonObject CreateThreadConfig(
        RekallAgeCodexThreadStartRequest request,
        string projectRoot)
    {
        var config = new JsonObject
        {
            ["sandbox_workspace_write"] = new JsonObject
            {
                ["network_access"] = request.NetworkEnabled,
                ["writable_roots"] = new JsonArray(projectRoot)
            }
        };
        if (request.McpServers.Count == 0)
        {
            return config;
        }

        var mcpServers = new JsonObject();
        foreach (var server in request.McpServers)
        {
            if (string.IsNullOrWhiteSpace(server.Name) || string.IsNullOrWhiteSpace(server.Command))
            {
                throw new ArgumentException("Codex MCP server names and commands must be non-empty.", nameof(request));
            }

            if (mcpServers.ContainsKey(server.Name))
            {
                throw new ArgumentException("Codex MCP server names must be unique.", nameof(request));
            }

            var arguments = new JsonArray();
            foreach (var argument in server.Arguments)
            {
                arguments.Add(argument);
            }

            mcpServers[server.Name] = new JsonObject
            {
                ["command"] = server.Command,
                ["args"] = arguments
            };
        }

        config["mcp_servers"] = mcpServers;
        return config;
    }

    private static string NormalizeProjectRoot(string projectRoot) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));

    private static bool ProjectRootsMatch(string requestedProjectRoot, string resolvedProjectRoot)
    {
        if (!Path.IsPathFullyQualified(resolvedProjectRoot))
        {
            return false;
        }

        string normalizedResolvedRoot;
        try
        {
            normalizedResolvedRoot = NormalizeProjectRoot(resolvedProjectRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return string.Equals(
            requestedProjectRoot,
            normalizedResolvedRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public async Task<RekallAgeCodexTurn> StartTurnAsync(
        string threadId,
        string task,
        string? reasoningEffort = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequired(threadId, nameof(threadId));
        ValidateRequired(task, nameof(task));
        ThrowIfUnavailable(cancellationToken);

        if (Interlocked.CompareExchange(ref _turnStartPending, 1, 0) != 0)
        {
            throw ProtocolInvalid("A Codex turn is already active or starting for this client.");
        }

        lock (_activeTurnGate)
        {
            if (_activeTurnId is not null)
            {
                Interlocked.Exchange(ref _turnStartPending, 0);
                throw ProtocolInvalid("A Codex turn is already active for this client.");
            }
        }

        var parameters = new JsonObject
        {
            ["threadId"] = threadId,
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = task
                }
            }
        };
        if (reasoningEffort is not null)
        {
            parameters.Insert(0, "effort", reasoningEffort);
        }
        Task<JsonElement> resultTask;
        try
        {
            // Once this write begins, retain the response even if the caller stops waiting. A late
            // response is the only safe way to learn the exact server-side turn ID to interrupt.
            resultTask = RequestAsync("turn/start", parameters, CancellationToken.None);
        }
        catch
        {
            Interlocked.Exchange(ref _turnStartPending, 0);
            throw;
        }

        JsonElement result;
        try
        {
            result = await resultTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _ = ObserveCancelledTurnStartAsync(threadId, resultTask);
            throw ProviderError(RekallAgeCodexErrorCodes.Cancelled, "The Codex turn start was cancelled.");
        }
        catch
        {
            Interlocked.Exchange(ref _turnStartPending, 0);
            throw;
        }

        try
        {
            return RegisterTurn(threadId, result);
        }
        finally
        {
            Interlocked.Exchange(ref _turnStartPending, 0);
        }
    }

    private RekallAgeCodexTurn RegisterTurn(string threadId, JsonElement result)
    {
        if (!result.TryGetProperty("turn", out var turn) || turn.ValueKind != JsonValueKind.Object)
        {
            throw ProtocolInvalid("Codex returned an invalid turn/start response.");
        }

        var turnId = GetRequiredString(turn, "id", "turn/start");
        var status = GetRequiredString(turn, "status", "turn/start");
        var completion = new TaskCompletionSource<RekallAgeCodexTurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        RekallAgeCodexTurnCompletion? earlyCompletion;
        lock (_lifecycleGate)
        {
            ThrowIfUnavailableLocked(CancellationToken.None, allowDuringDisposal: false);
            lock (_activeTurnGate)
            {
                if (!_turnCompletions.TryAdd(turnId, completion))
                {
                    throw ProtocolInvalid("Codex returned a duplicate turn identifier.");
                }

                if (_earlyTurnCompletions.Remove(turnId, out earlyCompletion))
                {
                    _activeThreadId = null;
                    _activeTurnId = null;
                }
                else
                {
                    _activeThreadId = threadId;
                    _activeTurnId = turnId;
                }
            }
        }

        if (earlyCompletion is not null)
        {
            completion.TrySetResult(earlyCompletion);
        }

        return new RekallAgeCodexTurn(threadId, turnId, status);
    }

    public async Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken = default)
    {
        ValidateRequired(threadId, nameof(threadId));
        ValidateRequired(turnId, nameof(turnId));
        await RequestAsync(
            "turn/interrupt",
            new JsonObject
            {
                ["threadId"] = threadId,
                ["turnId"] = turnId
            },
            cancellationToken,
            allowDuringDisposal: false);
    }

    public async Task<RekallAgeCodexTurnCompletion> WaitForTurnCompletionAsync(
        RekallAgeCodexTurn turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);
        if (!_turnCompletions.TryGetValue(turn.Id, out var source))
        {
            throw ProtocolInvalid("The requested Codex turn is not active.");
        }

        RekallAgeCodexTurnCompletion completion;
        try
        {
            completion = await source.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await InterruptForCancellationAsync(turn);
            throw ProviderError(RekallAgeCodexErrorCodes.Cancelled, "The Codex turn was cancelled.");
        }

        _turnCompletions.TryRemove(turn.Id, out _);
        return completion.Status switch
        {
            "completed" => completion,
            "interrupted" => throw ProviderError(RekallAgeCodexErrorCodes.Cancelled, "The Codex turn was interrupted."),
            "failed" => throw ProviderError(RekallAgeCodexErrorCodes.TurnFailed, "The Codex turn failed."),
            _ => throw ProtocolInvalid("Codex completed a turn with an invalid terminal status.")
        };
    }

    public ValueTask<RekallAgeCodexNotification> ReadNotificationAsync(CancellationToken cancellationToken = default) =>
        _notifications.Reader.ReadAsync(cancellationToken);

    public ValueTask<RekallAgeCodexServerRequest> ReadServerRequestAsync(CancellationToken cancellationToken = default) =>
        _serverRequests.Reader.ReadAsync(cancellationToken);

    public Task RespondToServerRequestAsync(
        RekallAgeCodexServerRequest request,
        JsonNode? result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WriteServerResponseAsync(
            request.Id,
            "result",
            result?.DeepClone() ?? new JsonObject(),
            cancellationToken);
    }

    public Task RespondToServerRequestErrorAsync(
        RekallAgeCodexServerRequest request,
        int code,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequired(message, nameof(message));
        return WriteServerResponseAsync(
            request.Id,
            "error",
            new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            },
            cancellationToken);
    }

    public ValueTask<RekallAgeCodexDiagnostic> ReadDiagnosticAsync(CancellationToken cancellationToken = default) =>
        _diagnostics.Reader.ReadAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync(
            "initialize",
            new JsonObject
            {
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = _options.ClientName,
                    ["title"] = _options.ClientTitle,
                    ["version"] = _options.ClientVersion
                },
                ["capabilities"] = new JsonObject { ["experimentalApi"] = false }
            },
            cancellationToken);
        try
        {
            var userAgent = GetRequiredString(result, "userAgent", "initialize");
            var platformFamily = GetRequiredString(result, "platformFamily", "initialize");
            var platformOs = GetRequiredString(result, "platformOs", "initialize");
            _ = GetRequiredString(result, "codexHome", "initialize");
            InitializeResult = new RekallAgeCodexInitializeResult(userAgent, platformFamily, platformOs);
        }
        catch (RekallAgeLanguageModelProviderException error)
            when (error.Code == RekallAgeCodexErrorCodes.ProtocolInvalid)
        {
            throw ProviderError(
                RekallAgeCodexErrorCodes.ProtocolUnsupported,
                "The installed Codex App Server does not support the required protocol. Update Codex and retry.");
        }

        await WriteMessageAsync(new JsonObject { ["method"] = "initialized" }, cancellationToken);
    }

    private async Task<JsonElement> RequestAsync(
        string method,
        JsonObject parameters,
        CancellationToken cancellationToken,
        bool allowDuringDisposal = false)
    {
        PendingRequest pending;
        long id;
        lock (_lifecycleGate)
        {
            ThrowIfUnavailableLocked(cancellationToken, allowDuringDisposal);
            if (!_pendingSlots.Wait(0))
            {
                throw ProtocolInvalid("The Codex pending-request limit was reached.");
            }

            id = Interlocked.Increment(ref _nextRequestId);
            pending = new PendingRequest(method);
            if (!_pending.TryAdd(id, pending))
            {
                _pendingSlots.Release();
                throw ProtocolInvalid("Codex request identifiers could not be allocated safely.");
            }
        }

        using var cancellationRegistration = cancellationToken.Register(() => CancelPending(id));
        try
        {
            await WriteMessageAsync(
                new JsonObject
                {
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CancelPending(id);
        }
        catch (RekallAgeLanguageModelProviderException error)
        {
            CompletePending(id, error: error);
        }
        catch
        {
            CompletePending(
                id,
                error: ProviderError(
                    RekallAgeCodexErrorCodes.ProcessExited,
                    "The Codex App Server input stream closed unexpectedly."));
        }

        return await pending.Completion.Task;
    }

    private async Task WriteMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var line = message.ToJsonString(JsonOptions);
        if (line.Length > _options.MaximumJsonLineCharacters)
        {
            throw ProtocolInvalid("A Codex JSONL request exceeded the configured line bound.");
        }

        await _writer.WaitAsync(cancellationToken);
        try
        {
            await _process.StandardInput.WriteLineAsync(line);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _writer.Release();
        }
    }

    private async Task DrainStdoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reader = new BoundedLineReader(_process.StandardOutput, _options.MaximumJsonLineCharacters);
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    if (Volatile.Read(ref _disposing) == 0)
                    {
                        FailAll(ProcessOrProtocolEof());
                    }

                    return;
                }

                await DispatchInboundLineAsync(line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (RekallAgeLanguageModelProviderException error)
        {
            FailAll(error);
        }
        catch (JsonException)
        {
            FailAll(ProtocolInvalid("Codex emitted malformed JSONL protocol data."));
        }
        catch (Exception)
        {
            FailAll(ProtocolInvalid("The Codex App Server output stream failed."));
        }
    }

    private async Task DrainStderrAsync(CancellationToken cancellationToken)
    {
        var buffer = new char[4_096];
        try
        {
            while (true)
            {
                var count = await _process.StandardError.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count == 0)
                {
                    return;
                }

                Interlocked.Add(ref _stderrCharactersRead, count);
                AppendStderrChunk(buffer.AsSpan(0, count));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            RecordDiagnostic(RekallAgeCodexErrorCodes.ProcessExited, "The Codex App Server stderr stream closed unexpectedly.");
        }
        finally
        {
            FlushPendingStderr();
        }
    }

    private async Task DispatchInboundLineAsync(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw ProtocolInvalid("Codex emitted a non-object JSONL message.");
        }

        if (root.TryGetProperty("method", out var methodElement))
        {
            if (methodElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(methodElement.GetString()))
            {
                throw ProtocolInvalid("Codex emitted a protocol message with an invalid method.");
            }

            var method = methodElement.GetString()!;
            var parameters = root.TryGetProperty("params", out var parameterElement)
                ? SanitizeRetainedParameters(parameterElement)
                : EmptyObject();
            if (root.TryGetProperty("id", out var serverRequestId))
            {
                ValidateServerRequestId(serverRequestId);
                if (!_serverRequests.Writer.TryWrite(new RekallAgeCodexServerRequest(serverRequestId.Clone(), method, parameters)))
                {
                    var overflow = ProtocolInvalid("The bounded Codex server-request queue is full.");
                    try
                    {
                        await WriteServerResponseAsync(
                            serverRequestId,
                            "error",
                            new JsonObject
                            {
                                ["code"] = -32000,
                                ["message"] = "Codex server-request capacity was exceeded."
                            },
                            CancellationToken.None);
                    }
                    finally
                    {
                        FailAndBeginShutdown(overflow);
                    }
                }

                return;
            }

            if (string.Equals(method, "turn/completed", StringComparison.Ordinal))
            {
                CompleteTurn(parameters);
            }

            if (!_notifications.Writer.TryWrite(new RekallAgeCodexNotification(method, parameters)))
            {
                RecordDiagnostic(RekallAgeCodexErrorCodes.ProtocolInvalid, "The bounded Codex notification queue is full.");
            }

            return;
        }

        if (!root.TryGetProperty("id", out var responseId)
            || responseId.ValueKind != JsonValueKind.Number
            || !responseId.TryGetInt64(out var id))
        {
            throw ProtocolInvalid("Codex emitted a protocol message that is neither a response, request, nor notification.");
        }

        HandleResponse(id, root);
    }

    private Task WriteServerResponseAsync(
        JsonElement id,
        string memberName,
        JsonNode payload,
        CancellationToken cancellationToken)
    {
        ValidateServerRequestId(id);
        return WriteMessageAsync(
            new JsonObject
            {
                ["id"] = JsonNode.Parse(id.GetRawText()),
                [memberName] = payload
            },
            cancellationToken);
    }

    private static void ValidateServerRequestId(JsonElement id)
    {
        if (id.ValueKind == JsonValueKind.String)
        {
            return;
        }

        if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out _))
        {
            throw ProtocolInvalid("Codex emitted a server request with an invalid identifier.");
        }
    }

    private static JsonElement SanitizeRetainedParameters(JsonElement parameters)
    {
        var node = JsonNode.Parse(parameters.GetRawText());
        SanitizeRetainedNode(node, identityOrAuthenticationContext: false);
        return JsonSerializer.SerializeToElement(node, JsonOptions);
    }

    private static void SanitizeRetainedNode(JsonNode? node, bool identityOrAuthenticationContext)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                var normalizedName = NormalizeRetainedPropertyName(property.Key);
                if (IsSensitiveRetainedProperty(normalizedName)
                    || (identityOrAuthenticationContext && IsIdentityProperty(normalizedName)))
                {
                    jsonObject.Remove(property.Key);
                }
                else
                {
                    SanitizeRetainedNode(
                        property.Value,
                        identityOrAuthenticationContext || IsIdentityOrAuthenticationContainer(normalizedName));
                }
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                SanitizeRetainedNode(item, identityOrAuthenticationContext);
            }

            return;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var textValue))
        {
            value.ReplaceWith(SanitizeStderr(textValue));
        }
    }

    private static string NormalizeRetainedPropertyName(string propertyName) =>
        propertyName.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static bool IsSensitiveRetainedProperty(string normalizedName) => normalizedName is
        "accesstoken"
        or "accountid"
        or "apikey"
        or "authorization"
        or "bearertoken"
        or "clientsecret"
        or "credential"
        or "credentials"
        or "email"
        or "idtoken"
        or "password"
        or "passphrase"
        or "privatekey"
        or "refreshtoken"
        or "secret"
        or "sessiontoken"
        or "token"
        or "tokens"
        or "userid";

    private static bool IsIdentityOrAuthenticationContainer(string normalizedName) => normalizedName is
        "account"
        or "auth"
        or "authentication"
        or "profile";

    private static bool IsIdentityProperty(string normalizedName) => normalizedName is
        "avatar"
        or "avatarurl"
        or "displayname"
        or "id"
        or "name"
        or "phone"
        or "phonenumber"
        or "picture"
        or "username";

    private void HandleResponse(long id, JsonElement response)
    {
        PendingRequest? pending;
        lock (_lifecycleGate)
        {
            if (!_pending.TryRemove(id, out pending))
            {
                RecordDiagnostic(RekallAgeCodexErrorCodes.ProtocolInvalid, "Codex emitted a response with an unknown request identifier.");
                return;
            }

            _pendingSlots.Release();
        }

        if (response.TryGetProperty("error", out var error))
        {
            var code = error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var errorCode)
                && errorCode.ValueKind == JsonValueKind.Number
                    ? errorCode.GetRawText()
                    : "unknown";
            var stableCode = pending.Method switch
            {
                "initialize" => RekallAgeCodexErrorCodes.ProtocolUnsupported,
                "turn/start" => RekallAgeCodexErrorCodes.TurnFailed,
                _ => RekallAgeCodexErrorCodes.ProtocolInvalid
            };
            pending.Completion.TrySetException(new RekallAgeLanguageModelProviderException(
                stableCode,
                ProviderId,
                "Codex rejected an App Server request.",
                providerDetail: $"JSON-RPC error {code}."));
            return;
        }

        if (!response.TryGetProperty("result", out var result))
        {
            pending.Completion.TrySetException(ProtocolInvalid("Codex returned a response without a result or error."));
            return;
        }

        pending.Completion.TrySetResult(result.Clone());
    }

    private void CompleteTurn(JsonElement parameters)
    {
        var threadId = GetRequiredString(parameters, "threadId", "turn/completed");
        if (!parameters.TryGetProperty("turn", out var turn) || turn.ValueKind != JsonValueKind.Object)
        {
            throw ProtocolInvalid("Codex returned an invalid turn/completed notification.");
        }

        var turnId = GetRequiredString(turn, "id", "turn/completed");
        var status = GetRequiredString(turn, "status", "turn/completed");
        TaskCompletionSource<RekallAgeCodexTurnCompletion>? completion;
        var completedTurn = new RekallAgeCodexTurnCompletion(threadId, turnId, status);
        var unknownTurn = false;
        lock (_lifecycleGate)
        {
            if (_terminalError is not null)
            {
                return;
            }

            lock (_activeTurnGate)
            {
                if (_turnCompletions.TryGetValue(turnId, out completion))
                {
                    if (string.Equals(_activeTurnId, turnId, StringComparison.Ordinal))
                    {
                        _activeThreadId = null;
                        _activeTurnId = null;
                    }
                }
                else if (Volatile.Read(ref _turnStartPending) != 0 && _earlyTurnCompletions.Count == 0)
                {
                    _earlyTurnCompletions.Add(turnId, completedTurn);
                }
                else
                {
                    unknownTurn = true;
                }
            }
        }

        if (unknownTurn)
        {
            RecordDiagnostic(RekallAgeCodexErrorCodes.ProtocolInvalid, "Codex completed an unknown turn identifier.");
            return;
        }

        completion?.TrySetResult(completedTurn);
    }

    private async Task InterruptForCancellationAsync(RekallAgeCodexTurn turn)
    {
        using var timeout = new CancellationTokenSource(_options.InterruptTimeout);
        try
        {
            await RequestAsync(
                "turn/interrupt",
                new JsonObject
                {
                    ["threadId"] = turn.ThreadId,
                    ["turnId"] = turn.Id
                },
                timeout.Token,
                allowDuringDisposal: true);
        }
        catch
        {
            // Cancellation still has a stable local outcome; shutdown owns eventual process cleanup.
        }
    }

    private async Task ObserveCancelledTurnStartAsync(string threadId, Task<JsonElement> resultTask)
    {
        try
        {
            var turn = RegisterTurn(threadId, await resultTask);
            await InterruptForCancellationAsync(turn);
        }
        catch
        {
            // The caller already received the stable cancellation result. Process failure/disposal
            // owns cleanup when the late response never arrives or is invalid.
        }
        finally
        {
            Interlocked.Exchange(ref _turnStartPending, 0);
        }
    }

    private async Task DisposeCoreAsync()
    {
        lock (_lifecycleGate)
        {
            Interlocked.Exchange(ref _disposing, 1);
        }
        string? activeThread;
        string? activeTurn;
        lock (_activeTurnGate)
        {
            activeThread = _activeThreadId;
            activeTurn = _activeTurnId;
        }

        if (activeThread is not null && activeTurn is not null && !_process.HasExited)
        {
            using var interruptTimeout = new CancellationTokenSource(_options.InterruptTimeout);
            try
            {
                await RequestAsync(
                    "turn/interrupt",
                    new JsonObject
                    {
                        ["threadId"] = activeThread,
                        ["turnId"] = activeTurn
                    },
                    interruptTimeout.Token,
                    allowDuringDisposal: true);
            }
            catch
            {
            }
        }

        FailAll(ProviderError(RekallAgeCodexErrorCodes.Cancelled, "The Codex App Server client was disposed."));

        try
        {
            await _process.CloseStandardInputAsync();
        }
        catch
        {
        }

        if (!_process.HasExited)
        {
            using var shutdownTimeout = new CancellationTokenSource(_options.ShutdownTimeout);
            try
            {
                await _process.WaitForExitAsync(shutdownTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!_process.HasExited)
                {
                    try
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        _drainCancellation.Cancel();
        await SuppressAsync(_stdoutDrain);
        await SuppressAsync(_stderrDrain);
        _notifications.Writer.TryComplete();
        _serverRequests.Writer.TryComplete();
        _diagnostics.Writer.TryComplete();
        await _process.DisposeAsync();
        _writer.Dispose();
        _pendingSlots.Dispose();
        _drainCancellation.Dispose();
    }

    private void CancelPending(long id)
    {
        CompletePending(
            id,
            error: ProviderError(RekallAgeCodexErrorCodes.Cancelled, "The Codex App Server request was cancelled."));
    }

    private void CompletePending(long id, JsonElement? result = null, Exception? error = null)
    {
        PendingRequest? pending;
        lock (_lifecycleGate)
        {
            if (!_pending.TryRemove(id, out pending))
            {
                return;
            }

            _pendingSlots.Release();
        }

        if (error is not null)
        {
            pending.Completion.TrySetException(error);
        }
        else if (result is JsonElement value)
        {
            pending.Completion.TrySetResult(value);
        }
    }

    private void FailAll(RekallAgeLanguageModelProviderException error)
    {
        List<PendingRequest> pendingRequests = [];
        List<TaskCompletionSource<RekallAgeCodexTurnCompletion>> turnCompletions = [];
        RekallAgeLanguageModelProviderException terminalError;
        lock (_lifecycleGate)
        {
            _terminalError ??= error;
            terminalError = _terminalError;
            foreach (var pending in _pending.ToArray())
            {
                if (_pending.TryRemove(pending.Key, out var removed))
                {
                    _pendingSlots.Release();
                    pendingRequests.Add(removed);
                }
            }

            foreach (var turn in _turnCompletions.ToArray())
            {
                if (_turnCompletions.TryRemove(turn.Key, out var completion))
                {
                    turnCompletions.Add(completion);
                }
            }

            lock (_activeTurnGate)
            {
                _earlyTurnCompletions.Clear();
                _activeThreadId = null;
                _activeTurnId = null;
            }
        }

        foreach (var pending in pendingRequests)
        {
            pending.Completion.TrySetException(terminalError);
        }

        foreach (var completion in turnCompletions)
        {
            completion.TrySetException(terminalError);
        }
    }

    private void FailAndBeginShutdown(RekallAgeLanguageModelProviderException error)
    {
        FailAll(error);
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
        }
    }

    private void ThrowIfUnavailable(CancellationToken cancellationToken, bool allowDuringDisposal = false)
    {
        lock (_lifecycleGate)
        {
            ThrowIfUnavailableLocked(cancellationToken, allowDuringDisposal);
        }
    }

    private void ThrowIfUnavailableLocked(CancellationToken cancellationToken, bool allowDuringDisposal)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw ProviderError(RekallAgeCodexErrorCodes.Cancelled, "The Codex App Server request was cancelled.");
        }

        if (!allowDuringDisposal && Volatile.Read(ref _disposing) != 0)
        {
            throw ProviderError(RekallAgeCodexErrorCodes.Cancelled, "The Codex App Server client is shutting down.");
        }

        if (_terminalError is not null)
        {
            throw _terminalError;
        }
    }

    private RekallAgeLanguageModelProviderException ProcessOrProtocolEof()
    {
        if (_process.HasExited && _process.ExitCode is int exitCode && exitCode != 0)
        {
            return ProviderError(
                RekallAgeCodexErrorCodes.ProcessExited,
                "The Codex App Server process exited before the protocol completed.",
                providerDetail: $"Exit code {exitCode}.");
        }

        return ProtocolInvalid("The Codex App Server output ended before the protocol completed.");
    }

    private void AppendStderrChunk(ReadOnlySpan<char> characters)
    {
        lock (_stderrGate)
        {
            foreach (var character in characters)
            {
                if (character == '\n')
                {
                    FlushStderrLineLocked(includeNewline: true);
                    continue;
                }

                if (_stderrLine.Length < _options.MaximumStderrCharacters)
                {
                    _stderrLine.Append(character);
                }
                else
                {
                    _stderrLineOverflow = true;
                }
            }
        }
    }

    private void FlushPendingStderr()
    {
        lock (_stderrGate)
        {
            if (_stderrLine.Length > 0 || _stderrLineOverflow)
            {
                FlushStderrLineLocked(includeNewline: false);
            }
        }
    }

    private void FlushStderrLineLocked(bool includeNewline)
    {
        if (_stderrLineOverflow)
        {
            _stderrHistory.Append("[REDACTED OVERSIZED STDERR LINE]");
        }
        else
        {
            _stderrHistory.Append(SanitizeStderr(_stderrLine.ToString().TrimEnd('\r')));
        }

        if (includeNewline)
        {
            _stderrHistory.Append('\n');
        }

        _stderrLine.Clear();
        _stderrLineOverflow = false;
        if (_stderrHistory.Length > _options.MaximumStderrCharacters)
        {
            _stderrHistory.Remove(0, _stderrHistory.Length - _options.MaximumStderrCharacters);
        }
    }

    private static string SanitizeStderr(string value)
    {
        var sanitized = BearerTokenPattern().Replace(value, "$1[REDACTED]");
        sanitized = ApiKeyPattern().Replace(sanitized, "[REDACTED]");
        sanitized = EmailPattern().Replace(sanitized, "[REDACTED]");
        return WindowsUserPathPattern().Replace(sanitized, "$1[REDACTED]");
    }

    private void RecordDiagnostic(string code, string message) =>
        _diagnostics.Writer.TryWrite(new RekallAgeCodexDiagnostic(code, message));

    private static RekallAgeCodexModel ParseModel(JsonElement model)
    {
        if (model.ValueKind != JsonValueKind.Object
            || !TryGetBoolean(model, "hidden", out var hidden)
            || !TryGetBoolean(model, "isDefault", out var isDefault))
        {
            throw ProtocolInvalid("Codex returned an invalid model/list entry.");
        }

        var efforts = new List<RekallAgeCodexReasoningEffort>();
        if (!model.TryGetProperty("supportedReasoningEfforts", out var supported)
            || supported.ValueKind != JsonValueKind.Array)
        {
            throw ProtocolInvalid("Codex returned an invalid model/list entry.");
        }

        foreach (var effort in supported.EnumerateArray())
        {
            efforts.Add(new RekallAgeCodexReasoningEffort(
                GetRequiredString(effort, "reasoningEffort", "model/list"),
                GetRequiredString(effort, "description", "model/list")));
        }

        return new RekallAgeCodexModel(
            GetRequiredString(model, "id", "model/list"),
            GetRequiredString(model, "model", "model/list"),
            GetRequiredString(model, "displayName", "model/list"),
            hidden,
            isDefault,
            GetRequiredString(model, "defaultReasoningEffort", "model/list"),
            efforts);
    }

    private static string GetRequiredString(JsonElement element, string name, string method)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw ProtocolInvalid($"Codex returned an invalid {method} response.");
        }

        return value.GetString()!;
    }

    private static bool TryGetBoolean(JsonElement element, string name, out bool value)
    {
        value = false;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static JsonElement EmptyObject() => JsonSerializer.SerializeToElement(new { });

    private static Channel<T> CreateBoundedChannel<T>(int capacity) =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });

    private static RekallAgeLanguageModelProviderException ProtocolInvalid(string message) =>
        ProviderError(RekallAgeCodexErrorCodes.ProtocolInvalid, message);

    private static RekallAgeLanguageModelProviderException ProviderError(
        string code,
        string message,
        string? providerDetail = null) =>
        new(code, ProviderId, message, providerDetail: providerDetail);

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
    }

    private static async Task SuppressAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    [GeneratedRegex(@"(?i)(authorization\s*:\s*bearer\s+)\S+")]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"(?i)\bsk-[a-z0-9_-]{8,}\b")]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(@"(?i)\b[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}\b")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?i)([a-z]:\\users\\)[^\\\r\n]+")]
    private static partial Regex WindowsUserPathPattern();

    private sealed class PendingRequest
    {
        public PendingRequest(string method)
        {
            Method = method;
        }

        public string Method { get; }

        public TaskCompletionSource<JsonElement> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BoundedLineReader
    {
        private readonly TextReader _reader;
        private readonly int _maximumCharacters;
        private readonly char[] _buffer = new char[4_096];
        private readonly StringBuilder _line = new();
        private int _bufferIndex;
        private int _bufferCount;

        public BoundedLineReader(TextReader reader, int maximumCharacters)
        {
            _reader = reader;
            _maximumCharacters = maximumCharacters;
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            _line.Clear();
            while (true)
            {
                if (_bufferIndex == _bufferCount)
                {
                    _bufferCount = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                    _bufferIndex = 0;
                    if (_bufferCount == 0)
                    {
                        return _line.Length == 0 ? null : _line.ToString().TrimEnd('\r');
                    }
                }

                var character = _buffer[_bufferIndex++];
                if (character == '\n')
                {
                    return _line.ToString().TrimEnd('\r');
                }

                if (_line.Length == _maximumCharacters)
                {
                    throw ProtocolInvalid("Codex emitted a JSONL message larger than the configured bound.");
                }

                _line.Append(character);
            }
        }
    }
}
