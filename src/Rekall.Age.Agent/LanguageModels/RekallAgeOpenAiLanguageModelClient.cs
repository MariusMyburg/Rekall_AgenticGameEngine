using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Agent.LanguageModels;

public sealed class RekallAgeOpenAiLanguageModelClient :
    IRekallAgeLanguageModelClient,
    IRekallAgeStreamingLanguageModelClient
{
    private const string ModelId = "gpt-5.6-sol";
    private static readonly Uri DefaultBaseUri = new("https://api.openai.com/v1/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(500)
    ];
    private static readonly IReadOnlySet<string> SupportedReasoningEfforts =
        new HashSet<string>(["none", "low", "medium", "high", "xhigh", "max"], StringComparer.Ordinal);
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> _delayAsync;

    public RekallAgeOpenAiLanguageModelClient(
        HttpClient httpClient,
        string apiKey,
        Uri? baseUri = null)
        : this(httpClient, apiKey, baseUri, DefaultDelayAsync)
    {
    }

    internal RekallAgeOpenAiLanguageModelClient(
        HttpClient httpClient,
        string apiKey,
        Uri? baseUri,
        Func<TimeSpan, CancellationToken, ValueTask> delayAsync)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
        BaseUri = NormalizeBaseUri(baseUri ?? DefaultBaseUri);
    }

    public string ProviderId => "openai";

    internal Uri BaseUri { get; }

    public async ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken)
    {
        var sensitiveValues = SensitiveValues();
        using var response = await SendWithRetriesAsync(
            () => CreateRequest(HttpMethod.Get, "models"),
            sensitiveValues,
            cancellationToken);
        await EnsureSuccessAsync(response, sensitiveValues, cancellationToken);
        var root = await ReadObjectAsync(response, sensitiveValues, cancellationToken);
        return (root["data"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(model => ReadString(model, "id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new RekallAgeLanguageModelInfo(id!, 0))
            .ToArray();
    }

    public async ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
        RekallAgeLanguageModelRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sensitiveValues = SensitiveValues(request);
        ValidateModel(request.Model, sensitiveValues);
        var toolNameMap = RekallAgeOpenAiToolNameMap.Create(request.Tools.Select(tool => tool.Name).ToArray());
        var payload = BuildPayload(request, toolNameMap, stream: false, sensitiveValues);
        using var response = await SendWithRetriesAsync(
            () => CreateRequest(HttpMethod.Post, "responses", payload),
            sensitiveValues,
            cancellationToken);
        await EnsureSuccessAsync(response, sensitiveValues, cancellationToken);
        var root = await ReadObjectAsync(response, sensitiveValues, cancellationToken);
        return MapResponse(root, toolNameMap, sensitiveValues, RequestId(response));
    }

    public async IAsyncEnumerable<RekallAgeLanguageModelStreamEvent> StreamChatAsync(
        RekallAgeLanguageModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sensitiveValues = SensitiveValues(request);
        ValidateModel(request.Model, sensitiveValues);
        var toolNameMap = RekallAgeOpenAiToolNameMap.Create(request.Tools.Select(tool => tool.Name).ToArray());
        var payload = BuildPayload(request, toolNameMap, stream: true, sensitiveValues);
        using var response = await SendWithRetriesAsync(
            () => CreateRequest(HttpMethod.Post, "responses", payload, acceptEventStream: true),
            sensitiveValues,
            cancellationToken);
        await EnsureSuccessAsync(response, sensitiveValues, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var reader = new RekallAgeOpenAiResponseStreamReader(
            toolNameMap,
            sensitiveValues,
            RequestId(response));
        await foreach (var streamEvent in reader.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            yield return streamEvent;
        }
    }

    private JsonObject BuildPayload(
        RekallAgeLanguageModelRequest request,
        RekallAgeOpenAiToolNameMap toolNameMap,
        bool stream,
        IReadOnlyCollection<string> sensitiveValues)
    {
        var payload = new JsonObject
        {
            ["model"] = ModelId,
            ["input"] = BuildInput(request.Messages, toolNameMap, sensitiveValues),
            ["tools"] = BuildTools(request.Tools, toolNameMap),
            ["stream"] = stream,
            ["store"] = false,
            ["parallel_tool_calls"] = true,
            ["include"] = new JsonArray("reasoning.encrypted_content"),
            ["reasoning"] = BuildReasoning(request.Think, sensitiveValues)
        };
        if (request.MaxOutputTokens is { } maxOutputTokens)
        {
            payload["max_output_tokens"] = maxOutputTokens;
        }

        if (request.Temperature is { } temperature)
        {
            payload["temperature"] = temperature;
        }

        return payload;
    }

    private static JsonArray BuildInput(
        IReadOnlyList<RekallAgeLanguageModelMessage> messages,
        RekallAgeOpenAiToolNameMap toolNameMap,
        IReadOnlyCollection<string> sensitiveValues)
    {
        var input = new JsonArray();
        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case "system":
                case "developer":
                case "user":
                    input.Add(new JsonObject
                    {
                        ["type"] = "message",
                        ["role"] = message.Role == "system" ? "developer" : message.Role,
                        ["content"] = message.Content
                    });
                    break;
                case "assistant":
                    if (message.OpaqueProviderState is not null)
                    {
                        ReplayOpaqueProviderState(message.OpaqueProviderState, input, toolNameMap, sensitiveValues);
                        break;
                    }

                    if (message.Content.Length > 0)
                    {
                        input.Add(new JsonObject
                        {
                            ["type"] = "message",
                            ["role"] = "assistant",
                            ["content"] = message.Content
                        });
                    }

                    foreach (var call in message.ToolCalls ?? [])
                    {
                        if (string.IsNullOrWhiteSpace(call.Id))
                        {
                            throw ProviderError(
                                "REKALL_OPENAI_TOOL_CALL_ID_REQUIRED",
                                "OpenAI assistant tool-call history requires a call ID.",
                                sensitiveValues);
                        }

                        input.Add(new JsonObject
                        {
                            ["type"] = "function_call",
                            ["call_id"] = call.Id,
                            ["name"] = AliasFor(toolNameMap, call.Name, sensitiveValues),
                            ["arguments"] = call.Arguments.ToJsonString(JsonOptions)
                        });
                    }

                    break;
                case "tool":
                    if (string.IsNullOrWhiteSpace(message.ToolCallId))
                    {
                        throw ProviderError(
                            "REKALL_OPENAI_TOOL_CALL_ID_REQUIRED",
                            "OpenAI function-call output history requires a call ID.",
                            sensitiveValues);
                    }

                    input.Add(new JsonObject
                    {
                        ["type"] = "function_call_output",
                        ["call_id"] = message.ToolCallId,
                        ["output"] = message.Content
                    });
                    break;
                default:
                    throw new RekallAgeLanguageModelProviderException(
                        "REKALL_OPENAI_MESSAGE_ROLE_UNSUPPORTED",
                        "openai",
                        "OpenAI does not support the supplied AGE message role.",
                        requestedValue: message.Role,
                        sensitiveValues: sensitiveValues);
            }
        }

        return input;
    }

    private static void ReplayOpaqueProviderState(
        RekallAgeLanguageModelOpaqueState state,
        JsonArray input,
        RekallAgeOpenAiToolNameMap toolNameMap,
        IReadOnlyCollection<string> sensitiveValues)
    {
        if (!state.ProviderId.Equals("openai", StringComparison.Ordinal))
        {
            throw ProviderError(
                "REKALL_OPENAI_CONTINUATION_PROVIDER_INVALID",
                "OpenAI cannot replay opaque state from another provider.",
                sensitiveValues);
        }

        foreach (var serializedItem in state.Items)
        {
            JsonObject item;
            try
            {
                item = JsonNode.Parse(serializedItem) as JsonObject ?? throw new JsonException();
                ValidateOpaqueOutputItem(item, toolNameMap);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
            {
                throw ProviderError(
                    "REKALL_OPENAI_CONTINUATION_INVALID",
                    "OpenAI opaque continuation state is invalid.",
                    sensitiveValues);
            }

            input.Add(item);
        }
    }

    private static void ValidateOpaqueOutputItem(
        JsonObject item,
        RekallAgeOpenAiToolNameMap toolNameMap)
    {
        switch (ReadString(item, "type"))
        {
            case "reasoning" when !string.IsNullOrWhiteSpace(ReadString(item, "encrypted_content")):
                return;
            case "function_call" when
                !string.IsNullOrWhiteSpace(ReadString(item, "call_id"))
                && !string.IsNullOrWhiteSpace(ReadString(item, "name"))
                && ReadString(item, "arguments") is not null:
                _ = toolNameMap.ToCanonical(ReadString(item, "name")!);
                return;
            case "message" when
                ReadString(item, "role") == "assistant"
                && item["content"] is JsonArray:
                return;
            default:
                throw new JsonException();
        }
    }

    private static JsonArray BuildTools(
        IReadOnlyList<RekallAgeLanguageModelTool> tools,
        RekallAgeOpenAiToolNameMap toolNameMap) =>
        new(tools.Select(tool => (JsonNode)new JsonObject
        {
            ["type"] = "function",
            ["name"] = toolNameMap.ToAlias(tool.Name),
            ["description"] = $"{tool.Description}\n\nCanonical AGE tool name: {tool.Name}",
            ["parameters"] = tool.Parameters.DeepClone(),
            ["strict"] = false
        }).ToArray());

    private static JsonObject BuildReasoning(
        string? requestedEffort,
        IReadOnlyCollection<string> sensitiveValues)
    {
        var reasoning = new JsonObject { ["summary"] = "auto" };
        if (requestedEffort is null)
        {
            return reasoning;
        }

        if (!SupportedReasoningEfforts.Contains(requestedEffort))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_OPENAI_REASONING_EFFORT_UNSUPPORTED",
                "openai",
                "OpenAI reasoning effort is unsupported for the configured model.",
                requestedValue: requestedEffort,
                sensitiveValues: sensitiveValues);
        }

        reasoning["effort"] = requestedEffort;
        return reasoning;
    }

    internal static RekallAgeLanguageModelResponse MapResponse(
        JsonObject root,
        RekallAgeOpenAiToolNameMap toolNameMap,
        IReadOnlyCollection<string> sensitiveValues,
        string? requestId)
    {
        var model = ReadString(root, "model");
        if (!string.Equals(model, ModelId, StringComparison.Ordinal))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_OPENAI_RESPONSE_MODEL_INVALID",
                "openai",
                "OpenAI returned an unexpected or missing model identifier.",
                requestId: requestId,
                requestedValue: ModelId,
                resolvedValue: model,
                sensitiveValues: sensitiveValues);
        }

        var status = ReadString(root, "status") ?? string.Empty;
        if (status == "failed")
        {
            var responseError = root["error"] as JsonObject;
            throw new RekallAgeLanguageModelProviderException(
                ProviderCode(ReadString(responseError, "code"), null),
                "openai",
                "OpenAI response generation failed.",
                requestId: requestId,
                sensitiveValues: sensitiveValues);
        }

        var outputItems = (root["output"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        var calls = new List<RekallAgeLanguageModelToolCall>();
        foreach (var output in outputItems)
        {
            switch (ReadString(output, "type"))
            {
                case "message":
                    foreach (var content in (output["content"] as JsonArray ?? []).OfType<JsonObject>())
                    {
                        switch (ReadString(content, "type"))
                        {
                            case "output_text":
                                text.Append(ReadString(content, "text"));
                                break;
                            case "refusal":
                                text.Append(ReadString(content, "refusal"));
                                break;
                        }
                    }

                    break;
                case "reasoning":
                    foreach (var summary in (output["summary"] as JsonArray ?? []).OfType<JsonObject>())
                    {
                        if (ReadString(summary, "type") == "summary_text")
                        {
                            reasoning.Append(ReadString(summary, "text"));
                        }
                    }

                    break;
                case "function_call":
                    calls.Add(MapToolCall(output, toolNameMap, sensitiveValues, requestId));
                    break;
            }
        }

        var usageObject = root["usage"] as JsonObject ?? new JsonObject();
        var inputDetails = usageObject["input_tokens_details"] as JsonObject;
        var outputDetails = usageObject["output_tokens_details"] as JsonObject;
        var usage = new RekallAgeLanguageModelUsage(
            ReadInt32(usageObject, "input_tokens"),
            ReadInt32(usageObject, "output_tokens"),
            0)
        {
            CachedInputTokens = ReadNullableInt32(inputDetails, "cached_tokens"),
            ReasoningTokens = ReadNullableInt32(outputDetails, "reasoning_tokens")
        };
        var finishReason = status switch
        {
            "completed" when calls.Count > 0 => "tool_calls",
            "completed" => "stop",
            "incomplete" => ReadString(root["incomplete_details"] as JsonObject, "reason") ?? "incomplete",
            "cancelled" => "cancelled",
            _ => status
        };
        RekallAgeLanguageModelOpaqueState? opaqueProviderState;
        try
        {
            opaqueProviderState = CreateOpaqueProviderState(outputItems);
        }
        catch (ArgumentException)
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_OPENAI_CONTINUATION_TOO_LARGE",
                "openai",
                "OpenAI returned continuation state beyond the configured bound.",
                requestId: requestId,
                sensitiveValues: sensitiveValues);
        }

        return new RekallAgeLanguageModelResponse(
            "openai",
            model!,
            text.ToString(),
            reasoning.ToString(),
            calls,
            finishReason,
            usage)
        {
            ResponseId = ReadString(root, "id"),
            OpaqueProviderState = opaqueProviderState
        };
    }

    private static RekallAgeLanguageModelOpaqueState? CreateOpaqueProviderState(
        IReadOnlyList<JsonObject> outputItems)
    {
        var items = outputItems
            .Where(output => ReadString(output, "type") is "function_call" or "message"
                || (ReadString(output, "type") == "reasoning"
                    && !string.IsNullOrWhiteSpace(ReadString(output, "encrypted_content"))))
            .Select(output => output.ToJsonString(JsonOptions))
            .ToArray();
        return items.Length == 0
            ? null
            : new RekallAgeLanguageModelOpaqueState("openai", items);
    }

    private static RekallAgeLanguageModelToolCall MapToolCall(
        JsonObject output,
        RekallAgeOpenAiToolNameMap toolNameMap,
        IReadOnlyCollection<string> sensitiveValues,
        string? requestId)
    {
        var alias = ReadString(output, "name");
        string canonicalName;
        try
        {
            canonicalName = toolNameMap.ToCanonical(alias ?? string.Empty);
        }
        catch (KeyNotFoundException)
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_OPENAI_TOOL_NAME_UNKNOWN",
                "openai",
                "OpenAI returned an unknown function tool name.",
                requestId: requestId,
                sensitiveValues: sensitiveValues);
        }

        var callId = ReadString(output, "call_id");
        if (string.IsNullOrWhiteSpace(callId))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_OPENAI_TOOL_CALL_ID_REQUIRED",
                "openai",
                "OpenAI returned a function call without its required call ID.",
                requestId: requestId,
                sensitiveValues: sensitiveValues);
        }

        var rawArguments = ReadString(output, "arguments");
        JsonObject arguments;
        try
        {
            arguments = JsonNode.Parse(rawArguments ?? string.Empty) as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_OPENAI_TOOL_ARGUMENTS_INVALID",
                "openai",
                "OpenAI returned function arguments that were not a JSON object.",
                requestId: requestId,
                sensitiveValues: sensitiveValues);
        }

        return new RekallAgeLanguageModelToolCall(canonicalName, arguments)
        {
            Id = callId
        };
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativePath,
        JsonObject? payload = null,
        bool acceptEventStream = false)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (acceptEventStream)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        if (payload is not null)
        {
            request.Content = new StringContent(
                payload.ToJsonString(JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    private async ValueTask<HttpResponseMessage> SendWithRetriesAsync(
        Func<HttpRequestMessage> requestFactory,
        IReadOnlyCollection<string> sensitiveValues,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = requestFactory();
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                throw ProviderError(
                    "REKALL_OPENAI_TRANSPORT_ERROR",
                    "OpenAI transport request failed.",
                    sensitiveValues,
                    retryable: true);
            }

            if (response.IsSuccessStatusCode
                || !IsTransient(response.StatusCode)
                || attempt >= RetryDelays.Length)
            {
                return response;
            }

            var delay = RetryDelay(response, RetryDelays[attempt]);
            response.Dispose();
            await _delayAsync(delay, cancellationToken);
        }
    }

    private static async ValueTask EnsureSuccessAsync(
        HttpResponseMessage response,
        IReadOnlyCollection<string> sensitiveValues,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? providerCode = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject;
            providerCode = ReadString(root?["error"] as JsonObject, "code");
        }
        catch (JsonException)
        {
            // Provider bodies are intentionally never copied into diagnostics.
        }

        var statusCode = (int)response.StatusCode;
        throw new RekallAgeLanguageModelProviderException(
            ProviderCode(providerCode, response.StatusCode),
            "openai",
            "OpenAI request failed.",
            httpStatus: statusCode,
            requestId: RequestId(response),
            retryable: IsTransient(response.StatusCode),
            sensitiveValues: sensitiveValues);
    }

    private static async ValueTask<JsonObject> ReadObjectAsync(
        HttpResponseMessage response,
        IReadOnlyCollection<string> sensitiveValues,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_OPENAI_RESPONSE_INVALID",
                "openai",
                "OpenAI returned an invalid JSON response.",
                httpStatus: (int)response.StatusCode,
                requestId: RequestId(response),
                sensitiveValues: sensitiveValues);
        }
    }

    private static void ValidateModel(string requestedModel, IReadOnlyCollection<string> sensitiveValues)
    {
        if (!string.Equals(requestedModel, ModelId, StringComparison.Ordinal))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_OPENAI_MODEL_UNSUPPORTED",
                "openai",
                "OpenAI provider requires its configured model identifier exactly.",
                requestedValue: requestedModel,
                resolvedValue: ModelId,
                sensitiveValues: sensitiveValues);
        }
    }

    private static string AliasFor(
        RekallAgeOpenAiToolNameMap toolNameMap,
        string canonicalName,
        IReadOnlyCollection<string> sensitiveValues)
    {
        try
        {
            return toolNameMap.ToAlias(canonicalName);
        }
        catch (KeyNotFoundException)
        {
            throw ProviderError(
                "REKALL_OPENAI_TOOL_NAME_UNKNOWN",
                "OpenAI assistant history referenced an unknown AGE tool.",
                sensitiveValues);
        }
    }

    private IReadOnlyCollection<string> SensitiveValues(RekallAgeLanguageModelRequest? request = null)
    {
        var values = new List<string> { _apiKey };
        if (request is null)
        {
            return values;
        }

        values.AddRange(request.Messages.Select(message => message.Content));
        values.AddRange(request.Tools.Select(tool => tool.Description));
        values.AddRange(request.Messages
            .SelectMany(message => message.ToolCalls ?? [])
            .Select(call => call.Arguments.ToJsonString(JsonOptions)));
        values.AddRange(request.Messages
            .Where(message => message.OpaqueProviderState is not null)
            .SelectMany(message => message.OpaqueProviderState!.Items));
        return values.Where(value => !string.IsNullOrEmpty(value)).ToArray();
    }

    private static RekallAgeLanguageModelProviderException ProviderError(
        string code,
        string message,
        IReadOnlyCollection<string> sensitiveValues,
        bool retryable = false) =>
        new(code, "openai", message, retryable: retryable, sensitiveValues: sensitiveValues);

    internal static string ProviderCode(string? providerCode, HttpStatusCode? statusCode)
    {
        if (!string.IsNullOrWhiteSpace(providerCode))
        {
            var normalized = new string(providerCode
                .Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_')
                .ToArray());
            return $"REKALL_OPENAI_{normalized}";
        }

        return statusCode switch
        {
            HttpStatusCode.RequestTimeout => "REKALL_OPENAI_REQUEST_TIMEOUT",
            HttpStatusCode.TooManyRequests => "REKALL_OPENAI_RATE_LIMITED",
            HttpStatusCode.BadRequest => "REKALL_OPENAI_BAD_REQUEST",
            HttpStatusCode.Unauthorized => "REKALL_OPENAI_UNAUTHORIZED",
            HttpStatusCode.Forbidden => "REKALL_OPENAI_FORBIDDEN",
            _ when statusCode is not null && (int)statusCode >= 500 => "REKALL_OPENAI_SERVER_ERROR",
            _ => "REKALL_OPENAI_HTTP_ERROR"
        };
    }

    private static Uri NormalizeBaseUri(Uri baseUri)
    {
        if (!baseUri.IsAbsoluteUri
            || (baseUri.Scheme != Uri.UriSchemeHttps
                && (baseUri.Scheme != Uri.UriSchemeHttp || !baseUri.IsLoopback)))
        {
            throw new ArgumentException(
                "OpenAI base URI must use HTTPS, except that loopback HTTP is allowed.",
                nameof(baseUri));
        }
        if (baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0)
        {
            throw new ArgumentException(
                "OpenAI base URI cannot contain user information, a query, or a fragment.",
                nameof(baseUri));
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = baseUri.AbsolutePath.TrimEnd('/') + '/'
        };
        return builder.Uri;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static TimeSpan RetryDelay(HttpResponseMessage response, TimeSpan fallback)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return fallback;
    }

    private static ValueTask DefaultDelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        new(Task.Delay(delay, cancellationToken));

    private static string? RequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-request-id", out var values)
            ? values.FirstOrDefault()
            : null;

    private static string? ReadString(JsonObject? value, string name) =>
        value?[name] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static int ReadInt32(JsonObject value, string name) =>
        value[name] is JsonValue node && node.TryGetValue<int>(out var number) ? number : 0;

    private static int? ReadNullableInt32(JsonObject? value, string name) =>
        value?[name] is JsonValue node && node.TryGetValue<int>(out var number) ? number : null;
}
