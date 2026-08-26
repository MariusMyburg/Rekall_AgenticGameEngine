using System.Text;
using System.Text.Json;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class OpenAiResponseStreamReaderTests
{
    [Fact]
    public async Task FragmentedCrLfCommentsAndMultilineDataAssembleDeltasCallsAndOneCompletion()
    {
        var map = RekallAgeOpenAiToolNameMap.Create(["rekall.scene.inspect"]);
        var reader = new RekallAgeOpenAiResponseStreamReader(map, [], "req_stream_1");
        await using var stream = FragmentedStream(SuccessfulSse(), maximumReadBytes: 1);

        var events = await ReadAllAsync(reader, stream, CancellationToken.None);

        Assert.Equal(
        [
            RekallAgeLanguageModelStreamEventKind.TextDelta,
            RekallAgeLanguageModelStreamEventKind.TextDelta,
            RekallAgeLanguageModelStreamEventKind.ThinkingDelta,
            RekallAgeLanguageModelStreamEventKind.ToolCallDelta,
            RekallAgeLanguageModelStreamEventKind.ToolCallDelta,
            RekallAgeLanguageModelStreamEventKind.Usage,
            RekallAgeLanguageModelStreamEventKind.Completed
        ],
            events.Select(streamEvent => streamEvent.Kind));
        Assert.Equal("Hel", events[0].Text);
        Assert.Equal("lo", events[1].Text);
        Assert.Equal("think", events[2].Text);
        Assert.Equal("{\"detail\":", events[3].Text);
        Assert.Equal("true}", events[4].Text);
        var completedEvents = events.Where(streamEvent =>
            streamEvent.Kind == RekallAgeLanguageModelStreamEventKind.Completed).ToArray();
        var completed = Assert.Single(completedEvents).Response!;
        Assert.Equal("resp_stream_1", completed.ResponseId);
        Assert.Equal("Hello", completed.Content);
        Assert.Equal("think", completed.Thinking);
        Assert.Equal(5, completed.Usage.PromptTokens);
        Assert.Equal(2, completed.Usage.CompletionTokens);
        Assert.Equal(3, completed.Usage.CachedInputTokens);
        Assert.Equal(1, completed.Usage.ReasoningTokens);
        var call = Assert.Single(completed.ToolCalls);
        Assert.Equal("rekall.scene.inspect", call.Name);
        Assert.Equal("call_stream_1", call.Id);
        Assert.True(call.Arguments["detail"]!.GetValue<bool>());
    }

    [Fact]
    public async Task MalformedEventJsonReturnsStableProviderError()
    {
        var reader = Reader();
        await using var stream = FragmentedStream("data: {not-json}\n\n", maximumReadBytes: 2);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_STREAM_INVALID", error.Code);
        Assert.Equal("req_stream_test", error.RequestId);
        Assert.DoesNotContain("not-json", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderErrorEventDoesNotExposeProviderMessageOrSensitiveValues()
    {
        const string secret = "private-stream-content";
        var map = RekallAgeOpenAiToolNameMap.Create([]);
        var reader = new RekallAgeOpenAiResponseStreamReader(map, [secret], "req_stream_error");
        await using var stream = FragmentedStream($$"""
            data: {"type":"error","code":"rate_limit_exceeded","message":"rejected {{secret}}"}

            """);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_RATE_LIMITED", error.Code);
        Assert.Equal("req_stream_error", error.RequestId);
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("rejected", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EndOfFileBeforeCompletionReturnsStableProviderError()
    {
        var reader = Reader();
        await using var stream = FragmentedStream(
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_STREAM_PREMATURE_EOF", error.Code);
    }

    [Fact]
    public async Task DuplicateCompletionReturnsStableProviderError()
    {
        var completed = CompletionEvent("resp_first") + CompletionEvent("resp_second");
        var reader = Reader();
        await using var stream = FragmentedStream(completed);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_STREAM_DUPLICATE_COMPLETION", error.Code);
    }

    [Fact]
    public async Task EventAfterCompletionReturnsStableProviderError()
    {
        var reader = Reader();
        await using var stream = FragmentedStream(
            CompletionEvent("resp_complete")
            + "data: {\"type\":\"response.output_text.delta\",\"delta\":\"late\"}\n\n");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_STREAM_AFTER_COMPLETION", error.Code);
    }

    [Fact]
    public async Task InvalidUtf8ReturnsStableProviderError()
    {
        var prefix = Encoding.ASCII.GetBytes("data: {\"type\":\"response.output_text.delta\",\"delta\":\"");
        var suffix = Encoding.ASCII.GetBytes("\"}\n\n");
        var bytes = prefix.Concat(new byte[] { 0xC3, 0x28 }).Concat(suffix).ToArray();
        var reader = Reader();
        await using var stream = new MemoryStream(bytes, writable: false);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_STREAM_INVALID", error.Code);
    }

    [Fact]
    public async Task ResponseFailedReturnsStructuredProviderErrorWithoutMessageText()
    {
        const string providerMessage = "private provider failure detail";
        var reader = Reader();
        await using var stream = FragmentedStream(
            "data: {\"type\":\"response.failed\",\"response\":{\"id\":\"resp_failed\",\"status\":\"failed\",\"error\":{\"code\":\"server_error\",\"message\":"
            + JsonSerializer.Serialize(providerMessage)
            + "}}}\n\n");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_UNAVAILABLE", error.Code);
        Assert.Equal(providerMessage, error.ProviderDetail);
        Assert.DoesNotContain(providerMessage, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseIncompleteCompletesWithProviderReasonAndPartialContent()
    {
        var reader = Reader();
        await using var stream = FragmentedStream("""
            data: {"type":"response.incomplete","response":{"id":"resp_incomplete","model":"gpt-5.6-sol","status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[{"id":"msg_incomplete","type":"message","role":"assistant","status":"incomplete","content":[{"type":"output_text","text":"partial"}]}],"usage":{"input_tokens":2,"output_tokens":3,"total_tokens":5}}}

            """);

        var events = await ReadAllAsync(reader, stream, CancellationToken.None);

        var completed = Assert.Single(events, item => item.Kind == RekallAgeLanguageModelStreamEventKind.Completed);
        Assert.Equal("partial", completed.Response!.Content);
        Assert.Equal("max_output_tokens", completed.Response.FinishReason);
    }

    [Fact]
    public async Task FunctionArgumentDoneAndOutputItemDoneProduceCompletedParallelCalls()
    {
        var map = RekallAgeOpenAiToolNameMap.Create(
            ["rekall.scene.inspect", "rekall.context.engine_status"]);
        var reader = new RekallAgeOpenAiResponseStreamReader(map, [], "req_done_shapes");
        await using var stream = FragmentedStream("""
            data: {"type":"response.output_item.added","output_index":0,"item":{"id":"fc_done_alpha","type":"function_call","call_id":"call_done_alpha","name":"rekall_scene_inspect_d7c351b75103","arguments":""}}

            data: {"type":"response.function_call_arguments.delta","item_id":"fc_done_alpha","output_index":0,"delta":"{\"detail\":"}

            data: {"type":"response.function_call_arguments.done","item_id":"fc_done_alpha","output_index":0,"arguments":"{\"detail\":true}"}

            data: {"type":"response.output_item.done","output_index":0,"item":{"id":"fc_done_alpha","type":"function_call","call_id":"call_done_alpha","name":"rekall_scene_inspect_d7c351b75103","arguments":"{\"detail\":true}","status":"completed"}}

            data: {"type":"response.output_item.done","output_index":1,"item":{"id":"fc_done_beta","type":"function_call","call_id":"call_done_beta","name":"rekall_context_engine_status_8179b61222fc","arguments":"{}","status":"completed"}}

            data: {"type":"response.completed","response":{"id":"resp_done_shapes","model":"gpt-5.6-sol","status":"completed","output":[{"id":"fc_done_alpha","type":"function_call","call_id":"call_done_alpha","name":"rekall_scene_inspect_d7c351b75103","arguments":"{\"detail\":true}","status":"completed"},{"id":"fc_done_beta","type":"function_call","call_id":"call_done_beta","name":"rekall_context_engine_status_8179b61222fc","arguments":"{}","status":"completed"}],"usage":{"input_tokens":2,"output_tokens":4,"total_tokens":6}}}

            """);

        var events = await ReadAllAsync(reader, stream, CancellationToken.None);

        Assert.Equal("{\"detail\":", Assert.Single(
            events,
            item => item.Kind == RekallAgeLanguageModelStreamEventKind.ToolCallDelta).Text);
        var completed = Assert.Single(events, item => item.Kind == RekallAgeLanguageModelStreamEventKind.Completed);
        Assert.Collection(
            completed.Response!.ToolCalls,
            call =>
            {
                Assert.Equal("call_done_alpha", call.Id);
                Assert.Equal("rekall.scene.inspect", call.Name);
                Assert.True(call.Arguments["detail"]!.GetValue<bool>());
            },
            call =>
            {
                Assert.Equal("call_done_beta", call.Id);
                Assert.Equal("rekall.context.engine_status", call.Name);
                Assert.Empty(call.Arguments);
            });
    }

    [Fact]
    public async Task OneSseEventCannotExceedConfiguredBound()
    {
        var reader = Reader(maxEventCharacters: 32);
        await using var stream = FragmentedStream($"data: {new string('x', 40)}\n\n");

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_STREAM_EVENT_TOO_LARGE", error.Code);
    }

    [Fact]
    public async Task AccumulatedTextCannotExceedConfiguredBound()
    {
        var reader = Reader(maxTextCharacters: 5);
        await using var stream = FragmentedStream("""
            data: {"type":"response.output_text.delta","delta":"abc"}

            data: {"type":"response.output_text.delta","delta":"def"}

            """);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_STREAM_TEXT_TOO_LARGE", error.Code);
    }

    [Fact]
    public async Task AccumulatedFunctionArgumentsCannotExceedConfiguredBound()
    {
        var reader = Reader(maxArgumentCharacters: 5);
        await using var stream = FragmentedStream("""
            data: {"type":"response.output_item.added","output_index":0,"item":{"id":"fc_bound","type":"function_call","call_id":"call_bound","name":"rekall_scene_inspect_d7c351b75103","arguments":""}}

            data: {"type":"response.function_call_arguments.delta","item_id":"fc_bound","output_index":0,"delta":"123456"}

            """);

        var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
            ReadAllAsync(reader, stream, CancellationToken.None));

        Assert.Equal("REKALL_OPENAI_STREAM_TOOL_ARGUMENTS_TOO_LARGE", error.Code);
    }

    [Fact]
    public async Task ValidCompletionEnvelopeLargerThanOrdinaryEventBoundSucceeds()
    {
        var outputText = new string('x', 262_200);
        var sse =
            "data: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_large\",\"model\":\"gpt-5.6-sol\",\"status\":\"completed\",\"output\":[{\"id\":\"msg_large\",\"type\":\"message\",\"role\":\"assistant\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":"
            + JsonSerializer.Serialize(outputText)
            + "}]}],\"usage\":{\"input_tokens\":1,\"output_tokens\":65550,\"total_tokens\":65551}}}\n\n";
        Assert.True(sse.Length > 262_144);
        var reader = Reader();
        await using var stream = FragmentedStream(sse, maximumReadBytes: 4_096);

        var events = await ReadAllAsync(reader, stream, CancellationToken.None);

        var completed = Assert.Single(events, item => item.Kind == RekallAgeLanguageModelStreamEventKind.Completed);
        Assert.Equal(outputText, completed.Response!.Content);
    }

    [Fact]
    public async Task RefusalDeltaAndCompletedRefusalContentArePreserved()
    {
        var reader = Reader();
        await using var stream = FragmentedStream("""
            data: {"type":"response.refusal.delta","delta":"I cannot "}

            data: {"type":"response.refusal.delta","delta":"comply."}

            data: {"type":"response.completed","response":{"id":"resp_refusal","model":"gpt-5.6-sol","status":"completed","output":[{"id":"msg_refusal","type":"message","role":"assistant","status":"completed","content":[{"type":"refusal","refusal":"I cannot comply."}]}],"usage":{"input_tokens":2,"output_tokens":3,"total_tokens":5}}}

            """);

        var events = await ReadAllAsync(reader, stream, CancellationToken.None);

        Assert.Equal(
            ["I cannot ", "comply."],
            events.Where(item => item.Kind == RekallAgeLanguageModelStreamEventKind.TextDelta)
                .Select(item => item.Text));
        var completed = Assert.Single(events, item => item.Kind == RekallAgeLanguageModelStreamEventKind.Completed);
        Assert.Equal("I cannot comply.", completed.Response!.Content);
    }

    [Fact]
    public async Task CancellationInterruptsAWaitingStreamRead()
    {
        var reader = Reader();
        await using var stream = new BlockingReadStream();
        using var cancellation = new CancellationTokenSource();

        var readTask = ReadAllAsync(reader, stream, cancellation.Token);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
    }

    private static RekallAgeOpenAiResponseStreamReader Reader(
        int maxEventCharacters = 262_144,
        int maxTextCharacters = 4_194_304,
        int maxArgumentCharacters = 4_194_304)
    {
        var map = RekallAgeOpenAiToolNameMap.Create(["rekall.scene.inspect"]);
        return new RekallAgeOpenAiResponseStreamReader(
            map,
            [],
            "req_stream_test",
            maxEventCharacters,
            maxTextCharacters,
            maxArgumentCharacters);
    }

    private static async Task<List<RekallAgeLanguageModelStreamEvent>> ReadAllAsync(
        RekallAgeOpenAiResponseStreamReader reader,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var events = new List<RekallAgeLanguageModelStreamEvent>();
        await foreach (var streamEvent in reader.ReadAsync(stream, cancellationToken))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static Stream FragmentedStream(string value, int maximumReadBytes = 3) =>
        new FragmentedReadStream(Encoding.UTF8.GetBytes(value), maximumReadBytes);

    private static string SuccessfulSse() => """
        : keep-alive
        event: response.output_text.delta
        data: {"type":"response.output_text.delta",
        data: "delta":"Hel"}

        data:{"type":"response.output_text.delta","delta":"lo"}

        data: {"type":"response.reasoning_summary_text.delta","delta":"think"}

        data: {"type":"response.output_item.added","output_index":1,"item":{"id":"fc_stream_1","type":"function_call","call_id":"call_stream_1","name":"rekall_scene_inspect_d7c351b75103","arguments":""}}

        data: {"type":"response.function_call_arguments.delta","item_id":"fc_stream_1","output_index":1,"delta":"{\"detail\":"}

        data: {"type":"response.function_call_arguments.delta","item_id":"fc_stream_1","output_index":1,"delta":"true}"}

        data: {"type":"response.completed","response":{"id":"resp_stream_1","model":"gpt-5.6-sol","status":"completed","output":[{"id":"rs_stream_1","type":"reasoning","summary":[{"type":"summary_text","text":"think"}]},{"id":"msg_stream_1","type":"message","role":"assistant","content":[{"type":"output_text","text":"Hello"}]},{"id":"fc_stream_1","type":"function_call","call_id":"call_stream_1","name":"rekall_scene_inspect_d7c351b75103","arguments":"{\"detail\":true}"}],"usage":{"input_tokens":5,"input_tokens_details":{"cached_tokens":3},"output_tokens":2,"output_tokens_details":{"reasoning_tokens":1},"total_tokens":7}}}

        """;

    private static string CompletionEvent(string responseId) => """
        data: {"type":"response.completed","response":{"id":"$RESPONSE_ID$","model":"gpt-5.6-sol","status":"completed","output":[],"usage":{"input_tokens":0,"output_tokens":0,"total_tokens":0}}}

        """.Replace("$RESPONSE_ID$", responseId, StringComparison.Ordinal) + "\n";

    private sealed class FragmentedReadStream(byte[] bytes, int maximumReadBytes) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumReadBytes)], cancellationToken);
    }

    private sealed class BlockingReadStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

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
