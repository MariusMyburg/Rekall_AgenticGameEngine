using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Agent.LanguageModels;

internal sealed class RekallAgeOpenAiResponseStreamReader
{
    private const int DefaultMaximumEventCharacters = 262_144;
    private const int DefaultMaximumTerminalEventCharacters = 8_388_608;
    private const int DefaultMaximumTextCharacters = 4_194_304;
    private const int DefaultMaximumArgumentCharacters = 4_194_304;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly RekallAgeOpenAiToolNameMap _toolNameMap;
    private readonly IReadOnlyCollection<string> _sensitiveValues;
    private readonly string? _requestId;
    private readonly int _maximumEventCharacters;
    private readonly int _maximumTerminalEventCharacters;
    private readonly int _maximumTextCharacters;
    private readonly int _maximumArgumentCharacters;

    public RekallAgeOpenAiResponseStreamReader(
        RekallAgeOpenAiToolNameMap toolNameMap,
        IReadOnlyCollection<string>? sensitiveValues = null,
        string? requestId = null,
        int maxEventCharacters = DefaultMaximumEventCharacters,
        int maxTextCharacters = DefaultMaximumTextCharacters,
        int maxArgumentCharacters = DefaultMaximumArgumentCharacters,
        int maxTerminalEventCharacters = DefaultMaximumTerminalEventCharacters)
    {
        _toolNameMap = toolNameMap ?? throw new ArgumentNullException(nameof(toolNameMap));
        _sensitiveValues = sensitiveValues ?? [];
        _requestId = requestId;
        _maximumEventCharacters = Positive(maxEventCharacters, nameof(maxEventCharacters));
        _maximumTerminalEventCharacters = Positive(
            maxTerminalEventCharacters,
            nameof(maxTerminalEventCharacters));
        if (_maximumTerminalEventCharacters < _maximumEventCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTerminalEventCharacters),
                "The terminal SSE event bound cannot be smaller than the ordinary event bound.");
        }
        _maximumTextCharacters = Positive(maxTextCharacters, nameof(maxTextCharacters));
        _maximumArgumentCharacters = Positive(maxArgumentCharacters, nameof(maxArgumentCharacters));
    }

    public async IAsyncEnumerable<RekallAgeLanguageModelStreamEvent> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var eventData = new StringBuilder();
        var state = new StreamState();
        await foreach (var line in ReadBoundedLinesAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length == 0)
            {
                if (eventData.Length == 0)
                {
                    continue;
                }

                foreach (var streamEvent in ProcessEvent(eventData.ToString(), state))
                {
                    yield return streamEvent;
                }

                eventData.Clear();
                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            var separator = line.IndexOf(':');
            var field = separator < 0 ? line : line[..separator];
            if (field != "data")
            {
                continue;
            }

            var value = separator < 0 ? string.Empty : line[(separator + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            var additionalCharacters = value.Length + (eventData.Length == 0 ? 0 : 1);
            if (eventData.Length > _maximumTerminalEventCharacters - additionalCharacters)
            {
                throw StreamError(
                    "REKALL_OPENAI_STREAM_EVENT_TOO_LARGE",
                    "OpenAI streamed an SSE event beyond the configured bound.");
            }

            if (eventData.Length > 0)
            {
                eventData.Append('\n');
            }

            eventData.Append(value);
        }

        if (eventData.Length > 0)
        {
            foreach (var streamEvent in ProcessEvent(eventData.ToString(), state))
            {
                yield return streamEvent;
            }
        }

        if (!state.Completed)
        {
            throw StreamError(
                "REKALL_OPENAI_STREAM_PREMATURE_EOF",
                "OpenAI stream ended before a completion event.");
        }
    }

    private IReadOnlyList<RekallAgeLanguageModelStreamEvent> ProcessEvent(
        string data,
        StreamState state)
    {
        if (data == "[DONE]")
        {
            return [];
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(data) as JsonObject ?? throw new JsonException();
        }
        catch (JsonException)
        {
            if (data.Length > _maximumEventCharacters)
            {
                throw StreamError(
                    "REKALL_OPENAI_STREAM_EVENT_TOO_LARGE",
                    "OpenAI streamed an SSE event beyond the configured bound.");
            }

            throw StreamError(
                "REKALL_OPENAI_STREAM_INVALID",
                "OpenAI streamed an invalid JSON event.");
        }

        var type = ReadString(root, "type") ?? string.Empty;
        if (data.Length > _maximumEventCharacters
            && type is not "response.completed" and not "response.incomplete" and not "response.failed")
        {
            throw StreamError(
                "REKALL_OPENAI_STREAM_EVENT_TOO_LARGE",
                "OpenAI streamed an SSE event beyond the configured bound.");
        }
        if (state.Completed)
        {
            throw StreamError(
                type is "response.completed" or "response.incomplete"
                    ? "REKALL_OPENAI_STREAM_DUPLICATE_COMPLETION"
                    : "REKALL_OPENAI_STREAM_AFTER_COMPLETION",
                "OpenAI streamed data after its completion event.");
        }

        switch (type)
        {
            case "response.output_text.delta":
            case "response.refusal.delta":
            {
                var delta = ReadString(root, "delta") ?? string.Empty;
                AddTextCharacters(state, delta.Length);
                return
                [
                    new RekallAgeLanguageModelStreamEvent(
                        RekallAgeLanguageModelStreamEventKind.TextDelta,
                        delta)
                ];
            }
            case "response.reasoning_summary_text.delta":
            case "response.reasoning_text.delta":
            {
                var delta = ReadString(root, "delta") ?? string.Empty;
                AddTextCharacters(state, delta.Length);
                return
                [
                    new RekallAgeLanguageModelStreamEvent(
                        RekallAgeLanguageModelStreamEventKind.ThinkingDelta,
                        delta)
                ];
            }
            case "response.output_item.added":
            case "response.output_item.done":
                TrackFunctionItem(root["item"] as JsonObject, state);
                return [];
            case "response.function_call_arguments.delta":
            {
                var delta = ReadString(root, "delta") ?? string.Empty;
                var itemId = ReadString(root, "item_id") ?? OutputIndexKey(root);
                AppendArguments(itemId, delta, state);
                return
                [
                    new RekallAgeLanguageModelStreamEvent(
                        RekallAgeLanguageModelStreamEventKind.ToolCallDelta,
                        delta)
                ];
            }
            case "response.function_call_arguments.done":
            {
                var itemId = ReadString(root, "item_id") ?? OutputIndexKey(root);
                ReplaceArguments(itemId, ReadString(root, "arguments") ?? string.Empty, state);
                return [];
            }
            case "response.completed":
            case "response.incomplete":
            {
                var responseObject = root["response"] as JsonObject
                    ?? throw StreamError(
                        "REKALL_OPENAI_STREAM_INVALID",
                        "OpenAI completion event omitted its response object.");
                var response = RekallAgeOpenAiLanguageModelClient.MapResponse(
                    responseObject,
                    _toolNameMap,
                    _sensitiveValues,
                    _requestId);
                ValidateFinalAccumulations(response, state);
                state.Completed = true;
                return
                [
                    new RekallAgeLanguageModelStreamEvent(
                        RekallAgeLanguageModelStreamEventKind.Usage,
                        string.Empty),
                    new RekallAgeLanguageModelStreamEvent(
                        RekallAgeLanguageModelStreamEventKind.Completed,
                        string.Empty,
                        response)
                ];
            }
            case "error":
                throw ProviderEventError(root);
            case "response.failed":
                throw ProviderEventError((root["response"] as JsonObject)?["error"] as JsonObject ?? root);
            default:
                return [];
        }
    }

    private void TrackFunctionItem(JsonObject? item, StreamState state)
    {
        if (ReadString(item, "type") != "function_call")
        {
            return;
        }

        var itemId = ReadString(item, "id");
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        ReplaceArguments(itemId, ReadString(item, "arguments") ?? string.Empty, state);
    }

    private void AppendArguments(string itemId, string delta, StreamState state)
    {
        state.ArgumentLengths.TryGetValue(itemId, out var existingLength);
        var newLength = checked(existingLength + delta.Length);
        ReplaceArgumentLength(itemId, existingLength, newLength, state);
    }

    private void ReplaceArguments(string itemId, string arguments, StreamState state)
    {
        state.ArgumentLengths.TryGetValue(itemId, out var existingLength);
        ReplaceArgumentLength(itemId, existingLength, arguments.Length, state);
    }

    private void ReplaceArgumentLength(
        string itemId,
        int existingLength,
        int newLength,
        StreamState state)
    {
        var newTotal = checked(state.TotalArgumentCharacters - existingLength + newLength);
        if (newTotal > _maximumArgumentCharacters)
        {
            throw StreamError(
                "REKALL_OPENAI_STREAM_TOOL_ARGUMENTS_TOO_LARGE",
                "OpenAI streamed function arguments beyond the configured bound.");
        }

        state.ArgumentLengths[itemId] = newLength;
        state.TotalArgumentCharacters = newTotal;
    }

    private void AddTextCharacters(StreamState state, int characters)
    {
        if (state.TotalTextCharacters > _maximumTextCharacters - characters)
        {
            throw StreamError(
                "REKALL_OPENAI_STREAM_TEXT_TOO_LARGE",
                "OpenAI streamed text beyond the configured bound.");
        }

        state.TotalTextCharacters += characters;
    }

    private void ValidateFinalAccumulations(
        RekallAgeLanguageModelResponse response,
        StreamState state)
    {
        var finalTextCharacters = checked(response.Content.Length + response.Thinking.Length);
        if (finalTextCharacters > _maximumTextCharacters)
        {
            throw StreamError(
                "REKALL_OPENAI_STREAM_TEXT_TOO_LARGE",
                "OpenAI completed with text beyond the configured bound.");
        }

        var finalArgumentCharacters = response.ToolCalls.Sum(call =>
            call.Arguments.ToJsonString().Length);
        if (finalArgumentCharacters > _maximumArgumentCharacters)
        {
            throw StreamError(
                "REKALL_OPENAI_STREAM_TOOL_ARGUMENTS_TOO_LARGE",
                "OpenAI completed with function arguments beyond the configured bound.");
        }
    }

    private RekallAgeLanguageModelProviderException ProviderEventError(JsonObject error)
    {
        var code = RekallAgeOpenAiLanguageModelClient.ProviderCode(ReadString(error, "code"), null);
        return new RekallAgeLanguageModelProviderException(
            code,
            "openai",
            "OpenAI stream reported a provider error.",
            requestId: _requestId,
            sensitiveValues: _sensitiveValues);
    }

    private async IAsyncEnumerable<string> ReadBoundedLinesAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4_096);
        var line = new ArrayBufferWriter<byte>();
        var consumeLf = false;
        try
        {
            while (true)
            {
                var bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                for (var index = 0; index < bytesRead; index++)
                {
                    var value = buffer[index];
                    if (consumeLf)
                    {
                        consumeLf = false;
                        if (value == (byte)'\n')
                        {
                            continue;
                        }
                    }

                    if (value is (byte)'\r' or (byte)'\n')
                    {
                        yield return DecodeLine(line.WrittenSpan);
                        line = new ArrayBufferWriter<byte>();
                        consumeLf = value == (byte)'\r';
                        continue;
                    }

                    if (line.WrittenCount >= _maximumTerminalEventCharacters)
                    {
                        throw StreamError(
                            "REKALL_OPENAI_STREAM_EVENT_TOO_LARGE",
                            "OpenAI streamed an SSE line beyond the configured bound.");
                    }

                    line.GetSpan(1)[0] = value;
                    line.Advance(1);
                }
            }

            if (line.WrittenCount > 0)
            {
                yield return DecodeLine(line.WrittenSpan);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private string DecodeLine(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw StreamError(
                "REKALL_OPENAI_STREAM_INVALID",
                "OpenAI streamed invalid UTF-8 data.");
        }
    }

    private RekallAgeLanguageModelProviderException StreamError(string code, string message) =>
        new(
            code,
            "openai",
            message,
            requestId: _requestId,
            sensitiveValues: _sensitiveValues);

    private static string OutputIndexKey(JsonObject value) =>
        value["output_index"] is JsonValue node && node.TryGetValue<int>(out var outputIndex)
            ? $"output:{outputIndex}"
            : "output:unknown";

    private static string? ReadString(JsonObject? value, string name) =>
        value?[name] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static int Positive(int value, string parameterName) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

    private sealed class StreamState
    {
        public bool Completed { get; set; }

        public int TotalTextCharacters { get; set; }

        public int TotalArgumentCharacters { get; set; }

        public Dictionary<string, int> ArgumentLengths { get; } = new(StringComparer.Ordinal);
    }
}
