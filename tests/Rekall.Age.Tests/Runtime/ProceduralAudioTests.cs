using System.Text.Json.Nodes;
using Rekall.Age.Runtime;

namespace Rekall.Age.Tests.Runtime;

/// <summary>
/// Synthesized audio is only useful to an engine that can promise the same samples every run:
/// otherwise nothing about a scene's sound can be asserted on, and a clip cannot be cached.
/// </summary>
public sealed class ProceduralAudioTests
{
    [Fact]
    public void SynthesizerProducesRequestedShapeAndLength()
    {
        var spec = new RekallAgeProceduralAudioSpec(
            Waveform: "saw",
            DurationSeconds: 0.25,
            StartFrequency: 1200,
            EndFrequency: 200,
            SampleRate: 44100);

        var clip = RekallAgeProceduralAudioSynthesizer.Synthesize(spec);

        Assert.Equal(44100, clip.SampleRate);
        Assert.Equal(1, clip.Channels);
        Assert.Equal(11025, clip.FrameCount);
        Assert.Equal(clip.FrameCount, clip.Samples.Count);
        Assert.All(clip.Samples, sample => Assert.InRange(sample, -1f, 1f));
        Assert.Contains(clip.Samples, sample => Math.Abs(sample) > 0.05f);
    }

    [Fact]
    public void SynthesizerIsDeterministicIncludingNoise()
    {
        var spec = new RekallAgeProceduralAudioSpec(
            Waveform: "square",
            DurationSeconds: 0.1,
            NoiseMix: 0.8,
            Seed: 12345);

        var first = RekallAgeProceduralAudioSynthesizer.Synthesize(spec);
        var second = RekallAgeProceduralAudioSynthesizer.Synthesize(spec);

        Assert.Equal(first.Samples, second.Samples);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentNoise()
    {
        var quiet = new RekallAgeProceduralAudioSpec(DurationSeconds: 0.1, NoiseMix: 1, Seed: 1);
        var other = quiet with { Seed = 2 };

        var first = RekallAgeProceduralAudioSynthesizer.Synthesize(quiet);
        var second = RekallAgeProceduralAudioSynthesizer.Synthesize(other);

        Assert.NotEqual(first.Samples, second.Samples);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void EnvelopeOpensAndClosesTheClip()
    {
        var spec = new RekallAgeProceduralAudioSpec(
            Waveform: "sine",
            DurationSeconds: 0.4,
            StartFrequency: 440,
            EndFrequency: 440,
            Attack: 0.05,
            Decay: 0.05,
            Sustain: 0.6,
            Release: 0.1);

        var clip = RekallAgeProceduralAudioSynthesizer.Synthesize(spec);

        // Starts from silence and returns to it, so a one-shot cannot click on either edge.
        Assert.True(Math.Abs(clip.Samples[0]) < 0.02f);
        Assert.True(Math.Abs(clip.Samples[^1]) < 0.02f);

        var middle = clip.Samples.Skip(clip.FrameCount / 3).Take(clip.FrameCount / 3);
        Assert.Contains(middle, sample => Math.Abs(sample) > 0.2f);
    }

    [Fact]
    public void StagesLongerThanTheClipAreScaledToFitRatherThanRejected()
    {
        var spec = new RekallAgeProceduralAudioSpec(
            DurationSeconds: 0.05,
            Attack: 1.0,
            Decay: 1.0,
            Release: 1.0);

        var clip = RekallAgeProceduralAudioSynthesizer.Synthesize(spec);

        Assert.Equal(2205, clip.FrameCount);
        Assert.All(clip.Samples, sample => Assert.InRange(sample, -1f, 1f));
    }

    [Fact]
    public void DurationIsBoundedSoASpecCannotExhaustMemory()
    {
        var spec = new RekallAgeProceduralAudioSpec(DurationSeconds: 100_000, SampleRate: 48000);

        var clip = RekallAgeProceduralAudioSynthesizer.Synthesize(spec);

        Assert.Equal((int)(RekallAgeProceduralAudioSpec.MaximumDurationSeconds * 48000), clip.FrameCount);
    }

    [Fact]
    public void SpecReadsFromComponentPropertiesAndKeysItsOwnCache()
    {
        var properties = new JsonObject
        {
            ["waveform"] = "triangle",
            ["durationSeconds"] = 0.3,
            ["startFrequency"] = 900,
            ["endFrequency"] = 120,
            ["noiseMix"] = 0.25,
            ["seed"] = 7,
        };

        var spec = RekallAgeProceduralAudioSpec.FromJson(properties);

        Assert.Equal("triangle", spec.Waveform);
        Assert.Equal(0.3, spec.DurationSeconds);
        Assert.Equal(900, spec.StartFrequency);
        Assert.Equal(7, spec.Seed);
        Assert.Contains("triangle", spec.CacheKey, StringComparison.Ordinal);
        Assert.NotEqual(spec.CacheKey, (spec with { Seed = 8 }).CacheKey);
    }

    [Fact]
    public void ZeroDurationYieldsAnEmptyClipRatherThanThrowing()
    {
        var clip = RekallAgeProceduralAudioSynthesizer.Synthesize(
            new RekallAgeProceduralAudioSpec(DurationSeconds: 0));

        Assert.Equal(0, clip.FrameCount);
        Assert.Empty(clip.Samples);
    }
}
