using NLayer;

namespace Rekall.Age.Runtime;

/// <summary>
/// Decodes MPEG audio (MP3) into the same <see cref="RekallAgePcmAudioClip"/> the WAV path
/// produces, so everything downstream - buses, emitters, the mixer - is format agnostic.
///
/// The engine could previously only load RIFF/WAVE, which meant any authored music had to be
/// shipped uncompressed. A menu track at 44.1kHz stereo is roughly ten times larger as WAV than
/// as MP3, so this is the difference between a game being able to carry music and not.
///
/// Decoding is fully managed (NLayer), so this adds no native dependency and behaves the same
/// on every backend.
/// </summary>
public static class RekallAgeMp3Decoder
{
    /// <summary>Matches the WAV decoder's ceiling so neither format can exhaust memory.</summary>
    private const int MaximumSampleCount = 512 * 1024 * 1024 / sizeof(float);

    /// <summary>
    /// True when the bytes look like MPEG audio: either an ID3v2 tag, or a frame sync word.
    /// Checked by content rather than file extension so a mislabelled asset still decodes.
    /// </summary>
    public static bool LooksLikeMp3(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 12
            && data[..4].SequenceEqual("RIFF"u8)
            && data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return false;
        }

        if (data.Length >= 3 && data[0] == 'I' && data[1] == 'D' && data[2] == '3')
        {
            return true;
        }

        // Frame sync is eleven set bits; the following bits must not be the reserved
        // layer/version encodings, which is what separates a real header from stray 0xFF bytes.
        for (var index = 0; index + 1 < Math.Min(data.Length, 4096); index++)
        {
            if (data[index] != 0xFF || (data[index + 1] & 0xE0) != 0xE0)
            {
                continue;
            }

            var version = (data[index + 1] >> 3) & 0x03;
            var layer = (data[index + 1] >> 1) & 0x03;
            if (version != 1 && layer != 0)
            {
                return true;
            }
        }

        return false;
    }

    public static RekallAgePcmAudioClip Decode(ReadOnlySpan<byte> data, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (data.IsEmpty)
        {
            throw new InvalidDataException("Audio asset is empty.");
        }

        using var stream = new MemoryStream(data.ToArray(), writable: false);
        MpegFile file;
        try
        {
            file = new MpegFile(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Audio asset is not decodable MPEG audio: {exception.Message}", exception);
        }

        try
        {
            var channels = file.Channels;
            var sampleRate = file.SampleRate;
            if (channels is < 1 or > 2 || sampleRate < 1)
            {
                throw new InvalidDataException(
                    $"Audio asset declares an unsupported MPEG layout: {channels} channel(s) at {sampleRate} Hz.");
            }

            // NLayer yields interleaved float samples, which is already the clip's layout.
            var samples = new List<float>(Math.Min(sampleRate * channels * 8, 1 << 20));
            var buffer = new float[sampleRate * channels];
            int read;
            while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                if (samples.Count + read > MaximumSampleCount)
                {
                    throw new InvalidDataException(
                        $"Audio asset '{id}' exceeds the decoded-sample limit of {MaximumSampleCount} samples.");
                }

                samples.AddRange(buffer.AsSpan(0, read));
            }

            if (samples.Count == 0)
            {
                throw new InvalidDataException($"Audio asset '{id}' decoded to no samples.");
            }

            return new RekallAgePcmAudioClip(
                id,
                sampleRate,
                channels,
                samples.Count / channels,
                samples);
        }
        finally
        {
            file.Dispose();
        }
    }
}
