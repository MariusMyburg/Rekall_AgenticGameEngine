using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Runtime;

/// <summary>
/// A described sound, rather than a recorded one: an oscillator, a pitch sweep, a noise mix and
/// an amplitude envelope.
///
/// Deliberately a generic synthesis primitive and not a library of named effects. "Laser",
/// "explosion" and "pickup" are things a game decides; an engine that shipped those would have
/// picked a genre. What the engine owes an author is the means to describe a sound precisely
/// and get the same samples back every time.
/// </summary>
public sealed record RekallAgeProceduralAudioSpec(
    string Waveform = "sine",
    double DurationSeconds = 0.25,
    double StartFrequency = 880,
    double EndFrequency = 220,
    string Sweep = "exponential",
    double Attack = 0.005,
    double Decay = 0.08,
    double Sustain = 0.35,
    double Release = 0.12,
    double NoiseMix = 0,
    int Harmonics = 1,
    double Amplitude = 0.7,
    int Seed = 1,
    int SampleRate = 44100)
{
    /// <summary>A minute of audio is far past any sound effect and well short of a memory risk.</summary>
    public const double MaximumDurationSeconds = 60;

    public const int MinimumSampleRate = 8000;
    public const int MaximumSampleRate = 48000;
    public const int MaximumHarmonics = 16;

    /// <summary>
    /// Frequencies are clamped rather than rejected. An author sweeping to zero wants silence
    /// at the end of the sweep, not a failed scene.
    /// </summary>
    public const double MinimumFrequency = 1;

    public const double MaximumFrequency = 20000;

    public static RekallAgeProceduralAudioSpec FromJson(JsonObject properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return new RekallAgeProceduralAudioSpec(
            ReadString(properties, "waveform") ?? "sine",
            ReadNumber(properties, "durationSeconds", 0.25),
            ReadNumber(properties, "startFrequency", 880),
            ReadNumber(properties, "endFrequency", 220),
            ReadString(properties, "sweep") ?? "exponential",
            ReadNumber(properties, "attack", 0.005),
            ReadNumber(properties, "decay", 0.08),
            ReadNumber(properties, "sustain", 0.35),
            ReadNumber(properties, "release", 0.12),
            ReadNumber(properties, "noiseMix", 0),
            (int)Math.Round(ReadNumber(properties, "harmonics", 1)),
            ReadNumber(properties, "amplitude", 0.7),
            (int)Math.Round(ReadNumber(properties, "seed", 1)),
            (int)Math.Round(ReadNumber(properties, "sampleRate", 44100)));
    }

    /// <summary>
    /// Stable identity for a spec, so the same described sound is synthesized once and reused
    /// however many emitters ask for it.
    /// </summary>
    public string CacheKey => string.Join(
        '|',
        "procedural",
        Waveform,
        DurationSeconds,
        StartFrequency,
        EndFrequency,
        Sweep,
        Attack,
        Decay,
        Sustain,
        Release,
        NoiseMix,
        Harmonics,
        Amplitude,
        Seed,
        SampleRate);

    private static string? ReadString(JsonObject properties, string name) =>
        properties[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// Tolerant of how the number was written. A JSON 900 is int-backed and does not satisfy
    /// TryGetValue&lt;double&gt;, so a strict read would silently ignore every whole-number value
    /// an author wrote and quietly substitute the default.
    /// </summary>
    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (properties[name] is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        if (value.TryGetValue<long>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        // Last resort, and the one that actually catches JSON integers: TryGetValue only
        // succeeds when the node's backing type matches exactly, so a JsonValue<int> satisfies
        // neither the double nor the long attempt above. Its JSON text always parses.
        return value.GetValueKind() == JsonValueKind.Number
            && double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fromJson)
                ? fromJson
                : fallback;
    }
}

/// <summary>
/// Turns a <see cref="RekallAgeProceduralAudioSpec"/> into PCM samples.
///
/// Fully deterministic, including the noise: the same spec yields byte-identical samples on
/// every machine and every run, which is what lets a scene's audio be asserted on at all.
/// </summary>
public static class RekallAgeProceduralAudioSynthesizer
{
    public static RekallAgePcmAudioClip Synthesize(RekallAgeProceduralAudioSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var sampleRate = Math.Clamp(
            spec.SampleRate,
            RekallAgeProceduralAudioSpec.MinimumSampleRate,
            RekallAgeProceduralAudioSpec.MaximumSampleRate);
        var duration = Math.Clamp(spec.DurationSeconds, 0, RekallAgeProceduralAudioSpec.MaximumDurationSeconds);
        var frameCount = (int)Math.Round(duration * sampleRate);
        if (frameCount <= 0)
        {
            return new RekallAgePcmAudioClip(spec.CacheKey, sampleRate, 1, 0, []);
        }

        var startFrequency = ClampFrequency(spec.StartFrequency);
        var endFrequency = ClampFrequency(spec.EndFrequency);
        var harmonics = Math.Clamp(spec.Harmonics, 1, RekallAgeProceduralAudioSpec.MaximumHarmonics);
        var noiseMix = Math.Clamp(spec.NoiseMix, 0, 1);
        var amplitude = Math.Clamp(spec.Amplitude, 0, 1);

        var samples = new float[frameCount];
        var noise = new DeterministicNoise(spec.Seed);

        // Phase is integrated rather than computed from t * frequency. With a sweeping
        // frequency the latter is not the same thing and produces an audible discontinuity;
        // integrating keeps the waveform continuous through the sweep.
        var phase = 0.0;

        for (var index = 0; index < frameCount; index++)
        {
            var position = (double)index / frameCount;
            var frequency = Interpolate(startFrequency, endFrequency, position, spec.Sweep);
            phase += 2 * Math.PI * frequency / sampleRate;
            if (phase > 2 * Math.PI)
            {
                phase -= 2 * Math.PI * Math.Floor(phase / (2 * Math.PI));
            }

            var tone = 0.0;
            var weight = 0.0;
            for (var harmonic = 1; harmonic <= harmonics; harmonic++)
            {
                // Each harmonic is quieter than the last, or a stack of them just clips.
                var harmonicWeight = 1.0 / harmonic;
                tone += Oscillator(spec.Waveform, phase * harmonic) * harmonicWeight;
                weight += harmonicWeight;
            }

            tone /= weight;

            var value = ((1 - noiseMix) * tone) + (noiseMix * noise.Next());
            samples[index] = (float)Math.Clamp(
                value * Envelope(index / (double)sampleRate, duration, spec) * amplitude,
                -1.0,
                1.0);
        }

        return new RekallAgePcmAudioClip(spec.CacheKey, sampleRate, 1, frameCount, samples);
    }

    private static double ClampFrequency(double value) => Math.Clamp(
        value,
        RekallAgeProceduralAudioSpec.MinimumFrequency,
        RekallAgeProceduralAudioSpec.MaximumFrequency);

    private static double Interpolate(double from, double to, double position, string sweep) =>
        sweep.Equals("linear", StringComparison.OrdinalIgnoreCase)
            ? from + ((to - from) * position)
            // Exponential by default: pitch is perceived logarithmically, so a linear sweep
            // sounds like it slows down as it descends.
            : from * Math.Pow(to / from, position);

    private static double Oscillator(string waveform, double phase)
    {
        var wrapped = phase % (2 * Math.PI);
        if (wrapped < 0)
        {
            wrapped += 2 * Math.PI;
        }

        return waveform.ToLowerInvariant() switch
        {
            "square" => wrapped < Math.PI ? 1.0 : -1.0,
            "saw" or "sawtooth" => (wrapped / Math.PI) - 1.0,
            "triangle" => 1.0 - (4.0 * Math.Abs((wrapped / (2 * Math.PI)) - 0.5)),
            "noise" => 0.0,
            _ => Math.Sin(wrapped),
        };
    }

    /// <summary>Attack, decay, sustain, release - clamped so the four always fit the duration.</summary>
    private static double Envelope(double time, double duration, RekallAgeProceduralAudioSpec spec)
    {
        var attack = Math.Max(0, spec.Attack);
        var decay = Math.Max(0, spec.Decay);
        var release = Math.Max(0, spec.Release);
        var sustain = Math.Clamp(spec.Sustain, 0, 1);

        // A spec whose stages overrun the clip is scaled to fit rather than rejected: the
        // author asked for a shape, and the shape still makes sense compressed.
        var total = attack + decay + release;
        if (total > duration && total > 0)
        {
            var scale = duration / total;
            attack *= scale;
            decay *= scale;
            release *= scale;
        }

        if (time < attack)
        {
            return attack <= 0 ? 1 : time / attack;
        }

        if (time < attack + decay)
        {
            return decay <= 0 ? sustain : 1 - ((1 - sustain) * ((time - attack) / decay));
        }

        var releaseStart = duration - release;
        if (time >= releaseStart)
        {
            return release <= 0 ? 0 : sustain * Math.Clamp((duration - time) / release, 0, 1);
        }

        return sustain;
    }

    /// <summary>
    /// A small xorshift, not System.Random: the sequence has to be identical across runtimes
    /// and framework versions for a synthesized clip to be reproducible.
    /// </summary>
    private struct DeterministicNoise(int seed)
    {
        private uint _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);

        public double Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return ((_state / (double)uint.MaxValue) * 2.0) - 1.0;
        }
    }
}
