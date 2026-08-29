using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class KimiLanguageModelClientTests
{
    [Fact]
    public async Task ListsOfficialModelsWithBearerAuthentication()
    {
        const string credential = "private-kimi-test-key";
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.moonshot.ai/v1/models", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(credential, request.Headers.Authorization?.Parameter);
            return JsonResponse("""
                {"object":"list","data":[
                  {"id":"kimi-k3","context_length":1048576,"supports_reasoning":true},
                  {"id":"kimi-k2.7-code","context_length":262144,"supports_reasoning":true}
                ]}
                """);
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeKimiLanguageModelClient(http, credential);

        var models = await client.ListModelsAsync(CancellationToken.None);

        Assert.Equal(["kimi-k2.7-code", "kimi-k3"], models.Select(model => model.Id));
        Assert.All(models, model => Assert.True(model.SupportsTools));
        Assert.DoesNotContain(credential, client.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatMapsMultiTurnToolCallsAndPreservedReasoning()
    {
        JsonObject? captured = null;
        var handler = new RecordingHandler(async request =>
        {
            captured = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            return JsonResponse("""
                {
                  "id":"cmpl-1",
                  "model":"kimi-k3",
                  "choices":[{"message":{"role":"assistant","content":"done","reasoning_content":"bounded reasoning","tool_calls":[{"id":"call-2","type":"function","function":{"name":"rekall.scene.inspect","arguments":"{\"sceneName\":\"Main\"}"}}]},"finish_reason":"tool_calls"}],
                  "usage":{"prompt_tokens":11,"completion_tokens":7,"cached_tokens":3}
                }
                """);
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeKimiLanguageModelClient(http, "test-key");
        var previousAssistantState = new RekallAgeLanguageModelOpaqueState(
            "kimi",
            ["{\"reasoning_content\":\"previous reasoning\"}"]);

        var response = await client.ChatAsync(
            new RekallAgeLanguageModelRequest(
                "kimi-k3",
                [
                    new RekallAgeLanguageModelMessage("user", "inspect"),
                    new RekallAgeLanguageModelMessage(
                        "assistant",
                        string.Empty,
                        ToolCalls:
                        [
                            new RekallAgeLanguageModelToolCall(
                                "rekall.tools.execute",
                                new JsonObject { ["name"] = "rekall.scene.inspect" }) { Id = "call-1" }
                        ]) { OpaqueProviderState = previousAssistantState },
                    new RekallAgeLanguageModelMessage("tool", "{\"ok\":true}", "rekall.tools.execute")
                    {
                        ToolCallId = "call-1"
                    }
                ],
                [
                    new RekallAgeLanguageModelTool(
                        "rekall.scene.inspect",
                        "Inspect a scene.",
                        new JsonObject { ["type"] = "object" })
                ])
            {
                Think = "medium",
                Temperature = 0.2,
                MaxOutputTokens = 2048
            },
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("kimi-k3", captured!["model"]!.GetValue<string>());
        Assert.False(captured["stream"]!.GetValue<bool>());
        Assert.Equal(2048, captured["max_completion_tokens"]!.GetValue<int>());
        Assert.Equal("max", captured["reasoning_effort"]!.GetValue<string>());
        Assert.False(captured.ContainsKey("temperature"));
        var messages = captured["messages"]!.AsArray();
        Assert.Equal("previous reasoning", messages[1]!["reasoning_content"]!.GetValue<string>());
        Assert.Equal("call-1", messages[2]!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("rekall.tools.execute", messages[2]!["name"]!.GetValue<string>());
        Assert.Equal("call-1", messages[1]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("kimi", response.ProviderId);
        Assert.Equal("cmpl-1", response.ResponseId);
        Assert.Equal("bounded reasoning", response.Thinking);
        Assert.Equal(3, response.Usage.CachedInputTokens);
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("call-2", call.Id);
        Assert.Equal("Main", call.Arguments["sceneName"]!.GetValue<string>());
        Assert.Equal("kimi", response.OpaqueProviderState?.ProviderId);
    }

    [Fact]
    public async Task ProviderFailureNeverExposesCredentialResponseBodyOrRequestContent()
    {
        const string credential = "private-kimi-key";
        const string privatePrompt = "private-user-prompt";
        const string providerBody = "upstream-private-error";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent($"{{\"error\":{{\"message\":\"{providerBody}\"}}}}", Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeKimiLanguageModelClient(http, credential);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ChatAsync(
                new RekallAgeLanguageModelRequest(
                    "kimi-k3",
                    [new RekallAgeLanguageModelMessage("user", privatePrompt)],
                    []),
                CancellationToken.None).AsTask());

        Assert.Equal("kimi", error.ProviderId);
        Assert.Equal(401, error.HttpStatus);
        Assert.DoesNotContain(credential, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(privatePrompt, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(providerBody, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyToolsAreOmittedAndDisabledK2ThinkingOmitsKeep()
    {
        JsonObject? captured = null;
        var handler = new RecordingHandler(async request =>
        {
            captured = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            return JsonResponse("""
                {"id":"cmpl-2","model":"kimi-k2.6","choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{}}
                """);
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeKimiLanguageModelClient(http, "test-key");

        await client.ChatAsync(
            new RekallAgeLanguageModelRequest(
                "kimi-k2.6",
                [new RekallAgeLanguageModelMessage("user", "hello")],
                []) { Think = "none", Temperature = 0.2 },
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("tools"));
        Assert.False(captured.ContainsKey("temperature"));
        var thinking = captured["thinking"]!.AsObject();
        Assert.Equal("disabled", thinking["type"]!.GetValue<string>());
        Assert.False(thinking.ContainsKey("keep"));
    }

    [Fact]
    public async Task ChatRetriesTransientResponsesHonorsBoundedRetryAfterAndRecreatesRequests()
    {
        var requests = new List<HttpRequestMessage>();
        var contents = new List<HttpContent?>();
        var delays = new List<TimeSpan>();
        var attempts = 0;
        var handler = new RecordingHandler(async request =>
        {
            requests.Add(request);
            contents.Add(request.Content);
            _ = await request.Content!.ReadAsStringAsync();
            attempts++;
            if (attempts == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(3));
                return response;
            }
            if (attempts == 2) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return JsonResponse("""
                {"id":"retry-ok","model":"kimi-k3","choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{}}
                """);
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeKimiLanguageModelClient(
            http,
            "test-key",
            null,
            (delay, _) =>
            {
                delays.Add(delay);
                return ValueTask.CompletedTask;
            });

        var response = await client.ChatAsync(
            new RekallAgeLanguageModelRequest(
                "kimi-k3",
                [new RekallAgeLanguageModelMessage("user", "hello")],
                []),
            CancellationToken.None);

        Assert.Equal("retry-ok", response.ResponseId);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delays.Count);
        Assert.Equal(TimeSpan.FromSeconds(5), delays[0]);
        Assert.InRange(delays[1], TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5));
        Assert.Equal(3, requests.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(3, contents.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public async Task ModelDiscoveryNormalizesExhaustedTransportRetriesWithoutLeakingDetails()
    {
        const string transportSecret = "private-transport-detail";
        var attempts = 0;
        var handler = new RecordingHandler((Func<HttpRequestMessage, HttpResponseMessage>)(_ =>
        {
            attempts++;
            throw new HttpRequestException($"Connection failed: {transportSecret}");
        }));
        using var http = new HttpClient(handler);
        var client = new RekallAgeKimiLanguageModelClient(
            http,
            "test-key",
            null,
            (_, _) => ValueTask.CompletedTask);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ListModelsAsync(CancellationToken.None).AsTask());

        Assert.Equal(3, attempts);
        Assert.Equal("REKALL_KIMI_UNAVAILABLE", error.Code);
        Assert.True(error.Retryable);
        Assert.DoesNotContain(transportSecret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryDelayObservesCancellationWithoutStartingAnotherAttempt()
    {
        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.RequestTimeout);
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeKimiLanguageModelClient(
            http,
            "test-key",
            null,
            (delay, cancellationToken) => new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ListModelsAsync(cancellation.Token).AsTask());

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task HttpClientTimeoutRetriesAndNormalizesWithoutBeingMistakenForCallerCancellation()
    {
        const string transportSecret = "private-timeout-detail";
        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            return Task.FromException<HttpResponseMessage>(new TaskCanceledException(transportSecret));
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeKimiLanguageModelClient(
            http,
            "test-key",
            null,
            (_, _) => ValueTask.CompletedTask);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ListModelsAsync(CancellationToken.None).AsTask());

        Assert.Equal(3, attempts);
        Assert.Equal("REKALL_KIMI_UNAVAILABLE", error.Code);
        Assert.True(error.Retryable);
        Assert.DoesNotContain(transportSecret, error.ToString(), StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
