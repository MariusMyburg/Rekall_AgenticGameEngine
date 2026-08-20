using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Rekall.Age.Modules.Hosting;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleHostProtocolTests
{
    [Fact]
    public async Task FrameRoundTripsDeterministicUtf8WithLittleEndianLength()
    {
        var envelope = RekallAgeModuleHostEnvelope.Request(
            1,
            RekallAgeModuleHostOperations.Initialize,
            new RekallAgeModuleHostInitializeRequest("staging/load-plan.json"));
        await using var stream = new MemoryStream();

        await new RekallAgeModuleHostFrameCodec().WriteAsync(stream, envelope, CancellationToken.None);

        var bytes = stream.ToArray();
        Assert.Equal(bytes.Length - sizeof(int), BinaryPrimitives.ReadInt32LittleEndian(bytes));
        Assert.Contains("staging/load-plan.json", Encoding.UTF8.GetString(bytes.AsSpan(sizeof(int))));
        stream.Position = 0;
        var result = await new RekallAgeModuleHostFrameCodec().ReadAsync(stream, CancellationToken.None);
        Assert.Equal(envelope.ProtocolVersion, result.ProtocolVersion);
        Assert.Equal(envelope.Sequence, result.Sequence);
        Assert.Equal(envelope.Operation, result.Operation);
        Assert.Equal("staging/load-plan.json", result.DeserializePayload<RekallAgeModuleHostInitializeRequest>().LoadPlanPath);
    }

    [Fact]
    public async Task FrameReaderHandlesOneByteReads()
    {
        await using var encoded = new MemoryStream();
        await new RekallAgeModuleHostFrameCodec().WriteAsync(
            encoded,
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Shutdown, new { }),
            CancellationToken.None);
        await using var partial = new OneByteReadStream(encoded.ToArray());

        var result = await new RekallAgeModuleHostFrameCodec().ReadAsync(partial, CancellationToken.None);

        Assert.Equal(RekallAgeModuleHostOperations.Shutdown, result.Operation);
    }

    [Fact]
    public async Task FrameWriterHandlesStreamThatPublishesOneByteAtATime()
    {
        await using var stream = new OneByteWriteStream();
        var envelope = RekallAgeModuleHostEnvelope.Request(
            1,
            RekallAgeModuleHostOperations.Shutdown,
            new { reason = "done" });

        await new RekallAgeModuleHostFrameCodec().WriteAsync(stream, envelope, CancellationToken.None);

        stream.Position = 0;
        var result = await new RekallAgeModuleHostFrameCodec().ReadAsync(stream, CancellationToken.None);
        Assert.Equal(RekallAgeModuleHostOperations.Shutdown, result.Operation);
    }

    [Theory]
    [InlineData(0, "REKALL_MODULE_HOST_PROTOCOL_INVALID")]
    [InlineData(65, "REKALL_MODULE_HOST_MESSAGE_TOO_LARGE")]
    public async Task InvalidOrOversizedLengthFailsWithStableCode(int length, string code)
    {
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        await using var stream = new MemoryStream(prefix);
        var codec = new RekallAgeModuleHostFrameCodec(maximumMessageBytes: 64);

        var exception = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await codec.ReadAsync(stream, CancellationToken.None));

        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task TruncatedFrameAndTrailingJsonFailClosed()
    {
        var truncated = Frame(Encoding.UTF8.GetBytes("{}"), declaredLength: 8);
        var trailing = Frame(Encoding.UTF8.GetBytes(
            "{\"protocolVersion\":1,\"sequence\":1,\"operation\":\"host.shutdown\",\"payload\":{}}x"));

        var truncatedError = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await new RekallAgeModuleHostFrameCodec().ReadAsync(new MemoryStream(truncated), CancellationToken.None));
        var trailingError = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await new RekallAgeModuleHostFrameCodec().ReadAsync(new MemoryStream(trailing), CancellationToken.None));

        Assert.Equal("REKALL_MODULE_HOST_PROTOCOL_INVALID", truncatedError.Code);
        Assert.Equal("REKALL_MODULE_HOST_PROTOCOL_INVALID", trailingError.Code);
    }

    [Fact]
    public async Task DepthVersionOperationAndSequenceAreValidated()
    {
        var codec = new RekallAgeModuleHostFrameCodec(maximumJsonDepth: 3);
        var deep = Frame(Encoding.UTF8.GetBytes(
            "{\"protocolVersion\":1,\"sequence\":1,\"operation\":\"host.shutdown\",\"payload\":{\"a\":{\"b\":{\"c\":1}}}}"));
        var badVersion = EncodeUnchecked(RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Shutdown, new { }) with
        {
            ProtocolVersion = 99
        });
        var badOperation = EncodeUnchecked(RekallAgeModuleHostEnvelope.Request(1, "host.unknown", new { }));

        foreach (var bytes in new[] { deep, badVersion, badOperation })
        {
            var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
                await new RekallAgeModuleHostFrameCodec(maximumJsonDepth: 3)
                    .ReadAsync(new MemoryStream(bytes), CancellationToken.None));
            Assert.Equal("REKALL_MODULE_HOST_PROTOCOL_INVALID", error.Code);
        }

        var first = Encode(RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Shutdown, new { }));
        var duplicate = Encode(RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Shutdown, new { }));
        await using var sequences = new MemoryStream(first.Concat(duplicate).ToArray());
        await codec.ReadAsync(sequences, CancellationToken.None);
        var sequenceError = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await codec.ReadAsync(sequences, CancellationToken.None));
        Assert.Equal("REKALL_MODULE_HOST_PROTOCOL_INVALID", sequenceError.Code);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToProtocolFailure()
    {
        await using var stream = new NeverCompletingReadStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new RekallAgeModuleHostFrameCodec().ReadAsync(stream, cancellation.Token));
    }

    [Fact]
    public async Task MissingFieldsAndInvalidUtf8FailClosed()
    {
        var missingPayload = Frame(Encoding.UTF8.GetBytes(
            "{\"protocolVersion\":1,\"sequence\":1,\"operation\":\"host.shutdown\"}"));
        var invalidUtf8 = Frame([0xff, 0xfe, 0xfd]);

        foreach (var bytes in new[] { missingPayload, invalidUtf8 })
        {
            var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
                await new RekallAgeModuleHostFrameCodec().ReadAsync(
                    new MemoryStream(bytes),
                    CancellationToken.None));
            Assert.Equal("REKALL_MODULE_HOST_PROTOCOL_INVALID", error.Code);
        }
    }

    [Fact]
    public async Task WriterRejectsOversizedPayloadBeforePublishingAFrame()
    {
        await using var stream = new MemoryStream();
        var envelope = RekallAgeModuleHostEnvelope.Request(
            1,
            RekallAgeModuleHostOperations.Shutdown,
            new { data = new string('x', 128) });

        var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await new RekallAgeModuleHostFrameCodec(maximumMessageBytes: 64)
                .WriteAsync(stream, envelope, CancellationToken.None));

        Assert.Equal("REKALL_MODULE_HOST_MESSAGE_TOO_LARGE", error.Code);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task SuccessAndBoundedFailureEnvelopesRoundTrip()
    {
        var success = RekallAgeModuleHostEnvelope.Success(
            1,
            RekallAgeModuleHostOperations.PlayableCreate,
            new RekallAgeModuleHostPlayableCreateResponse("fixture"));
        var failure = RekallAgeModuleHostEnvelope.Failure(
            2,
            RekallAgeModuleHostOperations.PlayableTick,
            new RekallAgeModuleHostError("REKALL_MODULE_HOST_MODULE_REJECTED", "InvalidOperationException", "bounded", "fixture"));
        await using var stream = new MemoryStream();
        var writer = new RekallAgeModuleHostFrameCodec();
        await writer.WriteAsync(stream, success, CancellationToken.None);
        await writer.WriteAsync(stream, failure, CancellationToken.None);
        stream.Position = 0;
        var reader = new RekallAgeModuleHostFrameCodec();

        var readSuccess = await reader.ReadAsync(stream, CancellationToken.None);
        var readFailure = await reader.ReadAsync(stream, CancellationToken.None);

        Assert.True(readSuccess.Ok is true);
        Assert.Equal("fixture", readSuccess.DeserializePayload<RekallAgeModuleHostPlayableCreateResponse>().Kind);
        Assert.True(readFailure.Ok is false);
        Assert.Equal("REKALL_MODULE_HOST_MODULE_REJECTED", readFailure.Error!.Code);
    }

    [Fact]
    public async Task DuplicateRootFieldsAndInconsistentResponseShapeFailClosed()
    {
        var duplicate = Frame(Encoding.UTF8.GetBytes(
            "{\"protocolVersion\":1,\"protocolVersion\":1,\"sequence\":1,\"operation\":\"host.shutdown\",\"payload\":{}}"));
        var falseWithoutError = EncodeUnchecked(
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Shutdown, new { }) with { Ok = false });

        foreach (var bytes in new[] { duplicate, falseWithoutError })
        {
            var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
                await new RekallAgeModuleHostFrameCodec().ReadAsync(
                    new MemoryStream(bytes),
                    CancellationToken.None));
            Assert.Equal("REKALL_MODULE_HOST_PROTOCOL_INVALID", error.Code);
        }
    }

    [Fact]
    public void TypedPayloadDecodeFailureUsesStableBoundaryCode()
    {
        var envelope = RekallAgeModuleHostEnvelope.Request(
            1,
            RekallAgeModuleHostOperations.Initialize,
            new { loadPlanPath = 42 });

        var error = Assert.Throws<RekallAgeModuleHostException>(() =>
            envelope.DeserializePayload<RekallAgeModuleHostInitializeRequest>());

        Assert.Equal("REKALL_MODULE_HOST_PROTOCOL_INVALID", error.Code);
    }

    private static byte[] Encode(RekallAgeModuleHostEnvelope envelope)
    {
        using var stream = new MemoryStream();
        new RekallAgeModuleHostFrameCodec().WriteAsync(stream, envelope, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return stream.ToArray();
    }

    private static byte[] EncodeUnchecked(RekallAgeModuleHostEnvelope envelope) =>
        Frame(JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static byte[] Frame(byte[] payload, int? declaredLength = null)
    {
        var result = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, declaredLength ?? payload.Length);
        payload.CopyTo(result, sizeof(int));
        return result;
    }

    private sealed class OneByteReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    private sealed class OneByteWriteStream : MemoryStream
    {
        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < buffer.Length; index++)
            {
                await base.WriteAsync(buffer.Slice(index, 1), cancellationToken);
            }
        }
    }

    private sealed class NeverCompletingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromCanceled<int>(cancellationToken);
    }
}
