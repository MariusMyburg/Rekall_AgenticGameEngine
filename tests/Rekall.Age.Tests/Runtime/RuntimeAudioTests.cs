using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeAudioTests
{
    [Fact]
    public void PcmWaveDecoderReadsValidatedSixteenBitSamples()
    {
        var bytes = CreatePcm16Wave(8_000, 1, [short.MinValue, -1, 0, short.MaxValue]);

        var clip = RekallAgeWaveDecoder.Decode(bytes, "test-tone");

        Assert.Equal("test-tone", clip.Id);
        Assert.Equal(8_000, clip.SampleRate);
        Assert.Equal(1, clip.Channels);
        Assert.Equal(4, clip.FrameCount);
        Assert.Equal(0.0005, clip.Duration.TotalSeconds, precision: 7);
        Assert.Equal(-1, clip.Samples[0]);
        Assert.InRange(clip.Samples[1], -0.00004f, 0);
        Assert.Equal(0, clip.Samples[2]);
        Assert.InRange(clip.Samples[3], 0.9999f, 1);
    }

    [Fact]
    public async Task AudioSystemAdvancesVoiceAndProjectsSpatialBusMixState()
    {
        var root = TestPaths.CreateTempDirectory();
        var audioDirectory = Path.Combine(root, "Assets", "audio");
        Directory.CreateDirectory(audioDirectory);
        var clipPath = Path.Combine(audioDirectory, "tone.wav");
        await File.WriteAllBytesAsync(
            clipPath,
            CreatePcm16Wave(8_000, 1, Enumerable.Repeat((short)12_000, 8_000).ToArray()));
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset-tone",
                    "tone",
                    "Tone",
                    "audio",
                    string.Empty,
                    "Assets/audio/tone.wav",
                    "test")
            ]),
            CancellationToken.None);
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "audio"])
            .AddEntity(RekallAgeEntityDocument.Create("Listener", [])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.AudioListener", new JsonObject())))
            .AddEntity(RekallAgeEntityDocument.Create("Effects", [])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AudioBus",
                    new JsonObject { ["name"] = "effects", ["gain"] = 0.5 })))
            .AddEntity(RekallAgeEntityDocument.Create("Tone", [])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AudioEmitter",
                    new JsonObject
                    {
                        ["clip"] = "asset-tone",
                        ["bus"] = "effects",
                        ["gain"] = 0.8,
                        ["spatial"] = true,
                        ["referenceDistance"] = 1,
                        ["maxDistance"] = 20,
                        ["playOnStart"] = true
                    })));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root)
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), 30, CancellationToken.None);

        var voice = Assert.Single(result.World.Subsystems.Audio.Voices);
        Assert.Equal("asset-tone", voice.ClipAssetId);
        Assert.Equal("effects", voice.Bus);
        Assert.Equal("playing", voice.State);
        Assert.Equal(0.5, voice.PlaybackSeconds, precision: 3);
        Assert.Equal(1, voice.DurationSeconds, precision: 3);
        Assert.True(voice.RightGain > voice.LeftGain);
        Assert.InRange(voice.RightGain, 0, 0.4);
        Assert.Equal(1, result.World.Subsystems.Audio.MixFrame.ActiveVoiceCount);
        Assert.True(result.World.Subsystems.Audio.MixFrame.PeakGain > 0);
        Assert.Equal(48_000, result.World.Subsystems.Audio.MixFrame.SampleRate);
        var mixedSamples = Assert.IsAssignableFrom<IReadOnlyList<float>>(
            result.World.Subsystems.Audio.MixFrame.Samples);
        Assert.Equal(1_600, mixedSamples.Count);
        Assert.Contains(mixedSamples, sample => sample != 0);
        Assert.Contains(result.World.SystemsRun, system => system == "runtime.audio");
        Assert.DoesNotContain(result.World.Observations, observation =>
            observation.Code is "REKALL_AUDIO_ASSET_MISSING" or "REKALL_AUDIO_CLIP_INVALID");

        var clockLoop = RekallAgeRuntimeExecutionLoop.CreateDefault(root);
        var clock = new RekallAgeRuntimeSimulationClock(clockLoop, TimeSpan.Zero);
        var advanced = await clock.AdvanceByAsync(
            new RekallAgeRuntimeWorldBuilder().Build(scene),
            TimeSpan.FromSeconds(1.0 / 30.0),
            CancellationToken.None);
        Assert.Equal(2, advanced.StepsSimulated);
        Assert.Equal(2, advanced.AudioFrames.Count);
        Assert.All(advanced.AudioFrames, frame => Assert.Equal(1_600, frame.Samples!.Count));
    }

    private static byte[] CreatePcm16Wave(int sampleRate, short channels, IReadOnlyList<short> samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataLength = samples.Count * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * sizeof(short));
        writer.Write((short)(channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
