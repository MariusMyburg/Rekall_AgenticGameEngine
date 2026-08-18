using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeSkeletalAnimationSystem : IRekallAgeRuntimeWorldSystem
{
    private const string AnimatorComponent = "Rekall.SkeletalAnimator";
    private const string PoseComponent = "Rekall.SkeletonPose";
    private const double Epsilon = 0.00001;
    private readonly string? _projectRoot;
    private readonly RekallAgeAssetCatalogStore _catalogStore = new();
    private IReadOnlyDictionary<string, RekallAgeAssetDocument>? _assets;
    private readonly Dictionary<string, RekallAgeGlbSkeletalAsset> _skeletalAssets = new(StringComparer.Ordinal);

    public RekallAgeSkeletalAnimationSystem(string? projectRoot)
    {
        _projectRoot = string.IsNullOrWhiteSpace(projectRoot) ? null : Path.GetFullPath(projectRoot);
    }

    public string Id => "runtime.animation.skeletal";

    public int Priority => 0;

    public async ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        await EnsureAssetsAsync(context.CancellationToken);
        var observations = new List<RekallAgeRuntimeObservation>();
        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            var animator = entity.FindComponent(AnimatorComponent);
            entities.Add(animator is null
                ? entity
                : await ApplyAsync(entity, animator, context, observations));
        }
        return world with
        {
            Entities = entities,
            Observations = world.Observations.Concat(observations).ToArray()
        };
    }

    private async ValueTask<RekallAgeRuntimeEntity> ApplyAsync(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent animator,
        RekallAgeRuntimeWorldFrameContext context,
        List<RekallAgeRuntimeObservation> observations)
    {
        var modelId = ReadString(animator.Properties, "model");
        if (string.IsNullOrWhiteSpace(modelId) || _assets is null || !_assets.TryGetValue(modelId, out var model))
        {
            observations.Add(Observation(context.FrameIndex, "runtime.animation.skeletal_model_missing", entity,
                string.IsNullOrWhiteSpace(modelId)
                    ? "Skeletal animator has no GLB model asset reference."
                    : $"Skeletal animator model asset '{modelId}' is not in the project catalog."));
            return entity;
        }

        RekallAgeGlbSkeletalAsset asset;
        try
        {
            if (!_skeletalAssets.TryGetValue(modelId, out asset!))
            {
                var path = ResolveImportedPath(model.ImportedPath);
                asset = await RekallAgeGlbSkeletalAnimationReader.ReadAsync(path, context.CancellationToken);
                _skeletalAssets[modelId] = asset;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            observations.Add(Observation(context.FrameIndex, "runtime.animation.skeletal_asset_invalid", entity,
                $"Skeletal animator could not load GLB model '{modelId}': {exception.Message}"));
            return entity;
        }

        var skinIndex = ReadInt(animator.Properties, "skinIndex", 0);
        if (skinIndex < 0 || skinIndex >= asset.Skins.Count)
        {
            observations.Add(Observation(context.FrameIndex, "runtime.animation.skeletal_skin_missing", entity,
                $"Skeletal animator skin index {skinIndex} is unavailable; model contains {asset.Skins.Count} skins."));
            return entity;
        }
        var requestedAnimation = ReadString(animator.Properties, "animation");
        var animation = string.IsNullOrWhiteSpace(requestedAnimation)
            ? asset.Animations.FirstOrDefault()
            : asset.Animations.FirstOrDefault(item => string.Equals(item.Name, requestedAnimation, StringComparison.Ordinal));
        if (animation is null)
        {
            observations.Add(Observation(context.FrameIndex, "runtime.animation.skeletal_animation_missing", entity,
                string.IsNullOrWhiteSpace(requestedAnimation)
                    ? "Skeletal animator model contains no animations."
                    : $"Skeletal animation '{requestedAnimation}' was not found in model '{modelId}'."));
            return entity;
        }

        var previousPose = entity.FindComponent(PoseComponent)?.Properties;
        var playing = ReadBoolean(animator.Properties, "playing", true);
        var rawTime = ReadNumber(previousPose, "rawTimeSeconds", ReadNumber(animator.Properties, "startTimeSeconds", 0));
        if (playing)
        {
            rawTime = Math.Max(0, rawTime + context.DeltaTime.TotalSeconds * ReadNumber(animator.Properties, "speed", 1));
        }
        var duration = Math.Max(Epsilon, animation.DurationSeconds);
        var loopMode = NormalizeLoopMode(ReadString(animator.Properties, "loopMode") ?? "loop");
        var sampleTime = ResolveSampleTime(rawTime, duration, loopMode);
        var localPoses = asset.Nodes.Select(node => new NodePose(node.Translation, node.Rotation, node.Scale)).ToArray();
        foreach (var channel in animation.Channels)
        {
            if (channel.NodeIndex < 0 || channel.NodeIndex >= localPoses.Length)
            {
                observations.Add(Observation(context.FrameIndex, "runtime.animation.skeletal_channel_target_invalid", entity,
                    $"Skeletal animation channel targets node {channel.NodeIndex}, outside the {localPoses.Length}-node hierarchy."));
                continue;
            }
            Vector4 value;
            try
            {
                value = Sample(channel, sampleTime);
            }
            catch (InvalidDataException exception)
            {
                observations.Add(Observation(context.FrameIndex, "runtime.animation.skeletal_sample_invalid", entity,
                    $"Skeletal animation channel for node {channel.NodeIndex} could not be sampled: {exception.Message}"));
                return entity;
            }
            localPoses[channel.NodeIndex] = channel.Path switch
            {
                "translation" => localPoses[channel.NodeIndex] with { Translation = new Vector3(value.X, value.Y, value.Z) },
                "scale" => localPoses[channel.NodeIndex] with { Scale = new Vector3(value.X, value.Y, value.Z) },
                "rotation" => localPoses[channel.NodeIndex] with { Rotation = Quaternion.Normalize(new Quaternion(value.X, value.Y, value.Z, value.W)) },
                _ => localPoses[channel.NodeIndex]
            };
        }

        Matrix4x4[] globals;
        try
        {
            globals = BuildGlobalMatrices(asset.Nodes, localPoses);
        }
        catch (InvalidDataException exception)
        {
            observations.Add(Observation(context.FrameIndex, "runtime.animation.skeletal_pose_invalid", entity,
                $"Skeletal animator could not build a joint pose: {exception.Message}"));
            return entity;
        }
        var skin = asset.Skins[skinIndex];
        var joints = new JsonArray();
        for (var jointIndex = 0; jointIndex < skin.JointNodeIndexes.Count; jointIndex++)
        {
            var nodeIndex = skin.JointNodeIndexes[jointIndex];
            if (nodeIndex < 0 || nodeIndex >= asset.Nodes.Count)
            {
                observations.Add(Observation(context.FrameIndex, "runtime.animation.skeletal_joint_invalid", entity,
                    $"Skin joint {jointIndex} targets node {nodeIndex}, outside the hierarchy."));
                continue;
            }
            var pose = localPoses[nodeIndex];
            var jointMatrix = skin.InverseBindMatrices[jointIndex] * globals[nodeIndex];
            joints.Add(new JsonObject
            {
                ["jointIndex"] = jointIndex,
                ["nodeIndex"] = nodeIndex,
                ["name"] = asset.Nodes[nodeIndex].Name,
                ["translation"] = Vector(pose.Translation),
                ["rotation"] = QuaternionNode(pose.Rotation),
                ["scale"] = Vector(pose.Scale),
                ["matrix"] = Matrix(jointMatrix)
            });
        }

        return entity.UpsertComponent(PoseComponent, new JsonObject
        {
            ["version"] = 1,
            ["model"] = modelId,
            ["animation"] = animation.Name,
            ["skinIndex"] = skinIndex,
            ["skinName"] = skin.Name,
            ["jointCount"] = skin.JointNodeIndexes.Count,
            ["timeSeconds"] = sampleTime,
            ["rawTimeSeconds"] = rawTime,
            ["durationSeconds"] = duration,
            ["loopMode"] = loopMode,
            ["playing"] = playing,
            ["joints"] = joints
        });
    }

    private string ResolveImportedPath(string importedPath)
    {
        if (_projectRoot is null)
        {
            throw new InvalidDataException("Skeletal animation requires a project root.");
        }
        var root = _projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.IsPathRooted(importedPath)
            ? Path.GetFullPath(importedPath)
            : Path.GetFullPath(Path.Combine(root, importedPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Skeletal animation asset path escapes the project root.");
        }
        return path;
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

    private static Vector4 Sample(RekallAgeGlbNodeAnimationChannel channel, double time)
    {
        if (time <= channel.Times[0] + Epsilon)
        {
            return FinalizeCubicSample(channel, channel.Values[0]);
        }
        var right = 1;
        while (right < channel.Times.Count && channel.Times[right] + Epsilon < time)
        {
            right++;
        }
        if (right >= channel.Times.Count)
        {
            return FinalizeCubicSample(channel, channel.Values[^1]);
        }
        var left = right - 1;
        if (channel.Interpolation.Equals("step", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Abs(time - channel.Times[right]) <= Epsilon ? channel.Values[right] : channel.Values[left];
        }
        var amount = (float)Math.Clamp(
            (time - channel.Times[left]) / Math.Max(Epsilon, channel.Times[right] - channel.Times[left]),
            0,
            1);
        if (channel.Interpolation.Equals("cubicspline", StringComparison.OrdinalIgnoreCase))
        {
            if (channel.InTangents is null
                || channel.OutTangents is null
                || channel.InTangents.Count != channel.Values.Count
                || channel.OutTangents.Count != channel.Values.Count)
            {
                throw new InvalidDataException("Cubic channel tangent counts do not match its values.");
            }
            amount = (float)Math.Round(amount, 5, MidpointRounding.AwayFromZero);
            var amount2 = amount * amount;
            var amount3 = amount2 * amount;
            var duration = channel.Times[right] - channel.Times[left];
            var value = (2 * amount3 - 3 * amount2 + 1) * channel.Values[left]
                + (amount3 - 2 * amount2 + amount) * duration * channel.OutTangents[left]
                + (-2 * amount3 + 3 * amount2) * channel.Values[right]
                + (amount3 - amount2) * duration * channel.InTangents[right];
            return FinalizeCubicSample(channel, value);
        }
        if (channel.Path == "rotation")
        {
            var from = Quaternion.Normalize(new Quaternion(channel.Values[left].X, channel.Values[left].Y, channel.Values[left].Z, channel.Values[left].W));
            var to = Quaternion.Normalize(new Quaternion(channel.Values[right].X, channel.Values[right].Y, channel.Values[right].Z, channel.Values[right].W));
            var rotation = Quaternion.Slerp(from, to, amount);
            return new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
        }
        return Vector4.Lerp(channel.Values[left], channel.Values[right], amount);
    }

    private static Vector4 FinalizeCubicSample(RekallAgeGlbNodeAnimationChannel channel, Vector4 value)
    {
        if (!channel.Interpolation.Equals("cubicspline", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }
        if (!float.IsFinite(value.X)
            || !float.IsFinite(value.Y)
            || !float.IsFinite(value.Z)
            || !float.IsFinite(value.W))
        {
            throw new InvalidDataException("Cubic channel produced a non-finite value.");
        }
        if (channel.Path != "rotation")
        {
            return value;
        }
        var quaternion = new Quaternion(value.X, value.Y, value.Z, value.W);
        if (quaternion.LengthSquared() < 1e-8f)
        {
            throw new InvalidDataException("Cubic rotation produced a near-zero quaternion.");
        }
        quaternion = Quaternion.Normalize(quaternion);
        return new Vector4(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
    }

    private static Matrix4x4[] BuildGlobalMatrices(
        IReadOnlyList<RekallAgeGlbSkeletonNode> nodes,
        IReadOnlyList<NodePose> poses)
    {
        var globals = new Matrix4x4[nodes.Count];
        var status = new byte[nodes.Count];
        Matrix4x4 Build(int index)
        {
            if (status[index] == 2) return globals[index];
            if (status[index] == 1) throw new InvalidDataException("GLB node hierarchy contains a cycle.");
            status[index] = 1;
            var pose = poses[index];
            var local = Matrix4x4.CreateScale(pose.Scale)
                * Matrix4x4.CreateFromQuaternion(pose.Rotation)
                * Matrix4x4.CreateTranslation(pose.Translation);
            globals[index] = nodes[index].ParentIndex >= 0 ? local * Build(nodes[index].ParentIndex) : local;
            status[index] = 2;
            return globals[index];
        }
        for (var index = 0; index < nodes.Count; index++) _ = Build(index);
        return globals;
    }

    private static JsonArray Vector(Vector3 value) => [(double)value.X, (double)value.Y, (double)value.Z];

    private static JsonArray QuaternionNode(Quaternion value) =>
        [(double)value.X, (double)value.Y, (double)value.Z, (double)value.W];

    private static JsonArray Matrix(Matrix4x4 value) =>
    [
        (double)value.M11, (double)value.M12, (double)value.M13, (double)value.M14,
        (double)value.M21, (double)value.M22, (double)value.M23, (double)value.M24,
        (double)value.M31, (double)value.M32, (double)value.M33, (double)value.M34,
        (double)value.M41, (double)value.M42, (double)value.M43, (double)value.M44
    ];

    private static double ResolveSampleTime(double rawTime, double duration, string loopMode)
    {
        if (loopMode == "clamp") return Math.Min(rawTime, duration);
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

    private static bool TryGet(JsonObject properties, string name, out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node)) return true;
        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        return properties.TryGetPropertyValue(pascal, out node);
    }

    private static string? ReadString(JsonObject properties, string name) =>
        TryGet(properties, name, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static double ReadNumber(JsonObject? properties, string name, double fallback)
    {
        if (properties is null || !TryGet(properties, name, out var node) || node is not JsonValue value) return fallback;
        if (value.TryGetValue<double>(out var number) && double.IsFinite(number)) return number;
        if (value.TryGetValue<int>(out var integer)) return integer;
        return fallback;
    }

    private static int ReadInt(JsonObject properties, string name, int fallback) =>
        (int)ReadNumber(properties, name, fallback);

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback) =>
        TryGet(properties, name, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var boolean) ? boolean : fallback;

    private static RekallAgeRuntimeObservation Observation(int frame, string code, RekallAgeRuntimeEntity entity, string message) =>
        new(frame, code, "error", "animation", entity.Id, entity.Name, "SkeletalAnimator", message, []);

    private sealed record NodePose(Vector3 Translation, Quaternion Rotation, Vector3 Scale);
}
