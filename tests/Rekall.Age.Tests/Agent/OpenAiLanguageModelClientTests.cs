using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class OpenAiLanguageModelClientTests
{
    [Fact]
    public void DefaultEndpointIsTheOpenAiV1ApiWithExactlyOneTrailingSlash()
    {
        using var httpClient = new HttpClient();
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        Assert.Equal(new Uri("https://api.openai.com/v1/"), client.BaseUri);
    }

    [Fact]
    public void CustomHttpsEndpointNormalizesExactlyOneTrailingSlash()
    {
        using var httpClient = new HttpClient();
        var client = new RekallAgeOpenAiLanguageModelClient(
            httpClient,
            "test-api-key",
            new Uri("https://gateway.example.test/openai/v1///"));

        Assert.Equal(new Uri("https://gateway.example.test/openai/v1/"), client.BaseUri);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/v1")]
    [InlineData("http://localhost:8080/v1/")]
    [InlineData("http://[::1]:8080/v1")]
    public void LoopbackHttpEndpointIsAllowed(string endpoint)
    {
        using var httpClient = new HttpClient();
        var client = new RekallAgeOpenAiLanguageModelClient(
            httpClient,
            "test-api-key",
            new Uri(endpoint));

        Assert.EndsWith("/", client.BaseUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://api.openai.example/v1")]
    [InlineData("ftp://localhost/v1")]
    public void InsecureOrUnsupportedRemoteEndpointIsRejected(string endpoint)
    {
        using var httpClient = new HttpClient();

        var error = Assert.Throws<ArgumentException>(() =>
            new RekallAgeOpenAiLanguageModelClient(
                httpClient,
                "test-api-key",
                new Uri(endpoint)));

        Assert.Equal("baseUri", error.ParamName);
    }

    [Fact]
    public async Task ListModelsUsesAuthorizedModelsEndpointAndReturnsSortedZeroSizeEntries()
    {
        string? authorizationScheme = null;
        string? authorizationParameter = null;
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(new Uri("https://api.openai.com/v1/models"), request.RequestUri);
            authorizationScheme = request.Headers.Authorization?.Scheme;
            authorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(JsonResponse(
                """{"object":"list","data":[{"id":"z-model"},{"id":"gpt-5.6-sol"},{"id":"a-model"}]}"""));
        });
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        var models = await client.ListModelsAsync(CancellationToken.None);

        Assert.Equal("Bearer", authorizationScheme);
        Assert.Equal("test-api-key", authorizationParameter);
        Assert.Equal(["a-model", "gpt-5.6-sol", "z-model"], models.Select(model => model.Id));
        Assert.All(models, model => Assert.Equal(0, model.SizeBytes));
    }

    [Fact]
    public async Task ChatBuildsExactResponsesPayloadWithOrderedInputsAliasesAndCallIds()
    {
        const string canonicalToolName = "rekall.context.engine_status";
        string? requestJson = null;
        string? authorizationScheme = null;
        string? authorizationParameter = null;
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri("https://api.openai.com/v1/responses"), request.RequestUri);
            authorizationScheme = request.Headers.Authorization?.Scheme;
            authorizationParameter = request.Headers.Authorization?.Parameter;
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(CompletedResponseJson());
        });
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");
        var request = new RekallAgeLanguageModelRequest(
            "gpt-5.6-sol",
            [
                new RekallAgeLanguageModelMessage("system", "Follow AGE policy."),
                new RekallAgeLanguageModelMessage("developer", "Preserve evidence."),
                new RekallAgeLanguageModelMessage("user", "Inspect the engine."),
                new RekallAgeLanguageModelMessage(
                    "assistant",
                    "I will inspect.",
                    ToolCalls:
                    [
                        new RekallAgeLanguageModelToolCall(
                            canonicalToolName,
                            new JsonObject { ["detail"] = true })
                        {
                            Id = "call_123"
                        }
                    ]),
                new RekallAgeLanguageModelMessage(
                    "tool",
                    "{\"ready\":true}",
                    canonicalToolName)
                {
                    ToolCallId = "call_123"
                }
            ],
            [
                new RekallAgeLanguageModelTool(
                    canonicalToolName,
                    "Inspect engine status.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["detail"] = new JsonObject { ["type"] = "boolean" }
                        }
                    })
            ])
        {
            Think = "xhigh",
            ContextWindowTokens = 1_050_000,
            MaxOutputTokens = 8_192
        };

        await client.ChatAsync(request, CancellationToken.None);

        Assert.Equal("Bearer", authorizationScheme);
        Assert.Equal("test-api-key", authorizationParameter);
        var sent = JsonNode.Parse(requestJson!)!.AsObject();
        Assert.Equal("gpt-5.6-sol", sent["model"]!.GetValue<string>());
        Assert.False(sent["stream"]!.GetValue<bool>());
        Assert.False(sent["store"]!.GetValue<bool>());
        Assert.Equal(8_192, sent["max_output_tokens"]!.GetValue<int>());
        Assert.Equal("xhigh", sent["reasoning"]!["effort"]!.GetValue<string>());
        Assert.Equal("auto", sent["reasoning"]!["summary"]!.GetValue<string>());
        Assert.Null(sent["context_window"]);
        Assert.Null(sent["context_window_tokens"]);
        Assert.Null(sent["num_ctx"]);

        var tools = sent["tools"]!.AsArray();
        var tool = Assert.IsType<JsonObject>(Assert.Single(tools));
        Assert.Equal("function", tool["type"]!.GetValue<string>());
        Assert.Equal("rekall_context_engine_status_8179b61222fc", tool["name"]!.GetValue<string>());
        Assert.Contains(canonicalToolName, tool["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("object", tool["parameters"]!["type"]!.GetValue<string>());

        var input = sent["input"]!.AsArray();
        Assert.Equal(6, input.Count);
        AssertMessage(input[0], "developer", "Follow AGE policy.");
        AssertMessage(input[1], "developer", "Preserve evidence.");
        AssertMessage(input[2], "user", "Inspect the engine.");
        AssertMessage(input[3], "assistant", "I will inspect.");
        Assert.Equal("function_call", input[4]!["type"]!.GetValue<string>());
        Assert.Equal("call_123", input[4]!["call_id"]!.GetValue<string>());
        Assert.Equal("rekall_context_engine_status_8179b61222fc", input[4]!["name"]!.GetValue<string>());
        Assert.True(JsonNode.Parse(input[4]!["arguments"]!.GetValue<string>())!["detail"]!.GetValue<bool>());
        Assert.Equal("function_call_output", input[5]!["type"]!.GetValue<string>());
        Assert.Equal("call_123", input[5]!["call_id"]!.GetValue<string>());
        Assert.Equal("{\"ready\":true}", input[5]!["output"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("xhigh")]
    [InlineData("max")]
    public async Task SupportedReasoningEffortMapsWithoutFallback(string effort)
    {
        string? requestJson = null;
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(CompletedResponseJson());
        });
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        await client.ChatAsync(
            new RekallAgeLanguageModelRequest("gpt-5.6-sol", [], []) { Think = effort },
            CancellationToken.None);

        Assert.Equal(
            effort,
            JsonNode.Parse(requestJson!)!["reasoning"]!["effort"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnsupportedReasoningEffortIsRejectedBeforeHttp()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(CompletedResponseJson())));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ChatAsync(
                new RekallAgeLanguageModelRequest("gpt-5.6-sol", [], []) { Think = "true" },
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_OPENAI_REASONING_EFFORT_UNSUPPORTED", error.Code);
        Assert.Equal("true", error.RequestedValue);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AnyModelOtherThanExactSolIsRejectedBeforeHttp()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(CompletedResponseJson())));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ChatAsync(
                new RekallAgeLanguageModelRequest("gpt-5.6", [], []),
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_OPENAI_MODEL_UNSUPPORTED", error.Code);
        Assert.Equal("gpt-5.6", error.RequestedValue);
        Assert.Equal("gpt-5.6-sol", error.ResolvedValue);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DuplicateCanonicalToolNamesAreRejectedBeforeHttp()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(CompletedResponseJson())));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");
        var duplicate = new RekallAgeLanguageModelTool(
            "rekall.scene.inspect",
            "Inspect.",
            new JsonObject { ["type"] = "object" });

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ChatAsync(
                new RekallAgeLanguageModelRequest("gpt-5.6-sol", [], [duplicate, duplicate]),
                CancellationToken.None).AsTask());

        Assert.Equal("canonicalNames", error.ParamName);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ResponseMapsIdTextReasoningCallsFinishAndDetailedUsage()
    {
        const string canonicalToolName = "Rekall.Context.Engine_Status";
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("""
            {
              "id":"resp_456",
              "object":"response",
              "model":"gpt-5.6-sol",
              "status":"completed",
              "output":[
                {
                  "id":"rs_1",
                  "type":"reasoning",
                  "summary":[{"type":"summary_text","text":"inspect first"}]
                },
                {
                  "id":"msg_1",
                  "type":"message",
                  "role":"assistant",
                  "content":[{"type":"output_text","text":"done"}]
                },
                {
                  "id":"fc_1",
                  "type":"function_call",
                  "call_id":"call_abc",
                  "name":"Rekall_Context_Engine_Status_3e165c4fa346",
                  "arguments":"{\"detail\":true}",
                  "status":"completed"
                }
              ],
              "usage":{
                "input_tokens":42,
                "input_tokens_details":{"cached_tokens":11},
                "output_tokens":9,
                "output_tokens_details":{"reasoning_tokens":4},
                "total_tokens":51
              }
            }
            """)));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        var response = await client.ChatAsync(
            new RekallAgeLanguageModelRequest(
                "gpt-5.6-sol",
                [],
                [
                    new RekallAgeLanguageModelTool(
                        canonicalToolName,
                        "Inspect.",
                        new JsonObject { ["type"] = "object" })
                ]),
            CancellationToken.None);

        Assert.Equal("openai", response.ProviderId);
        Assert.Equal("resp_456", response.ResponseId);
        Assert.Equal("gpt-5.6-sol", response.Model);
        Assert.Equal("done", response.Content);
        Assert.Equal("inspect first", response.Thinking);
        Assert.Equal("tool_calls", response.FinishReason);
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal(canonicalToolName, call.Name);
        Assert.Equal("call_abc", call.Id);
        Assert.True(call.Arguments["detail"]!.GetValue<bool>());
        Assert.Equal(42, response.Usage.PromptTokens);
        Assert.Equal(9, response.Usage.CompletionTokens);
        Assert.Equal(11, response.Usage.CachedInputTokens);
        Assert.Equal(4, response.Usage.ReasoningTokens);
        Assert.Equal(0, response.Usage.TotalDurationNanoseconds);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("\"not-an-object\"")]
    public async Task MalformedOrNonObjectFunctionArgumentsReturnStableProviderError(string arguments)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse($$"""
            {
              "id":"resp_invalid_arguments",
              "model":"gpt-5.6-sol",
              "status":"completed",
              "output":[{
                "type":"function_call",
                "call_id":"call_invalid",
                "name":"rekall_scene_inspect_d7c351b75103",
                "arguments":{{JsonSerializer.Serialize(arguments)}}
              }],
              "usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}
            }
            """)));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ChatAsync(
                new RekallAgeLanguageModelRequest(
                    "gpt-5.6-sol",
                    [],
                    [
                        new RekallAgeLanguageModelTool(
                            "rekall.scene.inspect",
                            "Inspect.",
                            new JsonObject { ["type"] = "object" })
                    ]),
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_OPENAI_TOOL_ARGUMENTS_INVALID", error.Code);
        Assert.DoesNotContain(arguments, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FunctionCallWithoutCallIdReturnsStableProviderError()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("""
            {
              "id":"resp_missing_call_id",
              "model":"gpt-5.6-sol",
              "status":"completed",
              "output":[{
                "type":"function_call",
                "name":"rekall_scene_inspect_d7c351b75103",
                "arguments":"{}"
              }],
              "usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}
            }
            """)));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ChatAsync(
                new RekallAgeLanguageModelRequest(
                    "gpt-5.6-sol",
                    [],
                    [
                        new RekallAgeLanguageModelTool(
                            "rekall.scene.inspect",
                            "Inspect.",
                            new JsonObject { ["type"] = "object" })
                    ]),
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_OPENAI_TOOL_CALL_ID_REQUIRED", error.Code);
    }

    [Fact]
    public async Task StructuredProviderErrorKeepsRequestIdAndRedactsKeyUserContentAndBody()
    {
        const string apiKey = "sk-test-secret-key";
        const string userContent = "private-user-prompt-9381";
        const string body =
            "{\"error\":{\"message\":\"Key sk-test-secret-key rejected while processing private-user-prompt-9381\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}";
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(apiKey, request.Headers.Authorization?.Parameter);
            return Task.FromResult(JsonResponse(body, HttpStatusCode.Unauthorized, "req_secure_123"));
        });
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, apiKey);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ChatAsync(
                new RekallAgeLanguageModelRequest(
                    "gpt-5.6-sol",
                    [new RekallAgeLanguageModelMessage("user", userContent)],
                    []),
                CancellationToken.None).AsTask());
        var diagnostics = JsonSerializer.Serialize(new
        {
            error.Code,
            error.ProviderId,
            error.HttpStatus,
            error.RequestId,
            error.Retryable,
            error.RequestedValue,
            error.ResolvedValue,
            error.Message,
            Exception = error.ToString()
        });

        Assert.Equal("REKALL_OPENAI_INVALID_API_KEY", error.Code);
        Assert.Equal(401, error.HttpStatus);
        Assert.Equal("req_secure_123", error.RequestId);
        Assert.False(error.Retryable);
        Assert.DoesNotContain(apiKey, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(userContent, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(body, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RetryableStatusesRetryBeforeAResponseBodyIsConsumed(HttpStatusCode statusCode)
    {
        var calls = 0;
        var delays = new DelayRecorder();
        var handler = new RecordingHandler((_, _) => Task.FromResult(
            Interlocked.Increment(ref calls) == 1
                ? JsonResponse("""{"error":{"code":"server_error","message":"temporary"}}""", statusCode)
                : JsonResponse(CompletedResponseJson())));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(
            httpClient,
            "test-api-key",
            null,
            delays.DelayAsync);

        var response = await client.ChatAsync(EmptyRequest(), CancellationToken.None);

        Assert.Equal("complete", response.Content);
        Assert.Equal(2, calls);
        Assert.Single(delays.Delays);
    }

    [Fact]
    public async Task RateLimitRetryHonorsRetryAfterAndDisposesEveryAttempt()
    {
        var attempts = new List<TrackingContent>();
        var delays = new DelayRecorder();
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new TrackingContent(
                attempts.Count == 0
                    ? """{"error":{"code":"rate_limit_exceeded","message":"temporary"}}"""
                    : CompletedResponseJson());
            attempts.Add(content);
            var response = new HttpResponseMessage(
                attempts.Count == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)
            {
                Content = content
            };
            if (attempts.Count == 1)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            }

            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(
            httpClient,
            "test-api-key",
            null,
            delays.DelayAsync);

        await client.ChatAsync(EmptyRequest(), CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(7)], delays.Delays);
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, content => Assert.True(content.IsDisposed));
        Assert.All(attempts, content => Assert.True(content.Stream.IsDisposed));
    }

    [Fact]
    public async Task RetryCountIsBoundedForPersistentServerFailure()
    {
        var delays = new DelayRecorder();
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            """{"error":{"code":"server_error","message":"still failing"}}""",
            HttpStatusCode.InternalServerError)));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(
            httpClient,
            "test-api-key",
            null,
            delays.DelayAsync);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ChatAsync(EmptyRequest(), CancellationToken.None).AsTask());

        Assert.Equal("REKALL_OPENAI_SERVER_ERROR", error.Code);
        Assert.True(error.Retryable);
        Assert.Equal(3, handler.CallCount);
        Assert.Equal([TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(500)], delays.Delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ClientOrModelErrorsNeverRetry(HttpStatusCode statusCode)
    {
        var delays = new DelayRecorder();
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            """{"error":{"code":"model_not_found","message":"invalid request"}}""",
            statusCode)));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(
            httpClient,
            "test-api-key",
            null,
            delays.DelayAsync);

        await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            client.ChatAsync(EmptyRequest(), CancellationToken.None).AsTask());

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(delays.Delays);
    }

    [Fact]
    public async Task CancellationInterruptsRetryDelayWithoutAnotherAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            """{"error":{"code":"rate_limit_exceeded","message":"temporary"}}""",
            HttpStatusCode.TooManyRequests)));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(
            httpClient,
            "test-api-key",
            null,
            (_, cancellationToken) =>
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled(cancellationToken);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ChatAsync(EmptyRequest(), cancellation.Token).AsTask());

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task StreamChatSendsSseRequestAndDisposesResponseContentAndStreamOnSuccess()
    {
        string? requestJson = null;
        var content = new TrackingContent(SuccessfulSse());
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains(
                request.Headers.Accept,
                value => value.MediaType == "text/event-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        var events = await ReadAllAsync(client.StreamChatAsync(EmptyRequest(), CancellationToken.None));

        Assert.True(JsonNode.Parse(requestJson!)!["stream"]!.GetValue<bool>());
        Assert.Single(events, streamEvent =>
            streamEvent.Kind == RekallAgeLanguageModelStreamEventKind.Completed);
        Assert.True(content.IsDisposed);
        Assert.True(content.Stream.IsDisposed);
    }

    [Fact]
    public async Task StreamChatDisposesResponseContentAndStreamOnProviderError()
    {
        var content = new TrackingContent(
            "data: {\"type\":\"error\",\"code\":\"server_error\",\"message\":\"failed\"}\n\n");
        var handler = new RecordingHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");

        await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(client.StreamChatAsync(EmptyRequest(), CancellationToken.None)));

        Assert.True(content.IsDisposed);
        Assert.True(content.Stream.IsDisposed);
    }

    [Fact]
    public async Task StreamChatCancellationDisposesResponseContentAndWaitingStream()
    {
        await using var blockingStream = new BlockingTrackingStream();
        var content = new TrackingContent(blockingStream);
        var handler = new RecordingHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var httpClient = new HttpClient(handler);
        var client = new RekallAgeOpenAiLanguageModelClient(httpClient, "test-api-key");
        using var cancellation = new CancellationTokenSource();

        var readTask = ReadAllAsync(client.StreamChatAsync(EmptyRequest(), cancellation.Token));
        await blockingStream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
        Assert.True(content.IsDisposed);
        Assert.True(blockingStream.IsDisposed);
    }

    private static void AssertMessage(JsonNode? node, string role, string content)
    {
        Assert.Equal("message", node!["type"]!.GetValue<string>());
        Assert.Equal(role, node["role"]!.GetValue<string>());
        Assert.Equal(content, node["content"]!.GetValue<string>());
    }

    private static string CompletedResponseJson() => """
        {
          "id":"resp_completed",
          "object":"response",
          "model":"gpt-5.6-sol",
          "status":"completed",
          "output":[{
            "id":"msg_completed",
            "type":"message",
            "role":"assistant",
            "content":[{"type":"output_text","text":"complete"}]
          }],
          "usage":{
            "input_tokens":1,
            "input_tokens_details":{"cached_tokens":0},
            "output_tokens":1,
            "output_tokens_details":{"reasoning_tokens":0},
            "total_tokens":2
          }
        }
        """;

    private static RekallAgeLanguageModelRequest EmptyRequest() =>
        new("gpt-5.6-sol", [], []);

    private static async Task<List<RekallAgeLanguageModelStreamEvent>> ReadAllAsync(
        IAsyncEnumerable<RekallAgeLanguageModelStreamEvent> stream)
    {
        var events = new List<RekallAgeLanguageModelStreamEvent>();
        await foreach (var streamEvent in stream)
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static string SuccessfulSse() => """
        data: {"type":"response.output_text.delta","delta":"complete"}

        data: {"type":"response.completed","response":{"id":"resp_stream_success","model":"gpt-5.6-sol","status":"completed","output":[{"id":"msg_stream_success","type":"message","role":"assistant","content":[{"type":"output_text","text":"complete"}]}],"usage":{"input_tokens":1,"input_tokens_details":{"cached_tokens":0},"output_tokens":1,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":2}}}

        """;

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? requestId = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (requestId is not null)
        {
            response.Headers.TryAddWithoutValidation("x-request-id", requestId);
        }

        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return handle(request, cancellationToken);
        }
    }

    private sealed class DelayRecorder
    {
        public List<TimeSpan> Delays { get; } = [];

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingContent : HttpContent
    {
        public TrackingContent(string text)
            : this(new TrackingReadStream(Encoding.UTF8.GetBytes(text)))
        {
        }

        public TrackingContent(TrackingReadStream stream)
        {
            Stream = stream;
        }

        public TrackingReadStream Stream { get; }

        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Stream CreateContentReadStream(CancellationToken cancellationToken) => Stream;

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(Stream);

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(Stream);

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            if (disposing)
            {
                Stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private class TrackingReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingTrackingStream() : TrackingReadStream([])
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
