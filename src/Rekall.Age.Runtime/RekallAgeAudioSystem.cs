using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeAudioSystem(string? projectRoot) : IRekallAgeRuntimeWorldSystem
{
    private const string StateComponentType = "Rekall.AudioPlaybackState";
    private readonly string? _projectRoot = string.IsNullOrWhiteSpace(projectRoot) ? null : Path.GetFullPath(projectRoot);
    private readonly RekallAgeAssetCatalogStore _catalogStore = new();
    private IReadOnlyDictionary<string, RekallAgeAssetDocument>? _assets;
    private readonly Dictionary<string, RekallAgePcmAudioClip> _clips = new(StringComparer.Ordinal);

    public string Id => "runtime.audio";

    public int Priority => 0;

    public async ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        await EnsureAssetsAsync(context.CancellationToken);
        var listener = world.Entities.FirstOrDefault(entity =>
            entity.Components.Any(component => component.Type == "Rekall.AudioListener" &&
                ReadBoolean(component.Properties, "active", true)));
        var buses = ReadBuses(world);
        var observations = new List<RekallAgeRuntimeObservation>(world.Observations);
        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            var emitter = entity.Components.FirstOrDefault(component => component.Type == "Rekall.AudioEmitter");
            entities.Add(emitter is null
                ? entity
                : await AdvanceEmitterAsync(entity, emitter, listener, buses, context, observations));
        }

        var mixFrame = BuildMixFrame(entities, context);
        return world with
        {
            Entities = entities,
            Observations = observations,
            Subsystems = world.Subsystems with
            {
                Audio = world.Subsystems.Audio with { MixFrame = mixFrame }
            }
        };
    }

    private async ValueTask<RekallAgeRuntimeEntity> AdvanceEmitterAsync(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent emitter,
        RekallAgeRuntimeEntity? listener,
        IReadOnlyDictionary<string, BusState> buses,
        RekallAgeRuntimeWorldFrameContext context,
        List<RekallAgeRuntimeObservation> observations)
    {
        var existing = entity.Components.FirstOrDefault(component => component.Type == StateComponentType);
        var clipId = ReadString(emitter.Properties, "clip") ?? ReadString(emitter.Properties, "assetId");
        if (string.IsNullOrWhiteSpace(clipId) || _assets is null || !_assets.TryGetValue(clipId, out var asset))
        {
            observations.Add(Observation(context.FrameIndex, "REKALL_AUDIO_ASSET_MISSING", entity,
                string.IsNullOrWhiteSpace(clipId)
                    ? "Audio emitter has no clip asset reference."
                    : $"Audio clip asset '{clipId}' is not present in the project catalog."));
            return ReplaceState(entity, CreateState(clipId ?? string.Empty, "missing", 0, 0, emitter, 0, 0));
        }

        RekallAgePcmAudioClip clip;
        try
        {
            clip = await LoadClipAsync(asset, context.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            observations.Add(Observation(context.FrameIndex, "REKALL_AUDIO_CLIP_INVALID", entity,
                $"Audio clip '{clipId}' could not be decoded: {exception.Message}"));
            return ReplaceState(entity, CreateState(clipId, "invalid", 0, 0, emitter, 0, 0));
        }

        var existingClip = existing is null ? null : ReadString(existing.Properties, "clipAssetId");
        var existingState = existing is null ? null : ReadString(existing.Properties, "state");
        var playback = existing is not null && clipId.Equals(existingClip, StringComparison.Ordinal)
            ? ReadNumber(existing.Properties, "playbackSeconds", 0)
            : 0;
        var hasExplicitPlaying = TryGetPropertyValue(emitter.Properties, "playing", out _);
        var shouldPlay = hasExplicitPlaying
            ? ReadBoolean(emitter.Properties, "playing", false)
            : existing is null || !clipId.Equals(existingClip, StringComparison.Ordinal)
                ? ReadBoolean(emitter.Properties, "playOnStart", true)
                : existingState == "playing";
        var loop = ReadBoolean(emitter.Properties, "loop", false);
        var pitch = Math.Clamp(ReadNumber(emitter.Properties, "pitch", 1), 0.01, 4);
        var duration = clip.Duration.TotalSeconds;
        var state = shouldPlay ? "playing" : existingState == "stopped" ? "stopped" : "paused";
        if (shouldPlay)
        {
            playback += context.DeltaTime.TotalSeconds * pitch;
            if (playback >= duration)
            {
                if (loop && duration > 0)
                {
                    playback %= duration;
                }
                else
                {
                    playback = duration;
                    state = "stopped";
                }
            }
        }

        var busName = ReadString(emitter.Properties, "bus") ?? "master";
        var bus = buses.TryGetValue(busName, out var configuredBus) ? configuredBus : BusState.Default;
        var gain = Math.Clamp(ReadNumber(emitter.Properties, "gain", 1), 0, 4) * (bus.Muted ? 0 : bus.Gain);
        var (leftGain, rightGain) = ResolveSpatialGains(entity, listener, emitter, gain);
        return ReplaceState(entity, CreateState(
            clipId,
            state,
            playback,
            duration,
            emitter,
            leftGain,
            rightGain));
    }

    private async ValueTask EnsureAssetsAsync(CancellationToken cancellationToken)
    {
        if (_assets is not null)
        {
            return;
        }

        if (_projectRoot is null)
        {
            _assets = new Dictionary<string, RekallAgeAssetDocument>(StringComparer.Ordinal);
            return;
        }

        var catalog = await _catalogStore.LoadAsync(_projectRoot, cancellationToken);
        _assets = catalog.Assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
    }

    private RekallAgeRuntimeAudioMixFrame BuildMixFrame(
        IReadOnlyList<RekallAgeRuntimeEntity> entities,
        RekallAgeRuntimeWorldFrameContext context)
    {
        const int outputSampleRate = 48_000;
        const int outputChannels = 2;
        var outputFrames = Math.Max(1, (int)Math.Round(outputSampleRate * context.DeltaTime.TotalSeconds));
        var samples = new float[outputFrames * outputChannels];
        var activeVoices = 0;
        var peakGain = 0.0;
        foreach (var entity in entities)
        {
            var state = entity.Components.FirstOrDefault(component => component.Type == StateComponentType);
            if (state is null || ReadString(state.Properties, "state") != "playing")
            {
                continue;
            }

            var clipId = ReadString(state.Properties, "clipAssetId");
            if (clipId is null || !_clips.TryGetValue(clipId, out var clip))
            {
                continue;
            }

            activeVoices++;
            var endSeconds = ReadNumber(state.Properties, "playbackSeconds", 0);
            var pitch = ReadNumber(state.Properties, "pitch", 1);
            var loop = ReadBoolean(state.Properties, "loop", false);
            var leftGain = ReadNumber(state.Properties, "leftGain", 0);
            var rightGain = ReadNumber(state.Properties, "rightGain", 0);
            peakGain = Math.Max(peakGain, Math.Max(leftGain, rightGain));
            var startSeconds = Math.Max(0, endSeconds - context.DeltaTime.TotalSeconds * pitch);
            for (var outputFrame = 0; outputFrame < outputFrames; outputFrame++)
            {
                var time = startSeconds + outputFrame / (double)outputSampleRate * pitch;
                if (loop && clip.Duration.TotalSeconds > 0)
                {
                    time %= clip.Duration.TotalSeconds;
                }

                var sourceFrame = (int)Math.Floor(time * clip.SampleRate);
                if (sourceFrame < 0 || sourceFrame >= clip.FrameCount)
                {
                    continue;
                }

                var sourceIndex = sourceFrame * clip.Channels;
                var left = clip.Samples[sourceIndex];
                var right = clip.Channels > 1 ? clip.Samples[sourceIndex + 1] : left;
                samples[outputFrame * 2] += left * (float)leftGain;
                samples[outputFrame * 2 + 1] += right * (float)rightGain;
            }
        }

        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = Math.Clamp(samples[index], -1, 1);
        }

        return new RekallAgeRuntimeAudioMixFrame(
            context.FrameIndex,
            activeVoices,
            peakGain,
            outputSampleRate,
            outputChannels,
            samples);
    }

    private async ValueTask<RekallAgePcmAudioClip> LoadClipAsync(
        RekallAgeAssetDocument asset,
        CancellationToken cancellationToken)
    {
        if (_clips.TryGetValue(asset.Id, out var cached))
        {
            return cached;
        }

        if (_projectRoot is null)
        {
            throw new InvalidDataException("Audio assets require a project root.");
        }

        var path = Path.IsPathRooted(asset.ImportedPath)
            ? Path.GetFullPath(asset.ImportedPath)
            : Path.GetFullPath(Path.Combine(_projectRoot, asset.ImportedPath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = _projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new InvalidDataException("Audio asset path is missing or outside the project root.");
        }

        var decoded = RekallAgeWaveDecoder.Decode(await File.ReadAllBytesAsync(path, cancellationToken), asset.Id);
        _clips.Add(asset.Id, decoded);
        return decoded;
    }

    private static IReadOnlyDictionary<string, BusState> ReadBuses(RekallAgeRuntimeWorld world)
    {
        var buses = new Dictionary<string, BusState>(StringComparer.OrdinalIgnoreCase)
        {
            ["master"] = BusState.Default
        };
        foreach (var component in world.Entities.SelectMany(entity => entity.Components)
                     .Where(component => component.Type == "Rekall.AudioBus"))
        {
            var name = ReadString(component.Properties, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                buses[name] = new BusState(
                    Math.Clamp(ReadNumber(component.Properties, "gain", 1), 0, 4),
                    ReadBoolean(component.Properties, "muted", false));
            }
        }

        return buses;
    }

    private static (double Left, double Right) ResolveSpatialGains(
        RekallAgeRuntimeEntity emitterEntity,
        RekallAgeRuntimeEntity? listener,
        RekallAgeRuntimeComponent emitter,
        double gain)
    {
        if (listener is null || !ReadBoolean(emitter.Properties, "spatial", false))
        {
            return (gain, gain);
        }

        var offsetX = emitterEntity.Transform.Position3D.X - listener.Transform.Position3D.X;
        var offsetY = emitterEntity.Transform.Position3D.Y - listener.Transform.Position3D.Y;
        var offsetZ = emitterEntity.Transform.Position3D.Z - listener.Transform.Position3D.Z;
        var distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ);
        var referenceDistance = Math.Max(0.001, ReadNumber(emitter.Properties, "referenceDistance", 1));
        var maxDistance = Math.Max(referenceDistance, ReadNumber(emitter.Properties, "maxDistance", 100));
        var attenuation = distance >= maxDistance ? 0 : Math.Min(1, referenceDistance / Math.Max(referenceDistance, distance));
        var yaw = listener.Transform.Rotation3D.Y * Math.PI / 180.0;
        var rightX = Math.Cos(yaw);
        var rightZ = -Math.Sin(yaw);
        var pan = distance <= 0.000001 ? 0 : Math.Clamp((offsetX * rightX + offsetZ * rightZ) / distance, -1, 1);
        var attenuated = gain * attenuation;
        return (
            attenuated * Math.Sqrt((1 - pan) * 0.5),
            attenuated * Math.Sqrt((1 + pan) * 0.5));
    }

    private static RekallAgeRuntimeComponent CreateState(
        string clipId,
        string state,
        double playback,
        double duration,
        RekallAgeRuntimeComponent emitter,
        double leftGain,
        double rightGain)
    {
        return new RekallAgeRuntimeComponent(
            StateComponentType,
            new JsonObject
            {
                ["clipAssetId"] = clipId,
                ["bus"] = ReadString(emitter.Properties, "bus") ?? "master",
                ["state"] = state,
                ["loop"] = ReadBoolean(emitter.Properties, "loop", false),
                ["playbackSeconds"] = playback,
                ["durationSeconds"] = duration,
                ["gain"] = Math.Clamp(ReadNumber(emitter.Properties, "gain", 1), 0, 4),
                ["pitch"] = Math.Clamp(ReadNumber(emitter.Properties, "pitch", 1), 0.01, 4),
                ["leftGain"] = leftGain,
                ["rightGain"] = rightGain
            });
    }

    private static RekallAgeRuntimeEntity ReplaceState(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent state)
    {
        return entity with
        {
            Components = entity.Components
                .Where(component => component.Type != StateComponentType)
                .Append(state)
                .OrderBy(component => component.Type, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static RekallAgeRuntimeObservation Observation(
        int frame,
        string code,
        RekallAgeRuntimeEntity entity,
        string message)
    {
        return new RekallAgeRuntimeObservation(
            frame,
            code,
            "error",
            "audio",
            entity.Id,
            entity.Name,
            "AudioEmitter",
            message,
            []);
    }

    private static string? ReadString(JsonObject properties, string name) =>
        TryGetPropertyValue(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback) =>
        TryGetPropertyValue(properties, name, out var node) && node is JsonValue value &&
        (value.TryGetValue<bool>(out var boolean) && boolean ||
         value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed) && parsed)
            ? true
            : TryGetPropertyValue(properties, name, out _) ? false : fallback;

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? value)
    {
        foreach (var property in properties)
        {
            if (property.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private readonly record struct BusState(double Gain, bool Muted)
    {
        public static BusState Default { get; } = new(1, false);
    }
}
