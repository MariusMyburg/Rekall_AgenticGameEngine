using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeTransformAnimationSystem : IRekallAgeRuntimeWorldSystem
{
    private const string ClipComponent = "Rekall.AnimationClip";
    private const string PlayerComponent = "Rekall.AnimationPlayer";
    private const string MixerComponent = "Rekall.AnimationMixer";
    private const string GraphComponent = "Rekall.AnimationStateGraph";
    private const string GraphMixerComponent = "Rekall.AnimationGraphMixer";
    private const string StateComponent = "Rekall.AnimationState";
    private const double Epsilon = 0.00001;
    private const long MaxClipBytes = 4 * 1024 * 1024;
    private const int MaxTracksPerClip = 1_024;
    private const int MaxKeysPerTrack = 4_096;
    private const int MaxMarkersPerClip = 4_096;
    private const int MaxMixerLayers = 32;
    private readonly string? _projectRoot;
    private readonly RekallAgeAssetCatalogStore _catalogStore = new();
    private IReadOnlyDictionary<string, RekallAgeAssetDocument>? _assets;
    private readonly Dictionary<string, JsonObject> _assetClips = new(StringComparer.Ordinal);

    public RekallAgeTransformAnimationSystem(string? projectRoot = null)
    {
        _projectRoot = string.IsNullOrWhiteSpace(projectRoot) ? null : Path.GetFullPath(projectRoot);
    }

    public string Id => "runtime.animation";
    public int Priority => 0;

    public async ValueTask<RekallAgeRuntimeWorld> UpdateAsync(RekallAgeRuntimeWorld world, RekallAgeRuntimeWorldFrameContext context)
    {
        await EnsureAssetsAsync(context.CancellationToken);
        var emitted = new List<RekallAgeRuntimeEvent>();
        var observations = new List<RekallAgeRuntimeObservation>();
        var targetedTracks = new List<TargetedAnimationTrack>();
        var targetedMixerTracks = new List<TargetedWeightedAnimationTrack>();
        var locallyAnimated = world.Entities
            .Select(entity => ApplyAnimation(
                entity,
                context,
                emitted,
                observations,
                targetedTracks,
                targetedMixerTracks))
            .ToArray();
        var entitiesById = locallyAnimated.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var childrenByParentId = locallyAnimated
            .Where(entity => entity.ParentId is not null)
            .GroupBy(entity => entity.ParentId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RekallAgeRuntimeEntity>)group.ToArray(),
                StringComparer.Ordinal);
        foreach (var targetedTrack in targetedTracks)
        {
            var owner = entitiesById[targetedTrack.OwnerId];
            var target = ResolveTrackTarget(
                owner,
                targetedTrack.Track,
                entitiesById,
                childrenByParentId,
                context.FrameIndex,
                observations);
            if (target is null)
            {
                continue;
            }

            var currentTarget = entitiesById[target.Id];
            entitiesById[target.Id] = ApplyTrack(
                currentTarget,
                targetedTrack.Track,
                targetedTrack.SampleTime,
                context.FrameIndex,
                observations);
        }
        var resolvedMixerTracks = new List<ResolvedTargetedWeightedAnimationTrack>();
        foreach (var request in targetedMixerTracks)
        {
            var owner = entitiesById[request.OwnerId];
            var target = ResolveTrackTarget(
                owner,
                request.Track,
                entitiesById,
                childrenByParentId,
                context.FrameIndex,
                observations);
            if (target is not null)
            {
                resolvedMixerTracks.Add(new ResolvedTargetedWeightedAnimationTrack(request, target.Id));
            }
        }
        foreach (var blendGroup in resolvedMixerTracks
                     .GroupBy(ResolvedTrackBlendKey.From)
                     .OrderBy(group => group.Key.OwnerId, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.TargetId, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.ComponentType, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.PropertyName, StringComparer.Ordinal))
        {
            var requests = blendGroup
                .Select(item => item.Request)
                .OrderBy(request => request.LayerIndex)
                .ToArray();
            var currentTarget = entitiesById[blendGroup.Key.TargetId];
            var samples = new List<WeightedAnimationSample>();
            foreach (var request in requests)
            {
                var componentType = ReadString(request.Track, "component")
                    ?? ReadString(request.Track, "targetComponent");
                var propertyName = ReadString(request.Track, "property");
                if (string.IsNullOrWhiteSpace(componentType) || string.IsNullOrWhiteSpace(propertyName))
                {
                    _ = ApplyTrack(
                        currentTarget,
                        request.Track,
                        request.SampleTime,
                        context.FrameIndex,
                        observations);
                    continue;
                }
                var sampled = ApplyTrack(
                    currentTarget,
                    request.Track,
                    request.SampleTime,
                    context.FrameIndex,
                    observations);
                var component = sampled.FindComponent(componentType);
                if (component is not null
                    && TryGetProperty(component.Properties, propertyName, out var value)
                    && value is not null)
                {
                    samples.Add(new WeightedAnimationSample(
                        componentType,
                        propertyName,
                        value.DeepClone(),
                        request.Weight,
                        request.LayerIndex));
                }
            }
            var blended = BlendSamples(samples);
            if (blended is not null && samples.Count > 0)
            {
                entitiesById[blendGroup.Key.TargetId] = ApplySampledValue(
                    currentTarget,
                    samples[0].ComponentType,
                    samples[0].PropertyName,
                    blended);
            }
        }
        var entities = locallyAnimated.Select(entity => entitiesById[entity.Id]).ToArray();
        return world with
        {
            Entities = entities,
            Observations = world.Observations.Concat(observations).ToArray(),
            Subsystems = world.Subsystems with
            {
                Events = new RekallAgeRuntimeEventView(world.Subsystems.Events.Events.Concat(emitted).ToArray())
            }
        };
    }

    private RekallAgeRuntimeEntity ApplyAnimation(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeWorldFrameContext context,
        List<RekallAgeRuntimeEvent> emitted,
        List<RekallAgeRuntimeObservation> observations,
        List<TargetedAnimationTrack> targetedTracks,
        List<TargetedWeightedAnimationTrack> targetedMixerTracks)
    {
        var updated = ApplyLegacyTransformRates(entity, context);
        if (updated.FindComponent(GraphComponent) is not null)
        {
            var graphMixer = updated.FindComponent(GraphMixerComponent);
            return graphMixer is null
                ? updated
                : ApplyMixer(updated, graphMixer, context, emitted, observations, targetedMixerTracks);
        }

        var mixer = updated.FindComponent(MixerComponent);
        if (mixer is not null && ReadBoolean(mixer.Properties, "playing", true))
        {
            return ApplyMixer(updated, mixer, context, emitted, observations, targetedMixerTracks);
        }

        var clip = updated.FindComponent(ClipComponent);
        var player = updated.FindComponent(PlayerComponent);
        if (player is null || !ReadBoolean(player.Properties, "playing", true))
        {
            return updated;
        }

        var clipProperties = clip?.Properties ?? ResolveAssetClip(entity, player, context.FrameIndex, observations);
        if (clipProperties is null)
        {
            return updated;
        }

        var version = ReadInt32(clipProperties, "version", 1);
        if (version != 1)
        {
            observations.Add(new RekallAgeRuntimeObservation(
                context.FrameIndex,
                "runtime.animation.unsupported_clip_version",
                "error",
                "animation",
                entity.Id,
                entity.Name,
                "AnimationPlayer",
                $"Animation clip version {version} is unsupported; expected version 1.",
                []));
            return updated;
        }

        var duration = Math.Max(Epsilon, ReadNumber(clipProperties, "durationSeconds", 1));
        var speed = ReadNumber(player.Properties, "speed", 1);
        var loopMode = NormalizeLoopMode(ReadString(player.Properties, "loopMode") ?? "loop");
        var previousRawTime = ReadNumber(
            updated.FindComponent(StateComponent)?.Properties,
            "rawTimeSeconds",
            ReadNumber(player.Properties, "startTimeSeconds", 0));
        var nextRawTime = Math.Max(0, previousRawTime + context.DeltaTime.TotalSeconds * speed);
        var sampleTime = ResolveSampleTime(nextRawTime, duration, loopMode);

        if (TryGetArray(clipProperties, "tracks", out var tracks))
        {
            if (tracks.Count > MaxTracksPerClip)
            {
                observations.Add(AnimationObservation(
                    context.FrameIndex,
                    "runtime.animation.track_limit_exceeded",
                    updated,
                    $"Animation clip contains {tracks.Count} tracks; the per-clip limit is {MaxTracksPerClip}."));
            }
            else
            {
                foreach (var trackNode in tracks)
                {
                    if (trackNode is JsonObject track)
                    {
                        if (HasExternalTrackTarget(track))
                        {
                            targetedTracks.Add(new TargetedAnimationTrack(
                                updated.Id,
                                track,
                                sampleTime));
                        }
                        else
                        {
                            updated = ApplyTrack(updated, track, sampleTime, context.FrameIndex, observations);
                        }
                    }
                    else
                    {
                        observations.Add(AnimationObservation(
                            context.FrameIndex,
                            "runtime.animation.track_invalid",
                            updated,
                            "Animation track entries must be JSON objects."));
                    }
                }
            }
        }

        EmitMarkers(
            updated,
            clipProperties,
            previousRawTime,
            nextRawTime,
            duration,
            loopMode,
            context.FrameIndex,
            emitted,
            observations);
        return updated.UpsertComponent(StateComponent, new JsonObject
        {
            ["version"] = 1,
            ["timeSeconds"] = sampleTime,
            ["rawTimeSeconds"] = nextRawTime,
            ["durationSeconds"] = duration,
            ["loopMode"] = loopMode,
            ["playing"] = loopMode != "clamp" || nextRawTime + Epsilon < duration,
            ["completedCycles"] = (int)Math.Floor((nextRawTime + Epsilon) / duration)
        });
    }

    private static bool HasExternalTrackTarget(JsonObject track) =>
        track.ContainsKey("targetEntityId") || track.ContainsKey("targetPath");

    private static RekallAgeRuntimeEntity? ResolveTrackTarget(
        RekallAgeRuntimeEntity owner,
        JsonObject track,
        IReadOnlyDictionary<string, RekallAgeRuntimeEntity> entitiesById,
        IReadOnlyDictionary<string, IReadOnlyList<RekallAgeRuntimeEntity>> childrenByParentId,
        int frame,
        List<RekallAgeRuntimeObservation> observations)
    {
        var targetEntityId = ReadString(track, "targetEntityId");
        var targetPath = ReadString(track, "targetPath");
        if (targetEntityId is not null && targetPath is not null)
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.track_target_conflict",
                owner,
                "Animation track must specify targetEntityId or targetPath, not both."));
            return null;
        }

        if (targetEntityId is not null)
        {
            if (entitiesById.TryGetValue(targetEntityId, out var target))
            {
                return target;
            }
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.track_target_missing",
                owner,
                $"Animation track target entity '{targetEntityId}' was not found."));
            return null;
        }

        if (targetPath is null || targetPath == ".")
        {
            return owner;
        }
        if (targetPath.Length > 1_024)
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.track_target_path_invalid",
                owner,
                "Animation track targetPath exceeds 1,024 characters."));
            return null;
        }

        var segments = targetPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length is < 1 or > 32
            || targetPath.StartsWith("/", StringComparison.Ordinal)
            || segments.Any(segment => segment is "." or ".."))
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.track_target_path_invalid",
                owner,
                $"Animation track targetPath '{targetPath}' must contain 1-32 relative child id/name segments."));
            return null;
        }

        var current = owner;
        foreach (var segment in segments)
        {
            var children = childrenByParentId.GetValueOrDefault(current.Id) ?? [];
            var idMatches = children.Where(entity => entity.Id.Equals(segment, StringComparison.Ordinal)).ToArray();
            var matches = idMatches.Length > 0
                ? idMatches
                : children.Where(entity => entity.Name.Equals(segment, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 1)
            {
                current = matches[0];
                continue;
            }

            observations.Add(AnimationObservation(
                frame,
                matches.Length == 0
                    ? "runtime.animation.track_target_missing"
                    : "runtime.animation.track_target_ambiguous",
                owner,
                matches.Length == 0
                    ? $"Animation track targetPath '{targetPath}' did not resolve child segment '{segment}'."
                    : $"Animation track targetPath '{targetPath}' resolves child segment '{segment}' ambiguously; use entity ids."));
            return null;
        }
        return current;
    }

    private RekallAgeRuntimeEntity ApplyMixer(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent mixer,
        RekallAgeRuntimeWorldFrameContext context,
        List<RekallAgeRuntimeEvent> emitted,
        List<RekallAgeRuntimeObservation> observations,
        List<TargetedWeightedAnimationTrack> targetedMixerTracks)
    {
        if (!TryGetArray(mixer.Properties, "layers", out var layers))
        {
            observations.Add(AnimationObservation(
                context.FrameIndex,
                "runtime.animation.mixer_layers_missing",
                entity,
                "Animation mixer must define a layers array."));
            return entity;
        }
        if (layers.Count > MaxMixerLayers)
        {
            observations.Add(AnimationObservation(
                context.FrameIndex,
                "runtime.animation.mixer_layer_limit_exceeded",
                entity,
                $"Animation mixer contains {layers.Count} layers; the per-mixer limit is {MaxMixerLayers}."));
            return entity;
        }

        var previousState = entity.FindComponent(StateComponent)?.Properties;
        var previousLayers = TryGetArray(previousState, "layers", out var stateLayers)
            ? stateLayers.OfType<JsonObject>().ToArray()
            : [];
        var layerStates = new JsonArray();
        var maximumTime = 0d;
        var maximumDuration = 0d;
        var layerIndex = 0;
        var layerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layerNode in layers)
        {
            if (layerNode is not JsonObject layer)
            {
                observations.Add(AnimationObservation(
                    context.FrameIndex,
                    "runtime.animation.mixer_layer_invalid",
                    entity,
                    "Animation mixer layer entries must be JSON objects."));
                layerIndex++;
                continue;
            }

            var name = ReadString(layer, "name") ?? $"layer-{layerIndex}";
            if (!layerNames.Add(name))
            {
                observations.Add(AnimationObservation(
                    context.FrameIndex,
                    "runtime.animation.mixer_layer_name_duplicate",
                    entity,
                    $"Animation mixer layer name '{name}' is duplicated; layer names must be unique for deterministic state resume."));
                layerIndex++;
                continue;
            }
            var clipId = ReadString(layer, "clip");
            var clip = ResolveAssetClip(entity, clipId, context.FrameIndex, observations, "mixer layer");
            if (clip is null)
            {
                layerIndex++;
                continue;
            }
            var version = ReadInt32(clip, "version", 1);
            if (version != 1)
            {
                observations.Add(AnimationObservation(
                    context.FrameIndex,
                    "runtime.animation.unsupported_clip_version",
                    entity,
                    $"Animation mixer layer '{name}' uses clip version {version}; expected version 1."));
                layerIndex++;
                continue;
            }

            var prior = previousLayers.FirstOrDefault(candidate =>
                string.Equals(ReadString(candidate, "name"), name, StringComparison.Ordinal));
            var authoredWeight = Math.Clamp(ReadNumber(layer, "weight", 1), 0, 1);
            var currentWeight = Math.Clamp(ReadNumber(prior, "weight", authoredWeight), 0, 1);
            var targetWeight = Math.Clamp(ReadNumber(layer, "targetWeight", authoredWeight), 0, 1);
            var fadeSeconds = Math.Max(0, ReadNumber(layer, "fadeSeconds", 0));
            currentWeight = MoveTowards(
                currentWeight,
                targetWeight,
                fadeSeconds <= Epsilon ? 1 : context.DeltaTime.TotalSeconds / fadeSeconds);

            var duration = Math.Max(Epsilon, ReadNumber(clip, "durationSeconds", 1));
            var speed = ReadNumber(layer, "speed", 1);
            var loopMode = NormalizeLoopMode(ReadString(layer, "loopMode") ?? "loop");
            var playing = ReadBoolean(layer, "playing", true);
            var previousRawTime = ReadNumber(prior, "rawTimeSeconds", ReadNumber(layer, "startTimeSeconds", 0));
            var nextRawTime = TryGetProperty(layer, "authoritativeTimeSeconds", out var authoritativeNode)
                && TryReadNumber(authoritativeNode, out var authoritativeTime)
                    ? Math.Max(0, authoritativeTime)
                    : playing
                        ? Math.Max(0, previousRawTime + context.DeltaTime.TotalSeconds * speed)
                        : previousRawTime;
            var sampleTime = ResolveSampleTime(nextRawTime, duration, loopMode);
            maximumTime = Math.Max(maximumTime, sampleTime);
            maximumDuration = Math.Max(maximumDuration, duration);

            if (currentWeight > Epsilon && TryGetArray(clip, "tracks", out var tracks))
            {
                if (tracks.Count > MaxTracksPerClip)
                {
                    observations.Add(AnimationObservation(
                        context.FrameIndex,
                        "runtime.animation.track_limit_exceeded",
                        entity,
                        $"Animation mixer layer '{name}' contains {tracks.Count} tracks; the per-clip limit is {MaxTracksPerClip}."));
                }
                else
                {
                    foreach (var trackNode in tracks)
                    {
                        if (trackNode is not JsonObject track)
                        {
                            observations.Add(AnimationObservation(
                                context.FrameIndex,
                                "runtime.animation.track_invalid",
                                entity,
                                $"Animation mixer layer '{name}' contains a track entry that is not a JSON object."));
                            continue;
                        }
                        targetedMixerTracks.Add(new TargetedWeightedAnimationTrack(
                            entity.Id,
                            track,
                            sampleTime,
                            currentWeight,
                            layerIndex));
                    }
                }
            }

            if (playing)
            {
                EmitMarkers(entity, clip, previousRawTime, nextRawTime, duration, loopMode, context.FrameIndex, emitted, observations);
            }
            layerStates.Add(new JsonObject
            {
                ["name"] = name,
                ["clip"] = clipId,
                ["weight"] = currentWeight,
                ["targetWeight"] = targetWeight,
                ["timeSeconds"] = sampleTime,
                ["rawTimeSeconds"] = nextRawTime,
                ["durationSeconds"] = duration,
                ["loopMode"] = loopMode,
                ["playing"] = playing
            });
            layerIndex++;
        }

        return entity.UpsertComponent(StateComponent, new JsonObject
        {
            ["version"] = 1,
            ["mode"] = "mixer",
            ["timeSeconds"] = maximumTime,
            ["durationSeconds"] = maximumDuration,
            ["loopMode"] = "mixed",
            ["playing"] = true,
            ["layers"] = layerStates
        });
    }

    private static JsonNode? BlendSamples(IReadOnlyList<WeightedAnimationSample> samples)
    {
        var active = samples.Where(sample => sample.Weight > Epsilon).ToArray();
        var totalWeight = active.Sum(sample => sample.Weight);
        if (active.Length == 0 || totalWeight <= Epsilon)
        {
            return null;
        }
        if (active.All(sample => TryReadNumber(sample.Value, out _)))
        {
            return JsonValue.Create(active.Sum(sample => ReadNodeNumber(sample.Value) * sample.Weight) / totalWeight);
        }
        if (active.All(sample => sample.Value is JsonArray)
            && active.Select(sample => ((JsonArray)sample.Value).Count).Distinct().Count() == 1)
        {
            var result = new JsonArray();
            var count = ((JsonArray)active[0].Value).Count;
            for (var index = 0; index < count; index++)
            {
                var childSamples = active.Select(sample => sample with
                {
                    Value = ((JsonArray)sample.Value)[index]?.DeepClone() ?? JsonValue.Create(0)
                }).ToArray();
                var child = BlendSamples(childSamples);
                if (child is null)
                {
                    return HighestWeightValue(active);
                }
                result.Add(child);
            }
            return result;
        }
        if (active.All(sample => TryReadColor(sample.Value, out _)))
        {
            static byte Channel(IEnumerable<(byte Value, double Weight)> values, double total) =>
                (byte)Math.Clamp(Math.Round(values.Sum(item => item.Value * item.Weight) / total, MidpointRounding.AwayFromZero), 0, 255);
            var colors = active.Select(sample => (Color: ReadNodeColor(sample.Value), sample.Weight)).ToArray();
            var red = Channel(colors.Select(item => (item.Color.R, item.Weight)), totalWeight);
            var green = Channel(colors.Select(item => (item.Color.G, item.Weight)), totalWeight);
            var blue = Channel(colors.Select(item => (item.Color.B, item.Weight)), totalWeight);
            var alpha = Channel(colors.Select(item => (item.Color.A, item.Weight)), totalWeight);
            var color = $"#{red:x2}{green:x2}{blue:x2}";
            return JsonValue.Create(alpha == 255 ? color : $"{color}{alpha:x2}");
        }
        return HighestWeightValue(active);
    }

    private static JsonNode HighestWeightValue(IEnumerable<WeightedAnimationSample> samples) =>
        samples.OrderByDescending(sample => sample.Weight)
            .ThenBy(sample => sample.LayerIndex)
            .First().Value.DeepClone();

    private static double ReadNodeNumber(JsonNode node)
    {
        _ = TryReadNumber(node, out var number);
        return number;
    }

    private static AnimationColor ReadNodeColor(JsonNode node)
    {
        _ = TryReadColor(node, out var color);
        return color;
    }

    private static double MoveTowards(double current, double target, double maximumDelta)
    {
        if (Math.Abs(target - current) <= maximumDelta)
        {
            return target;
        }
        return current + Math.Sign(target - current) * maximumDelta;
    }

    private static RekallAgeRuntimeEntity ApplyTrack(
        RekallAgeRuntimeEntity entity,
        JsonObject track,
        double sampleTime,
        int frame,
        List<RekallAgeRuntimeObservation> observations)
    {
        var componentType = ReadString(track, "component") ?? ReadString(track, "targetComponent");
        var propertyName = ReadString(track, "property");
        if (string.IsNullOrWhiteSpace(componentType)
            || string.IsNullOrWhiteSpace(propertyName)
            || !TryGetArray(track, "keys", out var keyNodes))
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.track_invalid",
                entity,
                "Animation track must define a component, property, and keys array."));
            return entity;
        }
        if (keyNodes.Count > MaxKeysPerTrack)
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.key_limit_exceeded",
                entity,
                $"Animation track '{componentType}.{propertyName}' contains {keyNodes.Count} keys; the per-track limit is {MaxKeysPerTrack}."));
            return entity;
        }

        if (entity.FindComponent(componentType) is not { })
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.track_component_missing",
                entity,
                $"Animation track targets missing component '{componentType}' property '{propertyName}'."));
            return entity;
        }
        if (componentType.Equals("Rekall.Transform3D", StringComparison.Ordinal)
            && propertyName.ToLowerInvariant() is not ("x" or "y" or "z" or "pitch" or "yaw" or "roll" or "scalex" or "scaley" or "scalez"))
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.transform_property_invalid",
                entity,
                $"Animation track property '{propertyName}' is not a supported Rekall.Transform3D property."));
            return entity;
        }

        var interpolation = ReadString(track, "interpolation") ?? "linear";
        if (interpolation.ToLowerInvariant() is not ("step" or "linear" or "smooth" or "smoothstep" or "cubic"))
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.interpolation_invalid",
                entity,
                $"Animation track '{componentType}.{propertyName}' uses unsupported interpolation '{interpolation}'."));
            return entity;
        }
        if (interpolation.Equals("cubic", StringComparison.OrdinalIgnoreCase))
        {
            if (!RekallAgeCubicAnimationSampler.TryCreateKeys(keyNodes, out var cubicKeys, out var issue))
            {
                observations.Add(AnimationObservation(
                    frame,
                    "runtime.animation.cubic_key_invalid",
                    entity,
                    $"Animation track '{componentType}.{propertyName}' is invalid: {issue}"));
                return entity;
            }
            var cubicValue = RekallAgeCubicAnimationSampler.Sample(cubicKeys, sampleTime);
            if (cubicValue is null)
            {
                observations.Add(AnimationObservation(
                    frame,
                    "runtime.animation.cubic_key_invalid",
                    entity,
                    $"Animation track '{componentType}.{propertyName}' produced a non-finite cubic value."));
                return entity;
            }
            return ApplySampledValue(entity, componentType, propertyName, cubicValue);
        }

        var keys = keyNodes.OfType<JsonObject>()
            .Select(key => new AnimationKey(ReadNumber(key, "time", 0), key["value"]?.DeepClone()))
            .Where(key => key.Value is not null)
            .OrderBy(key => key.Time)
            .ToArray();
        if (keys.Length == 0)
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.track_no_valid_keys",
                entity,
                $"Animation track '{componentType}.{propertyName}' has no keys with values."));
            return entity;
        }
        var value = Sample(keys, sampleTime, interpolation);
        if (value is null)
        {
            return entity;
        }

        return ApplySampledValue(entity, componentType, propertyName, value);
    }

    private static RekallAgeRuntimeEntity ApplySampledValue(
        RekallAgeRuntimeEntity entity,
        string componentType,
        string propertyName,
        JsonNode value)
    {
        var component = entity.FindComponent(componentType);
        if (component is null)
        {
            return entity;
        }
        var properties = (JsonObject)component.Properties.DeepClone();
        properties[propertyName] = value.DeepClone();
        var components = entity.Components.Select(item => ReferenceEquals(item, component)
            ? new RekallAgeRuntimeComponent(item.Type, properties)
            : item).ToArray();
        return ApplyTransformProperty(entity with { Components = components }, componentType, propertyName, value);
    }

    private static JsonNode? Sample(AnimationKey[] keys, double time, string interpolation)
    {
        if (time <= keys[0].Time + Epsilon)
        {
            return keys[0].Value?.DeepClone();
        }

        var rightIndex = Array.FindIndex(keys, key => key.Time + Epsilon >= time);
        if (rightIndex <= 0)
        {
            return keys[^1].Value?.DeepClone();
        }

        var left = keys[rightIndex - 1];
        var right = keys[rightIndex];
        if (interpolation.Equals("step", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Abs(time - right.Time) <= Epsilon ? right.Value?.DeepClone() : left.Value?.DeepClone();
        }

        var amount = Math.Clamp((time - left.Time) / Math.Max(Epsilon, right.Time - left.Time), 0, 1);
        amount = Math.Round(amount, 5, MidpointRounding.AwayFromZero);
        if (interpolation.Equals("smooth", StringComparison.OrdinalIgnoreCase)
            || interpolation.Equals("smoothstep", StringComparison.OrdinalIgnoreCase))
        {
            amount = amount * amount * (3 - 2 * amount);
        }

        return InterpolateValue(left.Value, right.Value, amount)
            ?? (Math.Abs(time - right.Time) <= Epsilon ? right.Value?.DeepClone() : left.Value?.DeepClone());
    }

    private static JsonNode? InterpolateValue(JsonNode? left, JsonNode? right, double amount)
    {
        if (TryReadNumber(left, out var leftNumber) && TryReadNumber(right, out var rightNumber))
        {
            return JsonValue.Create(leftNumber + (rightNumber - leftNumber) * amount);
        }

        if (left is JsonArray leftArray && right is JsonArray rightArray && leftArray.Count == rightArray.Count)
        {
            var result = new JsonArray();
            for (var index = 0; index < leftArray.Count; index++)
            {
                var value = InterpolateValue(leftArray[index], rightArray[index], amount);
                if (value is null)
                {
                    return null;
                }

                result.Add(value);
            }

            return result;
        }

        if (TryReadColor(left, out var leftColor) && TryReadColor(right, out var rightColor))
        {
            static byte Channel(byte from, byte to, double t) =>
                (byte)Math.Clamp(Math.Round(from + (to - from) * t, MidpointRounding.AwayFromZero), 0, 255);
            var alpha = Channel(leftColor.A, rightColor.A, amount);
            var color = $"#{Channel(leftColor.R, rightColor.R, amount):x2}{Channel(leftColor.G, rightColor.G, amount):x2}{Channel(leftColor.B, rightColor.B, amount):x2}";
            return JsonValue.Create(alpha == 255 ? color : $"{color}{alpha:x2}");
        }

        return null;
    }

    private static bool TryReadColor(JsonNode? node, out AnimationColor color)
    {
        color = default;
        if (node is not JsonValue value
            || !value.TryGetValue<string>(out var text)
            || text.Length is not (7 or 9)
            || text[0] != '#'
            || !byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        var alpha = (byte)255;
        if (text.Length == 9
            && !byte.TryParse(text.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out alpha))
        {
            return false;
        }

        color = new AnimationColor(red, green, blue, alpha);
        return true;
    }

    private static RekallAgeRuntimeEntity ApplyTransformProperty(
        RekallAgeRuntimeEntity entity,
        string componentType,
        string propertyName,
        JsonNode value)
    {
        if (!componentType.Equals("Rekall.Transform3D", StringComparison.Ordinal)
            || !TryReadNumber(value, out var number))
        {
            return entity;
        }

        var transform = entity.Transform;
        transform = propertyName.ToLowerInvariant() switch
        {
            "x" => transform with { Position3D = transform.Position3D with { X = number } },
            "y" => transform with { Position3D = transform.Position3D with { Y = number } },
            "z" => transform with { Position3D = transform.Position3D with { Z = number } },
            "pitch" => transform with { Rotation3D = transform.Rotation3D with { X = number } },
            "yaw" => transform with { Rotation3D = transform.Rotation3D with { Y = number } },
            "roll" => transform with { Rotation3D = transform.Rotation3D with { Z = number } },
            "scalex" => transform with { Scale3D = transform.Scale3D with { X = number } },
            "scaley" => transform with { Scale3D = transform.Scale3D with { Y = number } },
            "scalez" => transform with { Scale3D = transform.Scale3D with { Z = number } },
            _ => transform
        };
        return entity with { Transform = transform };
    }

    private static void EmitMarkers(
        RekallAgeRuntimeEntity entity,
        JsonObject clip,
        double previousRawTime,
        double nextRawTime,
        double duration,
        string loopMode,
        int frame,
        List<RekallAgeRuntimeEvent> emitted,
        List<RekallAgeRuntimeObservation> observations)
    {
        if (nextRawTime <= previousRawTime || !TryGetArray(clip, "events", out var markers))
        {
            return;
        }
        if (markers.Count > MaxMarkersPerClip)
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.marker_limit_exceeded",
                entity,
                $"Animation clip contains {markers.Count} markers; the per-clip limit is {MaxMarkersPerClip}."));
            return;
        }

        foreach (var marker in markers.OfType<JsonObject>())
        {
            var markerTime = Math.Clamp(ReadNumber(marker, "time", 0), 0, duration);
            if (!CrossedMarker(previousRawTime, nextRawTime, markerTime, duration, loopMode))
            {
                continue;
            }

            foreach (var handler in EventHandlers(entity, "animation.event"))
            {
                emitted.Add(new RekallAgeRuntimeEvent(
                    frame,
                    "animation.event",
                    entity.Id,
                    entity.Name,
                    "runtime.animation",
                    handler,
                    new JsonObject
                    {
                        ["name"] = ReadString(marker, "name") ?? "marker",
                        ["timeSeconds"] = markerTime,
                        ["payload"] = marker["payload"]?.DeepClone()
                    }));
            }
        }
    }

    private static bool CrossedMarker(double previous, double next, double marker, double duration, string loopMode)
    {
        if (loopMode == "clamp")
        {
            return marker > previous + Epsilon && marker <= next + Epsilon;
        }

        var firstCycle = Math.Max(0, (int)Math.Floor(previous / duration));
        var lastCycle = Math.Max(firstCycle, (int)Math.Floor(next / duration));
        for (var cycle = firstCycle; cycle <= lastCycle; cycle++)
        {
            var occurrence = loopMode == "pingpong" && cycle % 2 == 1
                ? cycle * duration + duration - marker
                : cycle * duration + marker;
            if (occurrence > previous + Epsilon && occurrence <= next + Epsilon)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string?> EventHandlers(RekallAgeRuntimeEntity entity, string type)
    {
        return entity.Components
            .Where(component => component.Type.Equals("Rekall.EventBindings", StringComparison.Ordinal))
            .SelectMany(component => TryGetArray(component.Properties, "events", out var events)
                ? events.OfType<JsonObject>()
                : [])
            .Where(binding => ReadBoolean(binding, "active", true)
                && string.Equals(ReadString(binding, "event") ?? ReadString(binding, "type"), type, StringComparison.OrdinalIgnoreCase))
            .Select(binding => ReadString(binding, "handler"))
            .ToArray();
    }

    private static double ResolveSampleTime(double rawTime, double duration, string loopMode)
    {
        if (loopMode == "clamp")
        {
            return Math.Min(rawTime, duration);
        }

        var cycle = (int)Math.Floor(rawTime / duration);
        var local = rawTime - cycle * duration;
        return loopMode == "pingpong" && cycle % 2 == 1 ? duration - local : local;
    }

    private static string NormalizeLoopMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "once" or "none" or "clamp" => "clamp",
        "pingpong" or "ping-pong" => "pingpong",
        _ => "loop"
    };

    private static RekallAgeRuntimeEntity ApplyLegacyTransformRates(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var pitchRate = 0.0;
        var yawRate = 0.0;
        var rollRate = 0.0;
        foreach (var component in entity.Components.Where(component =>
                     component.Type.Equals("Rekall.TransformAnimation", StringComparison.Ordinal)))
        {
            if (!ReadBoolean(component.Properties, "active", true))
            {
                continue;
            }

            pitchRate += ReadNumber(component.Properties, "pitchDegreesPerSecond", 0) + ReadNumber(component.Properties, "pitchRate", 0);
            yawRate += ReadNumber(component.Properties, "yawDegreesPerSecond", 0) + ReadNumber(component.Properties, "yawRate", 0);
            rollRate += ReadNumber(component.Properties, "rollDegreesPerSecond", 0) + ReadNumber(component.Properties, "rollRate", 0);
        }

        if (pitchRate == 0 && yawRate == 0 && rollRate == 0)
        {
            return entity;
        }

        var seconds = context.DeltaTime.TotalSeconds;
        var rotation = entity.Transform.Rotation3D;
        return entity with
        {
            Transform = entity.Transform with
            {
                Rotation3D = new RekallAgeRuntimeVector3(
                    rotation.X + pitchRate * seconds,
                    rotation.Y + yawRate * seconds,
                    rotation.Z + rollRate * seconds)
            }
        };
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

    private JsonObject? ResolveAssetClip(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent player,
        int frame,
        List<RekallAgeRuntimeObservation> observations)
    {
        var clipId = ReadString(player.Properties, "clip")
            ?? ReadString(player.Properties, "clipId")
            ?? ReadString(player.Properties, "animation")
            ?? ReadString(player.Properties, "assetId");
        return ResolveAssetClip(entity, clipId, frame, observations, "player");
    }

    private JsonObject? ResolveAssetClip(
        RekallAgeRuntimeEntity entity,
        string? clipId,
        int frame,
        List<RekallAgeRuntimeObservation> observations,
        string owner)
    {
        if (string.IsNullOrWhiteSpace(clipId) || _assets is null || !_assets.TryGetValue(clipId, out var asset))
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.clip_asset_missing",
                entity,
                string.IsNullOrWhiteSpace(clipId)
                    ? $"Animation {owner} has no clip asset reference."
                    : $"Animation {owner} clip asset '{clipId}' is not present in the project catalog."));
            return null;
        }

        if (_assetClips.TryGetValue(clipId, out var cached))
        {
            return cached;
        }

        try
        {
            if (!asset.Kind.Equals("animation", StringComparison.OrdinalIgnoreCase)
                && !asset.Kind.Equals("animation-clip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Asset kind '{asset.Kind}' is not an animation clip kind.");
            }

            if (_projectRoot is null)
            {
                throw new InvalidDataException("Animation asset resolution requires a project root.");
            }

            var root = Path.GetFullPath(_projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var path = Path.IsPathRooted(asset.ImportedPath)
                ? Path.GetFullPath(asset.ImportedPath)
                : Path.GetFullPath(Path.Combine(root, asset.ImportedPath.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = root + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Animation asset path escapes the project root.");
            }

            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException("Animation asset file was not found.", path);
            }

            if (file.Length > MaxClipBytes)
            {
                throw new InvalidDataException($"Animation clip exceeds the {MaxClipBytes}-byte limit.");
            }

            var rootNode = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException("Animation asset must contain a JSON object.");
            var clip = rootNode["clip"] as JsonObject ?? rootNode;
            _assetClips[clipId] = clip;
            return clip;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.clip_asset_invalid",
                entity,
                $"Animation clip asset '{clipId}' could not be loaded: {exception.Message}"));
            return null;
        }
    }

    private static RekallAgeRuntimeObservation AnimationObservation(
        int frame,
        string code,
        RekallAgeRuntimeEntity entity,
        string message)
    {
        return new RekallAgeRuntimeObservation(
            frame,
            code,
            "error",
            "animation",
            entity.Id,
            entity.Name,
            "AnimationPlayer",
            message,
            []);
    }

    private static bool TryGetArray(JsonObject? properties, string name, out JsonArray array)
    {
        if (properties is not null && TryGetProperty(properties, name, out var node) && node is JsonArray value)
        {
            array = value;
            return true;
        }

        array = [];
        return false;
    }

    private sealed record WeightedAnimationSample(
        string ComponentType,
        string PropertyName,
        JsonNode Value,
        double Weight,
        int LayerIndex);

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback)
    {
        if (!TryGetProperty(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        return value.TryGetValue<bool>(out var boolean)
            ? boolean
            : value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed) ? parsed : fallback;
    }

    private static int ReadInt32(JsonObject properties, string name, int fallback)
    {
        var number = ReadNumber(properties, name, fallback);
        return double.IsFinite(number) ? (int)number : fallback;
    }

    private static double ReadNumber(JsonObject? properties, string name, double fallback)
    {
        return properties is not null
            && TryGetProperty(properties, name, out var node)
            && TryReadNumber(node, out var number)
                ? number
                : fallback;
    }

    private static bool TryReadNumber(JsonNode? node, out double number)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out number))
            {
                return double.IsFinite(number);
            }

            if (value.TryGetValue<int>(out var integer))
            {
                number = integer;
                return true;
            }

            if (value.TryGetValue<string>(out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return double.IsFinite(number);
            }
        }

        number = 0;
        return false;
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return TryGetProperty(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text) ? text : null;
    }

    private static bool TryGetProperty(JsonObject properties, string name, out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        var match = properties.FirstOrDefault(property => property.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        node = match.Value;
        return !string.IsNullOrEmpty(match.Key);
    }

    private sealed record AnimationKey(double Time, JsonNode? Value);
    private sealed record TargetedAnimationTrack(string OwnerId, JsonObject Track, double SampleTime);
    private sealed record TargetedWeightedAnimationTrack(
        string OwnerId,
        JsonObject Track,
        double SampleTime,
        double Weight,
        int LayerIndex);
    private sealed record ResolvedTargetedWeightedAnimationTrack(
        TargetedWeightedAnimationTrack Request,
        string TargetId);
    private sealed record ResolvedTrackBlendKey(
        string OwnerId,
        string TargetId,
        string ComponentType,
        string PropertyName)
    {
        public static ResolvedTrackBlendKey From(ResolvedTargetedWeightedAnimationTrack resolved) => new(
            resolved.Request.OwnerId,
            resolved.TargetId,
            ReadString(resolved.Request.Track, "component")
                ?? ReadString(resolved.Request.Track, "targetComponent")
                ?? string.Empty,
            (ReadString(resolved.Request.Track, "property") ?? string.Empty).ToLowerInvariant());
    }
    private readonly record struct AnimationColor(byte R, byte G, byte B, byte A);
}
