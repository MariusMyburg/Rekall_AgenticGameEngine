using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Rekall.Age.Agent.Codex;

namespace Rekall.Age.Tests.Agent;

public sealed class CodexAppServerClientTests
{
    [Fact]
    public async Task InstalledV2TranscriptRoutesOutOfOrderResponsesAndSeparatesInboundKinds()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var process = new FakeCodexProcess();
        var factory = new FakeCodexProcessFactory(process);
        var options = new RekallAgeCodexAppServerOptions
        {
            ExecutablePath = "codex-test",
            ClientVersion = "1.2.3",
            ModelPageSize = 2,
            ShutdownTimeout = TimeSpan.FromMilliseconds(100),
            InterruptTimeout = TimeSpan.FromMilliseconds(100)
        };

        var startTask = RekallAgeCodexAppServerClient.StartAsync(options, factory, timeout.Token);

        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """
            {
              "id": 1,
              "method": "initialize",
              "params": {
                "clientInfo": {
                  "name": "rekall-age",
                  "title": "Rekall AGE",
                  "version": "1.2.3"
                },
                "capabilities": {
                  "experimentalApi": false
                }
              }
            }
            """);
        await process.WriteServerLineAsync(
            """
            {"id":1,"result":{"userAgent":"codex-cli/0.130.0","platformFamily":"windows","platformOs":"windows","codexHome":"C:\\bounded"}}
            """);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"method":"initialized"}""");

        await using var client = await startTask;
        var startInfo = Assert.Single(factory.StartInfos);
        Assert.Equal("codex-test", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(["app-server", "--listen", "stdio://"], startInfo.ArgumentList);

        var accountTask = client.ReadAccountAsync(cancellationToken: timeout.Token);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":2,"method":"account/read","params":{"refreshToken":false}}""");

        var modelsTask = client.ListModelsAsync(includeHidden: false, cancellationToken: timeout.Token);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":3,"method":"model/list","params":{"includeHidden":false,"limit":2}}""");

        await process.WriteServerLineAsync(
            """
            {"id":3,"result":{"data":[{"id":"model-a","model":"model-a","displayName":"Model A","description":"A","hidden":false,"isDefault":true,"defaultReasoningEffort":"medium","supportedReasoningEfforts":[{"reasoningEffort":"medium","description":"Balanced"}]}],"nextCursor":"next-page"}}
            """);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":4,"method":"model/list","params":{"cursor":"next-page","includeHidden":false,"limit":2}}""");

        await process.WriteServerLineAsync(
            """{"id":999,"result":{"ignored":true}}""");
        await process.WriteServerLineAsync(
            """{"id":2,"method":"item/commandExecution/requestApproval","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"item-1"}}""");
        await process.WriteServerLineAsync(
            """{"method":"item/started","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"item-1","type":"agentMessage","text":"bounded"}}}""");
        await process.WriteServerLineAsync(
            """{"id":2,"result":{"account":{"type":"chatgpt","email":"not-retained@example.invalid","planType":"plus"},"requiresOpenaiAuth":true}}""");
        await process.WriteServerLineAsync(
            """{"id":4,"result":{"data":[{"id":"model-b","model":"model-b","displayName":"Model B","description":"B","hidden":false,"isDefault":false,"defaultReasoningEffort":"high","supportedReasoningEfforts":[]}],"nextCursor":null}}""");

        var account = await accountTask;
        var models = await modelsTask;
        Assert.Equal("chatgpt", account.AuthenticationType);
        Assert.True(account.RequiresOpenAiAuthentication);
        Assert.DoesNotContain("not-retained@example.invalid", account.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["model-a", "model-b"], models.Select(model => model.Id));
        Assert.Equal("medium", models[0].DefaultReasoningEffort);
        Assert.Equal("REKALL_CODEX_PROTOCOL_INVALID", (await client.ReadDiagnosticAsync(timeout.Token)).Code);
        var serverRequest = await client.ReadServerRequestAsync(timeout.Token);
        Assert.Equal(2, serverRequest.Id.GetInt64());
        Assert.Equal("item/commandExecution/requestApproval", serverRequest.Method);
        var notification = await client.ReadNotificationAsync(timeout.Token);
        Assert.Equal("item/started", notification.Method);

        var projectRoot = Path.GetFullPath("codex-protocol-fixture");
        var threadTask = client.StartThreadAsync(
            new RekallAgeCodexThreadStartRequest(projectRoot, "model-a", "Author through AGE primitives.")
            {
                McpServers =
                [
                    new RekallAgeCodexMcpServer("rekall-age", "rekall-cli", ["mcp", "stdio"])
                ]
            },
            timeout.Token);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            $$"""
            {
              "id": 5,
              "method": "thread/start",
              "params": {
                "approvalPolicy": "on-request",
                "config": {
                  "mcp_servers": {
                    "rekall-age": {
                      "command": "rekall-cli",
                      "args": ["mcp", "stdio"]
                    }
                  },
                  "sandbox_workspace_write": {
                    "network_access": false,
                    "writable_roots": [{{JsonValue.Create(projectRoot)!.ToJsonString()}}]
                  }
                },
                "cwd": {{JsonValue.Create(projectRoot)!.ToJsonString()}},
                "developerInstructions": "Author through AGE primitives.",
                "model": "model-a",
                "sandbox": "workspace-write"
              }
            }
            """);
        await process.WriteServerLineAsync(
            "{\"id\":5,\"result\":{\"thread\":{\"id\":\"thread-1\"},\"model\":\"model-a\",\"cwd\":"
            + JsonValue.Create(projectRoot)!.ToJsonString()
            + ",\"modelProvider\":\"openai\",\"approvalPolicy\":\"on-request\",\"approvalsReviewer\":\"user\",\"sandbox\":{\"type\":\"workspaceWrite\"}}}");
        var thread = await threadTask;
        Assert.Equal("thread-1", thread.Id);
        Assert.Equal("model-a", thread.Model);

        var turnTask = client.StartTurnAsync(thread.Id, "Build a game.", "high", timeout.Token);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """
            {"id":6,"method":"turn/start","params":{"effort":"high","threadId":"thread-1","input":[{"type":"text","text":"Build a game."}]}}
            """);
        await process.WriteServerLineAsync(
            """{"id":6,"result":{"turn":{"id":"turn-1","status":"inProgress","items":[]}}}""");
        var turn = await turnTask;

        var interruptTask = client.InterruptTurnAsync(thread.Id, turn.Id, timeout.Token);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":7,"method":"turn/interrupt","params":{"threadId":"thread-1","turnId":"turn-1"}}""");
        await process.WriteServerLineAsync("""{"id":7,"result":{}}""");
        await interruptTask;

        var completionTask = client.WaitForTurnCompletionAsync(turn, timeout.Token);
        await process.WriteServerLineAsync(
            """{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"completed","items":[]}}}""");
        var completion = await completionTask;
        Assert.Equal("completed", completion.Status);
        Assert.Equal("turn-1", completion.TurnId);

        // A duplicate response is unknown after the original pending request has completed.
        await process.WriteServerLineAsync("""{"id":7,"result":{}}""");
        Assert.Equal("REKALL_CODEX_PROTOCOL_INVALID", (await client.ReadDiagnosticAsync(timeout.Token)).Code);
    }

    [Fact]
    public async Task PendingAndNotificationBacklogsStayWithinConfiguredBounds()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var process = new FakeCodexProcess();
        var options = new RekallAgeCodexAppServerOptions
        {
            ExecutablePath = "codex-test",
            ClientVersion = "test",
            MaximumPendingRequests = 1,
            NotificationCapacity = 1,
            ShutdownTimeout = TimeSpan.FromMilliseconds(40),
            InterruptTimeout = TimeSpan.FromMilliseconds(40)
        };
        var start = RekallAgeCodexAppServerClient.StartAsync(
            options,
            new FakeCodexProcessFactory(process),
            timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);
        await process.WriteServerLineAsync(
            """{"id":1,"result":{"userAgent":"codex-cli/0.130.0","platformFamily":"windows","platformOs":"windows","codexHome":"C:\\bounded"}}""");
        _ = await process.ReadClientLineAsync(timeout.Token);
        await using var client = await start;

        var account = client.ReadAccountAsync(cancellationToken: timeout.Token);
        _ = await process.ReadClientLineAsync(timeout.Token);
        var writesBeforeOverflow = process.ClientLinesWritten;
        var pendingError = await Assert.ThrowsAsync<Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelProviderException>(() =>
            client.ListModelsAsync(cancellationToken: timeout.Token));
        Assert.Equal(RekallAgeCodexErrorCodes.ProtocolInvalid, pendingError.Code);
        Assert.Equal(1, client.PendingRequestCount);
        Assert.Equal(writesBeforeOverflow, process.ClientLinesWritten);

        await process.WriteServerLineAsync(
            """{"method":"item/started","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"one"}}}""");
        await process.WriteServerLineAsync(
            """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"two"}}}""");
        await process.WriteServerLineAsync("""{"id":999,"result":{}}""");
        var diagnostic = await client.ReadDiagnosticAsync(timeout.Token);
        Assert.Equal(RekallAgeCodexErrorCodes.ProtocolInvalid, diagnostic.Code);
        Assert.Contains("notification queue", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("item/started", (await client.ReadNotificationAsync(timeout.Token)).Method);

        await process.WriteServerLineAsync(
            """{"id":2,"result":{"account":{"type":"apiKey"},"requiresOpenaiAuth":true}}""");
        _ = await account;
    }

    [Fact]
    public async Task ServerRequestResponsesPreserveStringAndNumericIdsThroughTheSingleWriter()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var process = new FakeCodexProcess();
        await using var client = await StartInitializedClientAsync(process, cancellationToken: timeout.Token);

        await process.WriteServerLineAsync(
            """{"id":"approval-1","method":"item/commandExecution/requestApproval","params":{"threadId":"thread-1"}}""");
        var stringRequest = await client.ReadServerRequestAsync(timeout.Token);
        await client.RespondToServerRequestAsync(
            stringRequest,
            new JsonObject { ["decision"] = "decline" },
            timeout.Token);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":"approval-1","result":{"decision":"decline"}}""");

        await process.WriteServerLineAsync(
            """{"id":17,"method":"item/fileChange/requestApproval","params":{"threadId":"thread-1"}}""");
        var numericRequest = await client.ReadServerRequestAsync(timeout.Token);
        await client.RespondToServerRequestErrorAsync(
            numericRequest,
            -32001,
            "Denied by the AGE authority boundary.",
            timeout.Token);
        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":17,"error":{"code":-32001,"message":"Denied by the AGE authority boundary."}}""");
    }

    [Fact]
    public async Task ServerRequestOverflowDeniesTheUnqueuedRequestAndShutsDownTheOwnedProcess()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var process = new FakeCodexProcess();
        var options = new RekallAgeCodexAppServerOptions
        {
            ExecutablePath = "codex-test",
            ClientVersion = "test",
            ServerRequestCapacity = 1,
            ShutdownTimeout = TimeSpan.FromMilliseconds(40),
            InterruptTimeout = TimeSpan.FromMilliseconds(40)
        };
        var client = await StartInitializedClientAsync(process, options, timeout.Token);

        await process.WriteServerLineAsync(
            """{"id":"kept","method":"item/commandExecution/requestApproval","params":{"threadId":"thread-1"}}""");
        await process.WriteServerLineAsync(
            """{"id":"overflow","method":"item/fileChange/requestApproval","params":{"threadId":"thread-1"}}""");

        AssertJson(
            await process.ReadClientLineAsync(timeout.Token),
            """{"id":"overflow","error":{"code":-32000,"message":"Codex server-request capacity was exceeded."}}""");
        await client.DisposeAsync();
        Assert.Equal(1, process.InputCloseCount);
        Assert.Equal(1, process.KillCount);
        Assert.True(process.LastKillEntireProcessTree);
    }

    [Fact]
    public async Task RetainedNotificationsAndServerRequestsRedactCredentialAndAccountFields()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var process = new FakeCodexProcess();
        var options = new RekallAgeCodexAppServerOptions
        {
            ExecutablePath = "codex-test",
            ClientVersion = "test",
            NotificationCapacity = 1,
            ShutdownTimeout = TimeSpan.FromMilliseconds(40),
            InterruptTimeout = TimeSpan.FromMilliseconds(40)
        };
        await using var client = await StartInitializedClientAsync(process, options, timeout.Token);

        await process.WriteServerLineAsync(
            """{"method":"account/updated","params":{"email":"person@example.invalid","accessToken":"token-value","nested":{"Authorization":"Bearer private-material"},"threadId":"thread-1"}}""");
        await process.WriteServerLineAsync(
            """{"method":"account/updated","params":{"password":"second-private-value","threadId":"thread-1"}}""");
        await process.WriteServerLineAsync(
            """{"id":"approval","method":"item/commandExecution/requestApproval","params":{"apiKey":"sk-private","credential":{"secret":"hidden"},"threadId":"thread-1"}}""");
        await process.WriteServerLineAsync("""{"id":999,"result":{}}""");

        var diagnostic = await client.ReadDiagnosticAsync(timeout.Token);
        var notification = await client.ReadNotificationAsync(timeout.Token);
        var serverRequest = await client.ReadServerRequestAsync(timeout.Token);
        var retained = notification.Params.GetRawText() + serverRequest.Params.GetRawText() + diagnostic.Message;
        Assert.Contains("thread-1", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.invalid", retained, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-value", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("private-material", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-private", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("second-private-value", retained, StringComparison.Ordinal);
    }

    private static async Task<RekallAgeCodexAppServerClient> StartInitializedClientAsync(
        FakeCodexProcess process,
        RekallAgeCodexAppServerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RekallAgeCodexAppServerOptions
        {
            ExecutablePath = "codex-test",
            ClientVersion = "test",
            ShutdownTimeout = TimeSpan.FromMilliseconds(40),
            InterruptTimeout = TimeSpan.FromMilliseconds(40)
        };
        var start = RekallAgeCodexAppServerClient.StartAsync(
            options,
            new FakeCodexProcessFactory(process),
            cancellationToken);
        _ = await process.ReadClientLineAsync(cancellationToken);
        await process.WriteServerLineAsync(
            """{"id":1,"result":{"userAgent":"codex-cli/0.130.0","platformFamily":"windows","platformOs":"windows","codexHome":"C:\\bounded"}}""");
        _ = await process.ReadClientLineAsync(cancellationToken);
        return await start;
    }

    private static void AssertJson(string actual, string expected)
    {
        var actualNode = JsonNode.Parse(actual);
        var expectedNode = JsonNode.Parse(expected);
        Assert.True(
            JsonNode.DeepEquals(expectedNode, actualNode),
            $"Expected JSON:{Environment.NewLine}{expectedNode}{Environment.NewLine}Actual JSON:{Environment.NewLine}{actualNode}");
    }
}

internal sealed class FakeCodexProcessFactory : IRekallAgeCodexProcessFactory
{
    private readonly IRekallAgeCodexProcess? _process;
    private readonly Exception? _exception;

    public FakeCodexProcessFactory(IRekallAgeCodexProcess process)
    {
        _process = process;
    }

    public FakeCodexProcessFactory(Exception exception)
    {
        _exception = exception;
    }

    public List<ProcessStartInfo> StartInfos { get; } = [];

    public IRekallAgeCodexProcess Start(ProcessStartInfo startInfo)
    {
        StartInfos.Add(startInfo);
        if (_exception is not null)
        {
            throw _exception;
        }

        return _process!;
    }
}

internal sealed class FakeCodexProcess : IRekallAgeCodexProcess
{
    private readonly FeedableTextReader _stdout = new();
    private readonly FeedableTextReader _stderr = new();
    private readonly RecordingLineTextWriter _stdin = new();
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeCount;
    private int _inputCloseCount;
    private int _killCount;

    public TextReader StandardOutput => _stdout;

    public TextReader StandardError => _stderr;

    public TextWriter StandardInput => _stdin;

    public bool HasExited => _exit.Task.IsCompletedSuccessfully;

    public int? ExitCode => HasExited ? _exit.Task.Result : null;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public int InputCloseCount => Volatile.Read(ref _inputCloseCount);

    public int KillCount => Volatile.Read(ref _killCount);

    public int ClientLinesWritten => _stdin.LinesWritten;

    public bool? LastKillEntireProcessTree { get; private set; }

    public ConcurrentQueue<string> LifecycleEvents { get; } = new();

    public Task<string> ReadClientLineAsync(CancellationToken cancellationToken) =>
        _stdin.ReadLineAsync(cancellationToken);

    public ValueTask WriteServerLineAsync(string line) => _stdout.WriteAsync(line + "\n");

    public ValueTask WriteServerRawAsync(string text) => _stdout.WriteAsync(text);

    public ValueTask WriteStderrAsync(string text) => _stderr.WriteAsync(text);

    public void CompleteStdout() => _stdout.Complete();

    public void CompleteStderr() => _stderr.Complete();

    public void Exit(int exitCode)
    {
        LifecycleEvents.Enqueue($"exit:{exitCode}");
        _exit.TrySetResult(exitCode);
        _stdout.Complete();
        _stderr.Complete();
    }

    public ValueTask CloseStandardInputAsync()
    {
        if (Interlocked.Increment(ref _inputCloseCount) == 1)
        {
            LifecycleEvents.Enqueue("close-stdin");
            _stdin.Complete();
        }

        return ValueTask.CompletedTask;
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        LifecycleEvents.Enqueue("wait-for-exit");
        await _exit.Task.WaitAsync(cancellationToken);
    }

    public void Kill(bool entireProcessTree)
    {
        Interlocked.Increment(ref _killCount);
        LastKillEntireProcessTree = entireProcessTree;
        LifecycleEvents.Enqueue($"kill:{entireProcessTree}");
        Exit(-1);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposeCount) == 1)
        {
            LifecycleEvents.Enqueue("dispose-process");
            _stdout.Complete();
            _stderr.Complete();
            _stdin.Complete();
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class FeedableTextReader : TextReader
{
    private readonly Channel<string> _chunks = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private string? _current;
    private int _currentIndex;

    public ValueTask WriteAsync(string text) => _chunks.Writer.WriteAsync(text);

    public void Complete() => _chunks.Writer.TryComplete();

    public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
    {
        while (_current is null || _currentIndex == _current.Length)
        {
            if (!await _chunks.Reader.WaitToReadAsync(cancellationToken))
            {
                return 0;
            }

            if (!_chunks.Reader.TryRead(out _current))
            {
                continue;
            }

            _currentIndex = 0;
        }

        var count = Math.Min(buffer.Length, _current.Length - _currentIndex);
        _current.AsMemory(_currentIndex, count).CopyTo(buffer);
        _currentIndex += count;
        return count;
    }
}

internal sealed class RecordingLineTextWriter : TextWriter
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly StringBuilder _current = new();
    private readonly object _gate = new();
    private int _linesWritten;

    public override Encoding Encoding => Encoding.UTF8;

    public int LinesWritten => Volatile.Read(ref _linesWritten);

    public Task<string> ReadLineAsync(CancellationToken cancellationToken) =>
        _lines.Reader.ReadAsync(cancellationToken).AsTask();

    public void Complete() => _lines.Writer.TryComplete();

    public override Task WriteAsync(char value)
    {
        WriteCharacters(value.ToString());
        return Task.CompletedTask;
    }

    public override Task WriteAsync(string? value)
    {
        if (value is not null)
        {
            WriteCharacters(value);
        }

        return Task.CompletedTask;
    }

    public override Task WriteLineAsync(string? value)
    {
        WriteCharacters((value ?? string.Empty) + "\n");
        return Task.CompletedTask;
    }

    private void WriteCharacters(string value)
    {
        lock (_gate)
        {
            foreach (var character in value)
            {
                if (character == '\n')
                {
                    var line = _current.ToString().TrimEnd('\r');
                    _current.Clear();
                    Interlocked.Increment(ref _linesWritten);
                    _lines.Writer.TryWrite(line);
                }
                else
                {
                    _current.Append(character);
                }
            }
        }
    }
}
