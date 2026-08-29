using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Agent.LanguageModels;

public sealed class RekallAgeKimiLanguageModelClient : IRekallAgeLanguageModelClient
{
    private const string DefaultBaseUrl = "https://api.moonshot.ai/v1/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _baseUri;

    public RekallAgeKimiLanguageModelClient(
        HttpClient httpClient,
        string apiKey,
        Uri? baseUri = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("A Kimi API key is required.", nameof(apiKey))
            : apiKey;
        _baseUri = NormalizeBaseUri(baseUri ?? new Uri(DefaultBaseUrl));
    }

    public string ProviderId => "kimi";

    public async ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "models");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var root = await ReadObjectAsync(response, cancellationToken);
        return (root["data"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(model => ReadString(model, "id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => new RekallAgeLanguageModelInfo(
                id!,
                0,
                SupportsTools: true,
                SupportsCompletion: true))
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
            ["stream"] = false
        };
        if (request.Tools.Count > 0)
        {
            payload["tools"] = new JsonArray(request.Tools.Select(ToTool).ToArray());
        }
        if (request.MaxOutputTokens is { } maxOutputTokens)
        {
            payload["max_completion_tokens"] = maxOutputTokens;
        }
        if (request.Temperature is { } temperature)
        {
            payload["temperature"] = temperature;
        }
        ApplyReasoning(request.Model, request.Think, payload);

        using var message = CreateRequest(HttpMethod.Post, "chat/completions");
        message.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var root = await ReadObjectAsync(response, cancellationToken);
        var choice = (root["choices"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()
            ?? throw InvalidResponse("Kimi returned no completion choice.");
        var responseMessage = choice["message"] as JsonObject
            ?? throw InvalidResponse("Kimi returned a completion choice without a message.");
        var calls = (responseMessage["tool_calls"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(ParseToolCall)
            .ToArray();
        var usage = root["usage"] as JsonObject ?? new JsonObject();
        var reasoning = ReadString(responseMessage, "reasoning_content") ?? string.Empty;
        return new RekallAgeLanguageModelResponse(
            ProviderId,
            ReadString(root, "model") ?? request.Model,
            ReadString(responseMessage, "content") ?? string.Empty,
            reasoning,
            calls,
            ReadString(choice, "finish_reason") ?? string.Empty,
            new RekallAgeLanguageModelUsage(
                ReadInt32(usage, "prompt_tokens"),
                ReadInt32(usage, "completion_tokens"),
                0)
            {
                CachedInputTokens = TryReadInt32(usage, "cached_tokens"),
                ReasoningTokens = TryReadInt32(usage, "reasoning_tokens")
            })
        {
            ResponseId = ReadString(root, "id"),
            OpaqueProviderState = reasoning.Length == 0
                ? null
                : new RekallAgeLanguageModelOpaqueState(
                    ProviderId,
                    [new JsonObject { ["reasoning_content"] = reasoning }.ToJsonString()])
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return request;
    }

    private static JsonObject ToMessage(RekallAgeLanguageModelMessage message)
    {
        var value = new JsonObject
        {
            ["role"] = message.Role,
            ["content"] = message.Content
        };
        if (message.Role.Equals("tool", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(message.ToolCallId))
        {
            value["tool_call_id"] = message.ToolCallId;
        }
        if (message.ToolCalls is { Count: > 0 })
        {
            value["tool_calls"] = new JsonArray(message.ToolCalls.Select(call => (JsonNode)new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = call.Name,
                    ["arguments"] = call.Arguments.ToJsonString()
                }
            }).ToArray());
        }
        if (message.OpaqueProviderState is { ProviderId: "kimi", Items.Count: > 0 }
            && TryReadPreservedReasoning(message.OpaqueProviderState.Items[0]) is { Length: > 0 } reasoning)
        {
            value["reasoning_content"] = reasoning;
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

    private static RekallAgeLanguageModelToolCall ParseToolCall(JsonObject call)
    {
        var function = call["function"] as JsonObject
            ?? throw InvalidResponse("Kimi returned a tool call without a function.");
        var name = ReadString(function, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw InvalidResponse("Kimi returned a tool call without a function name.");
        }
        var argumentsText = ReadString(function, "arguments") ?? "{}";
        JsonObject arguments;
        try
        {
            arguments = JsonNode.Parse(argumentsText) as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_KIMI_TOOL_ARGUMENTS_INVALID",
                "kimi",
                "Kimi returned invalid tool-call arguments.");
        }
        return new RekallAgeLanguageModelToolCall(name, arguments)
        {
            Id = ReadString(call, "id")
        };
    }

    private static void ApplyReasoning(string model, string? requestedEffort, JsonObject payload)
    {
        if (model.StartsWith("kimi-k3", StringComparison.OrdinalIgnoreCase))
        {
            payload["reasoning_effort"] = requestedEffort switch
            {
                "low" => "low",
                "high" => "high",
                _ => "max"
            };
            return;
        }
        if (model.StartsWith("kimi-k2.7-code", StringComparison.OrdinalIgnoreCase))
        {
            payload["thinking"] = new JsonObject { ["type"] = "enabled", ["keep"] = "all" };
            return;
        }
        if (model.StartsWith("kimi-k2.", StringComparison.OrdinalIgnoreCase))
        {
            var thinking = new JsonObject
            {
                ["type"] = requestedEffort == "none" ? "disabled" : "enabled"
            };
            if (requestedEffort != "none") thinking["keep"] = "all";
            payload["thinking"] = thinking;
        }
    }

    private async ValueTask EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        // Drain and dispose provider content without ever surfacing provider-controlled text.
        _ = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var (code, message, retryable) = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                ("REKALL_KIMI_AUTHENTICATION_FAILED", "Kimi authentication failed. Apply a valid session API key and retry.", false),
            HttpStatusCode.TooManyRequests =>
                ("REKALL_KIMI_RATE_LIMITED", "Kimi rate-limited the request. Retry later.", true),
            HttpStatusCode.RequestTimeout =>
                ("REKALL_KIMI_TIMEOUT", "Kimi timed out while processing the request.", true),
            _ when (int)response.StatusCode >= 500 =>
                ("REKALL_KIMI_UNAVAILABLE", "Kimi is temporarily unavailable.", true),
            _ =>
                ("REKALL_KIMI_REQUEST_FAILED", "Kimi rejected the request.", false)
        };
        throw new RekallAgeLanguageModelProviderException(
            code,
            ProviderId,
            message,
            (int)response.StatusCode,
            requestId: ReadRequestId(response),
            retryable: retryable,
            sensitiveValues: [_apiKey]);
    }

    private static string? ReadRequestId(HttpResponseMessage response)
    {
        foreach (var name in new[] { "x-request-id", "request-id" })
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                return values.FirstOrDefault();
            }
        }
        return null;
    }

    private static async ValueTask<JsonObject> ReadObjectAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
                ?? throw InvalidResponse("Kimi returned a response that was not a JSON object.");
        }
        catch (JsonException)
        {
            throw InvalidResponse("Kimi returned malformed JSON.");
        }
    }

    private static RekallAgeLanguageModelProviderException InvalidResponse(string message) => new(
        "REKALL_KIMI_RESPONSE_INVALID",
        "kimi",
        message);

    private static string? TryReadPreservedReasoning(string value)
    {
        try
        {
            return JsonNode.Parse(value)?["reasoning_content"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonObject value, string name) =>
        value[name] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static int ReadInt32(JsonObject value, string name) => TryReadInt32(value, name) ?? 0;

    private static int? TryReadInt32(JsonObject value, string name) =>
        value[name] is JsonValue node && node.TryGetValue<int>(out var number) ? number : null;

    private static Uri NormalizeBaseUri(Uri baseUri)
    {
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Kimi base URI must be an absolute HTTP or HTTPS URI.", nameof(baseUri));
        }
        return new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
