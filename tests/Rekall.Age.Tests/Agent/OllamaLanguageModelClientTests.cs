using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class OllamaLanguageModelClientTests
{
    [Fact]
    public async Task ChatMapsProviderNeutralMessagesToolsCallsAndUsage()
    {
        string? requestJson = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(new Uri("http://127.0.0.1:11434/api/chat"), request.RequestUri);
            requestJson = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""
                {
                  "model":"qwen3.5:35b",
                  "message":{
                    "role":"assistant",
                    "content":"",
                    "thinking":"inspect first",
                    "tool_calls":[{"function":{"name":"rekall.context.engine_status","arguments":{"detail":true}}}]
                  },
                  "done":true,
                  "done_reason":"stop",
                  "total_duration":1200,
                  "prompt_eval_count":42,
                  "eval_count":7
                }
                """);
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeOllamaLanguageModelClient(http, new Uri("http://127.0.0.1:11434"));
        var request = new RekallAgeLanguageModelRequest(
            "qwen3.5:35b",
            [new RekallAgeLanguageModelMessage("user", "Inspect the engine")],
            [new RekallAgeLanguageModelTool("rekall.context.engine_status", "Inspect engine status", new JsonObject { ["type"] = "object" })])
        {
            Think = "medium",
            ContextWindowTokens = 65_536,
            MaxOutputTokens = 8_192
        };

        var response = await client.ChatAsync(request, CancellationToken.None);

        var sent = JsonNode.Parse(requestJson!)!.AsObject();
        Assert.False(sent["stream"]!.GetValue<bool>());
        Assert.Equal("medium", sent["think"]!.GetValue<string>());
        Assert.Equal(65_536, sent["options"]!["num_ctx"]!.GetValue<int>());
        Assert.Equal(8_192, sent["options"]!["num_predict"]!.GetValue<int>());
        Assert.Equal("function", sent["tools"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("rekall.context.engine_status", sent["tools"]![0]!["function"]!["name"]!.GetValue<string>());
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("rekall.context.engine_status", call.Name);
        Assert.True(call.Arguments["detail"]!.GetValue<bool>());
        Assert.Equal("inspect first", response.Thinking);
        Assert.Equal(42, response.Usage.PromptTokens);
        Assert.Equal(7, response.Usage.CompletionTokens);
    }

    [Fact]
    public async Task ListModelsReturnsInstalledOllamaModels()
    {
        var handler = new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/tags", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {"models":[{"name":"qwen3.5:35b","size":24000000000},{"model":"gemma3:latest","size":3000},{"name":"nomic-embed-text:latest","size":2000}]}
                    """);
            }

            Assert.EndsWith("/api/show", request.RequestUri.AbsolutePath, StringComparison.Ordinal);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            var model = body["model"]!.GetValue<string>();
            return model switch
            {
                "qwen3.5:35b" => JsonResponse("""{"capabilities":["completion","tools","thinking"]}"""),
                "gemma3:latest" => JsonResponse("""{"capabilities":["completion"]}"""),
                _ => JsonResponse("""{"capabilities":["embedding"]}""")
            };
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeOllamaLanguageModelClient(http, new Uri("http://localhost:11434"));

        var models = await client.ListModelsAsync(CancellationToken.None);

        Assert.Equal(["gemma3:latest", "qwen3.5:35b"], models.Select(model => model.Id));
        Assert.Equal(24_000_000_000, models.Single(model => model.Id == "qwen3.5:35b").SizeBytes);
        Assert.True(models.Single(model => model.Id == "qwen3.5:35b").SupportsTools);
        Assert.False(models.Single(model => model.Id == "gemma3:latest").SupportsTools);
    }

    [Fact]
    public async Task ChatRetriesTransientServerFailureFromMalformedToolGeneration()
    {
        var calls = 0;
        var handler = new StubHandler(_ => Task.FromResult(Interlocked.Increment(ref calls) == 1
            ? JsonResponse(
                """{"error":"XML syntax error: function closed by parameter"}""",
                HttpStatusCode.InternalServerError)
            : JsonResponse("""
                {
                  "model":"qwen3.5:35b",
                  "message":{"role":"assistant","content":"recovered","tool_calls":[]},
                  "done":true,
                  "done_reason":"stop"
                }
                """)));
        using var http = new HttpClient(handler);
        var client = new RekallAgeOllamaLanguageModelClient(http, new Uri("http://localhost:11434"));

        var response = await client.ChatAsync(
            new RekallAgeLanguageModelRequest("qwen3.5:35b", [], []),
            CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal("recovered", response.Content);
    }

    [Fact]
    public async Task ChatRetriesWithoutThinkWhenModelDoesNotSupportThinking()
    {
        var requestBodies = new List<string>();
        var handler = new StubHandler(async request =>
        {
            requestBodies.Add(await request.Content!.ReadAsStringAsync());
            return requestBodies.Count == 1
                ? JsonResponse(
                    """{"error":"\"devstral-small-2:24b\" does not support thinking"}""",
                    HttpStatusCode.BadRequest)
                : JsonResponse("""
                    {
                      "model":"devstral-small-2:24b",
                      "message":{"role":"assistant","content":"compatible","tool_calls":[]},
                      "done":true,
                      "done_reason":"stop"
                    }
                    """);
        });
        using var http = new HttpClient(handler);
        var client = new RekallAgeOllamaLanguageModelClient(http, new Uri("http://localhost:11434"));

        var response = await client.ChatAsync(
            new RekallAgeLanguageModelRequest("devstral-small-2:24b", [], []) { Think = "medium" },
            CancellationToken.None);

        Assert.Equal(2, requestBodies.Count);
        Assert.Equal("medium", JsonNode.Parse(requestBodies[0])!["think"]!.GetValue<string>());
        Assert.Null(JsonNode.Parse(requestBodies[1])!["think"]);
        Assert.Equal("compatible", response.Content);
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request);
    }
}
