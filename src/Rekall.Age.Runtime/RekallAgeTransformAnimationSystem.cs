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
    private const string StateComponent = "Rekall.AnimationState";
    private const double Epsilon = 0.00001;
    private const long MaxClipBytes = 4 * 1024 * 1024;
    private const int MaxTracksPerClip = 1_024;
    private const int MaxKeysPerTrack = 4_096;
    private const int MaxMarkersPerClip = 4_096;
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
        var entities = world.Entities.Select(entity => ApplyAnimation(entity, context, emitted, observations)).ToArray();
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
        List<RekallAgeRuntimeObservation> observations)
    {
        var updated = ApplyLegacyTransformRates(entity, context);
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
                        updated = ApplyTrack(updated, track, sampleTime, context.FrameIndex, observations);
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
        if (entity.FindComponent(componentType) is not { } component)
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

        var value = Sample(keys, sampleTime, ReadString(track, "interpolation") ?? "linear");
        if (value is null)
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
        if (string.IsNullOrWhiteSpace(clipId) || _assets is null || !_assets.TryGetValue(clipId, out var asset))
        {
            observations.Add(AnimationObservation(
                frame,
                "runtime.animation.clip_asset_missing",
                entity,
                string.IsNullOrWhiteSpace(clipId)
                    ? "Animation player has neither an inline clip nor a clip asset reference."
                    : $"Animation clip asset '{clipId}' is not present in the project catalog."));
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

    private static bool TryGetArray(JsonObject properties, string name, out JsonArray array)
    {
        if (TryGetProperty(properties, name, out var node) && node is JsonArray value)
        {
            array = value;
            return true;
        }

        array = [];
        return false;
    }

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
    private readonly record struct AnimationColor(byte R, byte G, byte B, byte A);
}
