using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Rekall.Age.Tests.Cli;

public sealed class AgentCliTests
{
    [Fact]
    public async Task ProviderCommandsExposeBothProvidersAndFailClosedWithoutLeakingSessionCredentials()
    {
        const string sessionKey = "session-key-must-not-appear";
        var cli = FindCliAssemblyPath();

        var providers = await RunAsync(cli, null, "agent", "providers");
        var missingAuth = await RunAsync(
            cli,
            new Dictionary<string, string> { ["OPENAI_API_KEY"] = string.Empty },
            "agent", "models", "openai");
        var unsupportedModel = await RunAsync(
            cli,
            new Dictionary<string, string> { ["OPENAI_API_KEY"] = sessionKey },
            "agent", "run", "openai", "not-gpt-5.6-sol", "inspect the project", "1");
        var unsupportedProvider = await RunAsync(
            cli,
            null,
            "agent", "models", "missing-provider");

        Assert.Equal(0, providers.ExitCode);
        Assert.Contains("ollama\tOllama\tqwen3.5:35b\tnone", providers.Output, StringComparison.Ordinal);
        Assert.Contains("openai\tOpenAI\tgpt-5.6-sol\tapi-key", providers.Output, StringComparison.Ordinal);
        Assert.Equal(1, missingAuth.ExitCode);
        Assert.Contains("REKALL_OPENAI_API_KEY_MISSING", missingAuth.Output, StringComparison.Ordinal);
        Assert.Equal(1, unsupportedModel.ExitCode);
        Assert.Contains("REKALL_OPENAI_MODEL_UNSUPPORTED", unsupportedModel.Output, StringComparison.Ordinal);
        Assert.Contains("Requested: not-gpt-5.6-sol", unsupportedModel.Output, StringComparison.Ordinal);
        Assert.Contains("Resolved: gpt-5.6-sol", unsupportedModel.Output, StringComparison.Ordinal);
        Assert.Equal(1, unsupportedProvider.ExitCode);
        Assert.Contains("REKALL_LANGUAGE_MODEL_PROVIDER_UNSUPPORTED", unsupportedProvider.Output, StringComparison.Ordinal);
        Assert.Contains("Requested: missing-provider", unsupportedProvider.Output, StringComparison.Ordinal);
        Assert.Contains("Resolved: ollama,openai", unsupportedProvider.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionKey, providers.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionKey, missingAuth.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionKey, unsupportedModel.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiRunAndRunProjectUseTheProviderLeaseAndRenderProviderAndToolFacts()
    {
        const string sessionKey = "session-key-must-not-appear";
        var environment = new Dictionary<string, string> { ["OPENAI_API_KEY"] = sessionKey };
        await using var runServer = await FakeOpenAiResponsesServer.StartAsync(finalAfterAudit: true);
        environment["REKALL_AGE_OPENAI_URL"] = runServer.BaseUrl;

        var run = await RunAsync(
            FindCliAssemblyPath(),
            environment,
            "agent", "run", "openai", "gpt-5.6-sol", "inspect the tool catalog", "3");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("Provider: openai", run.Output, StringComparison.Ordinal);
        Assert.Contains("Tool execution trace:", run.Output, StringComparison.Ordinal);
        Assert.Contains("tools=1", run.Output, StringComparison.Ordinal);
        Assert.True(runServer.RequestCount >= 3);
        Assert.DoesNotContain(sessionKey, run.Output, StringComparison.Ordinal);

        var projectRoot = TestPaths.CreateTempDirectory();
        await using var projectServer = await FakeOpenAiResponsesServer.StartAsync(finalAfterAudit: false);
        environment["REKALL_AGE_OPENAI_URL"] = projectServer.BaseUrl;
        var project = await RunAsync(
            FindCliAssemblyPath(),
            environment,
            "agent", "run-project", "openai", "gpt-5.6-sol", projectRoot, "Main", "inspect the tool catalog", "1");

        Assert.NotEqual(2, project.ExitCode);
        Assert.Contains("Provider: openai", project.Output, StringComparison.Ordinal);
        Assert.Contains("Tool execution trace:", project.Output, StringComparison.Ordinal);
        Assert.Equal(1, projectServer.RequestCount);
        Assert.DoesNotContain(sessionKey, project.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledOpenAiCliCommandRendersAStableCancellationResult()
    {
        var priorKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var priorUrl = Environment.GetEnvironmentVariable("REKALL_AGE_OPENAI_URL");
        var originalError = Console.Error;
        var output = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "session-key-must-not-appear");
            Environment.SetEnvironmentVariable("REKALL_AGE_OPENAI_URL", "http://127.0.0.1:1/v1/");
            Console.SetError(output);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var exitCode = await InvokeCliAsync(["agent", "models", "openai"], cancellation.Token);

            Assert.Equal(1, exitCode);
            Assert.Contains("REKALL_LANGUAGE_MODEL_CANCELLED", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Unexpected error", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", priorKey);
            Environment.SetEnvironmentVariable("REKALL_AGE_OPENAI_URL", priorUrl);
        }
    }

    [Fact]
    public async Task CancelledNonAgentCommandDoesNotRenderALanguageModelCancellation()
    {
        var originalError = Console.Error;
        var output = new StringWriter();
        try
        {
            Console.SetError(output);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var exitCode = await InvokeCliAsync(
                ["context", "summary", TestPaths.CreateTempDirectory()],
                cancellation.Token);

            Assert.Equal(1, exitCode);
            Assert.DoesNotContain("REKALL_LANGUAGE_MODEL_CANCELLED", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Unexpected error", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void InternalAgentCancellationWithoutCallerCancellationDoesNotQualifyForLanguageModelCancellation()
    {
        var assembly = Assembly.LoadFrom(FindCliAssemblyPath());
        var method = assembly.GetType("RekallAgeCli", throwOnError: true)!.GetMethod(
            "IsUserRequestedLanguageModelCancellation",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        var qualifies = (bool)method.Invoke(
            null,
            [new[] { "agent", "models", "openai" }, CancellationToken.None])!;

        Assert.False(qualifies);
    }

    [Fact]
    public async Task ExistingOllamaAgentFormsStillReachTheirOriginalArgumentValidation()
    {
        var result = await RunAsync(
            FindCliAssemblyPath(),
            null,
            "agent", "run-project", "ollama", "qwen3.5:35b", ".", "Main", "inspect", "zero");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Invalid maximum turn count 'zero'.", result.Output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string cliAssembly,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepositoryRoot()
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        startInfo.ArgumentList.Add(cliAssembly);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }

    private static async Task<int> InvokeCliAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var assembly = Assembly.LoadFrom(FindCliAssemblyPath());
        var method = assembly.GetType("RekallAgeCli", throwOnError: true)!.GetMethod(
            "RunAsync",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        return await (Task<int>)method.Invoke(null, [arguments, cancellationToken])!;
    }

    private sealed class FakeOpenAiResponsesServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly bool _finalAfterAudit;
        private readonly Task _loop;
        private int _requestCount;

        private FakeOpenAiResponsesServer(HttpListener listener, bool finalAfterAudit)
        {
            _listener = listener;
            _finalAfterAudit = finalAfterAudit;
            _loop = RunAsync();
        }

        public string BaseUrl => _listener.Prefixes.Single();

        public int RequestCount => Volatile.Read(ref _requestCount);

        public static Task<FakeOpenAiResponsesServer> StartAsync(bool finalAfterAudit)
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/v1/");
            listener.Start();
            return Task.FromResult(new FakeOpenAiResponsesServer(listener, finalAfterAudit));
        }

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            _listener.Close();
            try { await _loop; }
            catch (OperationCanceledException) { }
            _cancellation.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    await HandleAsync(context);
                }
            }
            catch (HttpListenerException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            if (context.Request.HttpMethod != "POST" || context.Request.Url?.AbsolutePath != "/v1/responses")
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var payload = JsonNode.Parse(await reader.ReadToEndAsync())!.AsObject();
            var sequence = Interlocked.Increment(ref _requestCount);
            var response = sequence == 1 || !_finalAfterAudit
                ? ToolCall(payload, sequence)
                : Message(sequence == 2 ? "Audit evidence recorded." : "Final evidence recorded.", sequence);
            var stream = payload["stream"]?.GetValue<bool>() == true;
            var eventEnvelope = new JsonObject
            {
                ["type"] = "response.completed",
                ["response"] = response
            };
            var body = stream
                ? $"data: {eventEnvelope.ToJsonString()}\n\n"
                : response.ToJsonString();
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = stream ? "text/event-stream" : "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        private static JsonObject ToolCall(JsonObject payload, int sequence)
        {
            var toolName = payload["tools"]!.AsArray()[0]!["name"]!.GetValue<string>();
            return Response(sequence, new JsonObject
            {
                ["type"] = "function_call",
                ["call_id"] = $"call_{sequence}",
                ["name"] = toolName,
                ["arguments"] = "{\"query\":\"scene\"}"
            });
        }

        private static JsonObject Message(string text, int sequence) => Response(sequence, new JsonObject
        {
            ["type"] = "message",
            ["role"] = "assistant",
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "output_text", ["text"] = text }
            }
        });

        private static JsonObject Response(int sequence, JsonObject output) => new()
        {
            ["id"] = $"resp_{sequence}",
            ["model"] = "gpt-5.6-sol",
            ["status"] = "completed",
            ["output"] = new JsonArray(output),
            ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 }
        };
    }

    private static string FindCliAssemblyPath() =>
        Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Rekall.Age.Cli",
            "bin",
            "Debug",
            "net10.0",
            "Rekall.Age.Cli.dll");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
