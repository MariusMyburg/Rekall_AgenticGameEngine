using System.Buffers.Binary;

namespace Rekall.Age.Runtime;

public sealed record RekallAgePcmAudioClip(
    string Id,
    int SampleRate,
    int Channels,
    int FrameCount,
    IReadOnlyList<float> Samples)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);
}

public static class RekallAgeWaveDecoder
{
    private const int MaximumDataBytes = 512 * 1024 * 1024;

    public static RekallAgePcmAudioClip Decode(ReadOnlySpan<byte> data, string id)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Audio asset is not a RIFF/WAVE file.");
        }

        WaveFormat? format = null;
        ReadOnlySpan<byte> sampleData = default;
        var offset = 12;
        while (offset <= data.Length - 8)
        {
            var chunkId = data.Slice(offset, 4);
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            if (chunkLength > int.MaxValue || offset + 8L + chunkLength > data.Length)
            {
                throw new InvalidDataException("WAVE chunk extends beyond the file boundary.");
            }

            var chunk = data.Slice(offset + 8, (int)chunkLength);
            if (chunkId.SequenceEqual("fmt "u8))
            {
                format = ReadFormat(chunk);
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                if (chunk.Length > MaximumDataBytes)
                {
                    throw new InvalidDataException("WAVE sample data exceeds the supported size limit.");
                }

                sampleData = chunk;
            }

            offset += 8 + (int)chunkLength + ((int)chunkLength & 1);
        }

        if (format is null || sampleData.IsEmpty)
        {
            throw new InvalidDataException("WAVE file must contain format and sample-data chunks.");
        }

        var value = format.Value;
        if (sampleData.Length % value.BlockAlign != 0)
        {
            throw new InvalidDataException("WAVE sample data is not aligned to complete frames.");
        }

        var sampleCount = sampleData.Length / (value.BitsPerSample / 8);
        var samples = new float[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var source = sampleData.Slice(index * (value.BitsPerSample / 8));
            samples[index] = value.BitsPerSample switch
            {
                8 => (source[0] - 128) / 128f,
                16 => BinaryPrimitives.ReadInt16LittleEndian(source) / 32768f,
                24 => ReadInt24(source) / 8388608f,
                32 => BinaryPrimitives.ReadInt32LittleEndian(source) / 2147483648f,
                _ => throw new InvalidDataException($"Unsupported PCM bit depth {value.BitsPerSample}.")
            };
        }

        return new RekallAgePcmAudioClip(
            id,
            value.SampleRate,
            value.Channels,
            sampleData.Length / value.BlockAlign,
            samples);
    }

    private static WaveFormat ReadFormat(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 16)
        {
            throw new InvalidDataException("WAVE format chunk is incomplete.");
        }

        var encoding = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
        var byteRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[8..]);
        var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]);
        var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
        if (encoding != 1 || channels is < 1 or > 8 || sampleRate is < 8_000 or > 192_000 ||
            bitsPerSample is not (8 or 16 or 24 or 32) ||
            blockAlign != channels * (bitsPerSample / 8) ||
            byteRate != sampleRate * blockAlign)
        {
            throw new InvalidDataException("WAVE format is unsupported or internally inconsistent.");
        }

        return new WaveFormat(sampleRate, channels, bitsPerSample, blockAlign);
    }

    private static int ReadInt24(ReadOnlySpan<byte> source)
    {
        var value = source[0] | source[1] << 8 | source[2] << 16;
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xff000000);
    }

    private readonly record struct WaveFormat(int SampleRate, int Channels, int BitsPerSample, int BlockAlign);
}
