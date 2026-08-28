using System.Text;
using Rekall.Age.Runtime;

namespace Rekall.Age.Tests.Runtime;

/// <summary>
/// The engine could only load RIFF/WAVE, so any authored music had to ship uncompressed.
/// MP3 support closes that gap; these tests pin the format probe, which is what decides
/// whether an asset takes the MPEG or the WAVE path.
///
/// The probe deliberately inspects content rather than the file extension, so a mislabelled
/// asset still decodes. Decoding a real MPEG stream is covered by loading an actual track
/// through the player rather than here, since a valid MP3 cannot be synthesised inline.
/// </summary>
public sealed class Mp3DecoderTests
{
    [Fact]
    public void Id3TaggedDataIsRecognisedAsMpeg()
    {
        var data = new byte[64];
        Encoding.ASCII.GetBytes("ID3").CopyTo(data, 0);

        Assert.True(RekallAgeMp3Decoder.LooksLikeMp3(data));
    }

    [Fact]
    public void RawFrameSyncIsRecognisedAsMpeg()
    {
        // 0xFF 0xFB is an MPEG-1 Layer III frame header.
        var data = new byte[] { 0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00 };

        Assert.True(RekallAgeMp3Decoder.LooksLikeMp3(data));
    }

    [Fact]
    public void WaveDataIsNotMistakenForMpeg()
    {
        // A RIFF/WAVE header must keep taking the WAVE path.
        var data = new byte[64];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(data, 8);

        Assert.False(RekallAgeMp3Decoder.LooksLikeMp3(data));
    }

    [Fact]
    public void ReservedLayerEncodingIsNotMistakenForAFrameSync()
    {
        // Eleven set bits followed by the reserved layer encoding is not a real header, so
        // stray 0xFF bytes in arbitrary data must not be read as MPEG audio.
        var data = new byte[] { 0xFF, 0xE1, 0x00, 0x00 };

        Assert.False(RekallAgeMp3Decoder.LooksLikeMp3(data));
    }

    [Fact]
    public void EmptyDataIsRejectedRatherThanDecoded()
    {
        Assert.False(RekallAgeMp3Decoder.LooksLikeMp3([]));
        Assert.Throws<InvalidDataException>(() => RekallAgeMp3Decoder.Decode([], "empty"));
    }

    [Fact]
    public void NonMpegDataFailsWithADiagnosticRatherThanCrashing()
    {
        var data = new byte[512];
        Encoding.ASCII.GetBytes("ID3").CopyTo(data, 0);   // claims to be MP3, carries no frames

        Assert.Throws<InvalidDataException>(() => RekallAgeMp3Decoder.Decode(data, "not-really-mp3"));
    }
}
