using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Agent.LanguageModels;

public sealed class RekallAgeOllamaLanguageModelClient : IRekallAgeLanguageModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(500)
    ];
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public RekallAgeOllamaLanguageModelClient(HttpClient httpClient, Uri? baseUri = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUri = NormalizeBaseUri(baseUri ?? new Uri("http://127.0.0.1:11434"));
    }

    public string ProviderId => "ollama";

    public async ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(new Uri(_baseUri, "api/tags"), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var root = await ReadObjectAsync(response, cancellationToken);
        var discovered = (root["models"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(model => new RekallAgeLanguageModelInfo(
                ReadString(model, "name") ?? ReadString(model, "model") ?? string.Empty,
                ReadInt64(model, "size")))
            .Where(model => model.Id.Length > 0)
            .ToArray();
        var inspected = await Task.WhenAll(discovered.Select(model => InspectModelAsync(model, cancellationToken)));
        return inspected
            .Where(model => model.SupportsCompletion is not false)
            .OrderBy(model => model.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
        RekallAgeLanguageModelRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        var payload = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = new JsonArray(request.Messages.Select(ToMessage).ToArray()),
            ["tools"] = new JsonArray(request.Tools.Select(ToTool).ToArray()),
            ["stream"] = false
        };
        if (!string.IsNullOrWhiteSpace(request.Think))
        {
            payload["think"] = request.Think;
        }

        if (!string.IsNullOrWhiteSpace(request.KeepAlive))
        {
            payload["keep_alive"] = request.KeepAlive;
        }

        var runtimeOptions = new JsonObject();
        if (request.ContextWindowTokens is { } contextWindowTokens)
        {
            runtimeOptions["num_ctx"] = contextWindowTokens;
        }

        if (request.MaxOutputTokens is { } maxOutputTokens)
        {
            runtimeOptions["num_predict"] = maxOutputTokens;
        }

        if (request.Temperature is { } temperature)
        {
            runtimeOptions["temperature"] = temperature;
        }

        if (runtimeOptions.Count > 0)
        {
            payload["options"] = runtimeOptions;
        }

        var unsupportedThinkRecovered = false;
        for (var attempt = 0; ; attempt++)
        {
            using var response = await _httpClient.PostAsJsonAsync(
                new Uri(_baseUri, "api/chat"),
                payload,
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode
                && IsTransient(response.StatusCode)
                && attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt], cancellationToken);
                continue;
            }

            if (!unsupportedThinkRecovered
                && payload.ContainsKey("think")
                && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (errorBody.Contains("does not support thinking", StringComparison.OrdinalIgnoreCase))
                {
                    payload.Remove("think");
                    unsupportedThinkRecovered = true;
                    continue;
                }
            }

            await EnsureSuccessAsync(response, cancellationToken);
            var root = await ReadObjectAsync(response, cancellationToken);
            var message = root["message"] as JsonObject ?? new JsonObject();
            var calls = (message["tool_calls"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(call => call["function"] as JsonObject)
                .Where(function => function is not null && !string.IsNullOrWhiteSpace(ReadString(function, "name")))
                .Select(function => new RekallAgeLanguageModelToolCall(
                    ReadString(function!, "name")!,
                    function!["arguments"] as JsonObject ?? new JsonObject()))
                .ToArray();
            return new RekallAgeLanguageModelResponse(
                ProviderId,
                ReadString(root, "model") ?? request.Model,
                ReadString(message, "content") ?? string.Empty,
                ReadString(message, "thinking") ?? string.Empty,
                calls,
                ReadString(root, "done_reason") ?? string.Empty,
                new RekallAgeLanguageModelUsage(
                    checked((int)ReadInt64(root, "prompt_eval_count")),
                    checked((int)ReadInt64(root, "eval_count")),
                    ReadInt64(root, "total_duration")));
        }
    }

    private static JsonObject ToMessage(RekallAgeLanguageModelMessage message)
    {
        var value = new JsonObject { ["role"] = message.Role, ["content"] = message.Content };
        if (!string.IsNullOrWhiteSpace(message.ToolName))
        {
            value["tool_name"] = message.ToolName;
        }

        if (message.ToolCalls is { Count: > 0 })
        {
            value["tool_calls"] = new JsonArray(message.ToolCalls.Select((call, index) => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["index"] = index,
                    ["name"] = call.Name,
                    ["arguments"] = call.Arguments.DeepClone()
                }
            }).ToArray());
        }

        return value;
    }

    private static JsonObject ToTool(RekallAgeLanguageModelTool tool) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = tool.Parameters.DeepClone()
        }
    };

    private async Task<RekallAgeLanguageModelInfo> InspectModelAsync(
        RekallAgeLanguageModelInfo model,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                new Uri(_baseUri, "api/show"),
                new JsonObject { ["model"] = model.Id },
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return model;
            }

            var root = await ReadObjectAsync(response, cancellationToken);
            var capabilities = (root["capabilities"] as JsonArray ?? [])
                .OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out var capability) ? capability : string.Empty)
                .Where(capability => capability.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return model with
            {
                SupportsTools = capabilities.Contains("tools"),
                SupportsCompletion = capabilities.Contains("completion")
            };
        }
        catch (HttpRequestException)
        {
            return model;
        }
    }

    private static async ValueTask EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {body}",
            null,
            response.StatusCode);
    }

    private static bool IsTransient(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.RequestTimeout
            or System.Net.HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static async ValueTask<JsonObject> ReadObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
            ?? throw new InvalidDataException("Ollama returned a response that was not a JSON object.");
    }

    private static Uri NormalizeBaseUri(Uri baseUri)
    {
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Ollama base URI must be an absolute HTTP or HTTPS URI.", nameof(baseUri));
        }

        return new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static string? ReadString(JsonObject value, string name) =>
        value[name] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static long ReadInt64(JsonObject value, string name) =>
        value[name] is JsonValue node && node.TryGetValue<long>(out var number) ? number : 0;
}
