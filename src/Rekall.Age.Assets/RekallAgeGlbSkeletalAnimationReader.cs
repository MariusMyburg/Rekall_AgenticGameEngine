using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace Rekall.Age.Assets;

public sealed record RekallAgeGlbSkeletalAsset(
    IReadOnlyList<RekallAgeGlbSkeletonNode> Nodes,
    IReadOnlyList<RekallAgeGlbSkin> Skins,
    IReadOnlyList<RekallAgeGlbNodeAnimation> Animations);

public sealed record RekallAgeGlbSkeletonNode(
    string? Name,
    int ParentIndex,
    Vector3 Translation,
    Quaternion Rotation,
    Vector3 Scale);

public sealed record RekallAgeGlbSkin(
    string? Name,
    int? SkeletonNodeIndex,
    IReadOnlyList<int> JointNodeIndexes,
    IReadOnlyList<Matrix4x4> InverseBindMatrices);

public sealed record RekallAgeGlbNodeAnimation(
    string? Name,
    double DurationSeconds,
    IReadOnlyList<RekallAgeGlbNodeAnimationChannel> Channels);

public sealed record RekallAgeGlbNodeAnimationChannel(
    int NodeIndex,
    string Path,
    string Interpolation,
    IReadOnlyList<float> Times,
    IReadOnlyList<Vector4> Values,
    IReadOnlyList<Vector4>? InTangents = null,
    IReadOnlyList<Vector4>? OutTangents = null);

public static class RekallAgeGlbSkeletalAnimationReader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;
    private const int MaximumFileBytes = 256 * 1024 * 1024;
    private const int MaximumNodes = 65_536;
    private const int MaximumSkins = 1_024;
    private const int MaximumJointsPerSkin = 4_096;
    private const int MaximumAnimations = 1_024;
    private const int MaximumChannelsPerAnimation = 16_384;
    private const int MaximumKeysPerChannel = 1_000_000;

    public static async ValueTask<RekallAgeGlbSkeletalAsset> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("GLB skeletal asset was not found.", path);
        }
        if (!file.Extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Skeletal animation assets must use the .glb container.");
        }
        if (file.Length > MaximumFileBytes)
        {
            throw new InvalidDataException($"GLB skeletal asset exceeds the {MaximumFileBytes}-byte limit.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var chunks = ReadChunks(bytes);
        using var document = JsonDocument.Parse(chunks.Json);
        var root = document.RootElement;
        var nodes = ReadArray(root, "nodes");
        var skins = ReadArray(root, "skins");
        var animations = ReadArray(root, "animations");
        var accessors = ReadArray(root, "accessors");
        var bufferViews = ReadArray(root, "bufferViews");
        EnsureLimit(nodes.Count, MaximumNodes, "nodes");
        EnsureLimit(skins.Count, MaximumSkins, "skins");
        EnsureLimit(animations.Count, MaximumAnimations, "animations");

        return new RekallAgeGlbSkeletalAsset(
            ReadNodes(nodes),
            ReadSkins(skins, accessors, bufferViews, chunks.Binary),
            ReadAnimations(animations, accessors, bufferViews, chunks.Binary));
    }

    private static IReadOnlyList<RekallAgeGlbSkeletonNode> ReadNodes(IReadOnlyList<JsonElement> nodes)
    {
        var parents = Enumerable.Repeat(-1, nodes.Count).ToArray();
        for (var parent = 0; parent < nodes.Count; parent++)
        {
            if (!nodes[parent].TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var child in children.EnumerateArray())
            {
                if (!child.TryGetInt32(out var childIndex) || childIndex < 0 || childIndex >= nodes.Count)
                {
                    throw new InvalidDataException("GLB node hierarchy contains an out-of-range child index.");
                }
                if (parents[childIndex] >= 0)
                {
                    throw new InvalidDataException("GLB node hierarchy assigns more than one parent to a node.");
                }
                parents[childIndex] = parent;
            }
        }

        return nodes.Select((node, index) => new RekallAgeGlbSkeletonNode(
            ReadString(node, "name"),
            parents[index],
            ReadVector3(node, "translation", Vector3.Zero),
            ReadQuaternion(node, "rotation", Quaternion.Identity),
            ReadVector3(node, "scale", Vector3.One))).ToArray();
    }

    private static IReadOnlyList<RekallAgeGlbSkin> ReadSkins(
        IReadOnlyList<JsonElement> skins,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> binary)
    {
        var result = new List<RekallAgeGlbSkin>(skins.Count);
        foreach (var skin in skins)
        {
            var joints = skin.TryGetProperty("joints", out var jointArray) && jointArray.ValueKind == JsonValueKind.Array
                ? jointArray.EnumerateArray().Select(item => item.GetInt32()).ToArray()
                : [];
            EnsureLimit(joints.Length, MaximumJointsPerSkin, "joints per skin");
            IReadOnlyList<Matrix4x4> matrices = Enumerable.Repeat(Matrix4x4.Identity, joints.Length).ToArray();
            if (TryReadInt(skin, "inverseBindMatrices", out var accessorIndex))
            {
                var values = ReadFloatAccessor(accessorIndex, 16, accessors, bufferViews, binary);
                if (values.Count != joints.Length)
                {
                    throw new InvalidDataException("GLB inverse bind matrix count must match the skin joint count.");
                }
                matrices = values.Select(value => Matrix4x4.Transpose(new Matrix4x4(
                    value[0], value[1], value[2], value[3],
                    value[4], value[5], value[6], value[7],
                    value[8], value[9], value[10], value[11],
                    value[12], value[13], value[14], value[15]))).ToArray();
            }
            result.Add(new RekallAgeGlbSkin(
                ReadString(skin, "name"),
                TryReadInt(skin, "skeleton", out var skeleton) ? skeleton : null,
                joints,
                matrices));
        }
        return result;
    }

    private static IReadOnlyList<RekallAgeGlbNodeAnimation> ReadAnimations(
        IReadOnlyList<JsonElement> animations,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> binary)
    {
        var result = new List<RekallAgeGlbNodeAnimation>(animations.Count);
        foreach (var animation in animations)
        {
            var samplers = ReadArray(animation, "samplers");
            var channels = ReadArray(animation, "channels");
            EnsureLimit(channels.Count, MaximumChannelsPerAnimation, "animation channels");
            var parsed = new List<RekallAgeGlbNodeAnimationChannel>(channels.Count);
            foreach (var channel in channels)
            {
                if (!TryReadInt(channel, "sampler", out var samplerIndex)
                    || samplerIndex < 0 || samplerIndex >= samplers.Count
                    || !channel.TryGetProperty("target", out var target)
                    || !TryReadInt(target, "node", out var nodeIndex))
                {
                    throw new InvalidDataException("GLB animation channel has an invalid sampler or target node.");
                }
                var path = ReadString(target, "path")?.ToLowerInvariant();
                var componentCount = path switch
                {
                    "translation" or "scale" => 3,
                    "rotation" => 4,
                    _ => throw new InvalidDataException($"GLB animation target path '{path}' is unsupported.")
                };
                var sampler = samplers[samplerIndex];
                if (!TryReadInt(sampler, "input", out var input) || !TryReadInt(sampler, "output", out var output))
                {
                    throw new InvalidDataException("GLB animation sampler must reference input and output accessors.");
                }
                var times = ReadFloatAccessor(input, 1, accessors, bufferViews, binary)
                    .Select(value => value[0]).ToArray();
                EnsureLimit(times.Length, MaximumKeysPerChannel, "animation keys per channel");
                var interpolation = (ReadString(sampler, "interpolation") ?? "LINEAR").ToLowerInvariant();
                if (interpolation is not ("linear" or "step" or "cubicspline"))
                {
                    throw new InvalidDataException($"GLB animation interpolation '{interpolation}' is unsupported.");
                }
                var rawValues = ReadFloatAccessor(output, componentCount, accessors, bufferViews, binary)
                    .Select(value => new Vector4(
                        value[0],
                        value.Length > 1 ? value[1] : 0,
                        value.Length > 2 ? value[2] : 0,
                        value.Length > 3 ? value[3] : 0))
                    .ToArray();
                EnsureLimit(rawValues.Length, MaximumKeysPerChannel * 3, "animation output records per channel");
                if (times.Length == 0)
                {
                    throw new InvalidDataException("GLB animation input times must be non-empty.");
                }
                for (var index = 0; index < times.Length; index++)
                {
                    if (!float.IsFinite(times[index])
                        || (index > 0 && (interpolation == "cubicspline"
                            ? times[index] <= times[index - 1]
                            : times[index] < times[index - 1])))
                    {
                        throw new InvalidDataException(interpolation == "cubicspline"
                            ? "GLB cubic animation input times must be finite and strictly increasing."
                            : "GLB animation input times must be finite and non-decreasing.");
                    }
                }
                if (rawValues.Any(value => !IsFinite(value)))
                {
                    throw new InvalidDataException("GLB animation values and tangents must be finite.");
                }

                Vector4[] values;
                Vector4[]? inTangents = null;
                Vector4[]? outTangents = null;
                if (interpolation == "cubicspline")
                {
                    if (rawValues.Length != times.Length * 3)
                    {
                        throw new InvalidDataException("GLB cubic animation output count must be exactly three times the input time count.");
                    }
                    values = new Vector4[times.Length];
                    inTangents = new Vector4[times.Length];
                    outTangents = new Vector4[times.Length];
                    for (var index = 0; index < times.Length; index++)
                    {
                        inTangents[index] = rawValues[index * 3];
                        values[index] = rawValues[index * 3 + 1];
                        outTangents[index] = rawValues[index * 3 + 2];
                    }
                }
                else
                {
                    if (rawValues.Length != times.Length)
                    {
                        throw new InvalidDataException("GLB animation input and output accessor counts must match.");
                    }
                    values = rawValues;
                }
                parsed.Add(new RekallAgeGlbNodeAnimationChannel(
                    nodeIndex,
                    path,
                    interpolation,
                    times,
                    values,
                    inTangents,
                    outTangents));
            }
            result.Add(new RekallAgeGlbNodeAnimation(
                ReadString(animation, "name"),
                parsed.SelectMany(channel => channel.Times).DefaultIfEmpty(0).Max(),
                parsed));
        }
        return result;
    }

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && float.IsFinite(value.Z)
        && float.IsFinite(value.W);

    private static IReadOnlyList<float[]> ReadFloatAccessor(
        int accessorIndex,
        int expectedComponents,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> binary)
    {
        if (accessorIndex < 0 || accessorIndex >= accessors.Count)
        {
            throw new InvalidDataException("GLB accessor index is out of range.");
        }
        var accessor = accessors[accessorIndex];
        if (ReadInt(accessor, "componentType", 0) != 5126
            || ComponentCount(ReadString(accessor, "type")) != expectedComponents
            || !TryReadInt(accessor, "bufferView", out var viewIndex)
            || viewIndex < 0 || viewIndex >= bufferViews.Count)
        {
            throw new InvalidDataException("GLB skeletal animation accessors must be float accessors with the expected shape.");
        }
        var view = bufferViews[viewIndex];
        if (ReadInt(view, "buffer", 0) != 0)
        {
            throw new InvalidDataException("GLB skeletal animation currently requires the embedded binary buffer.");
        }
        var count = ReadInt(accessor, "count", -1);
        if (count < 0 || count > MaximumKeysPerChannel)
        {
            throw new InvalidDataException("GLB accessor count is invalid or exceeds the runtime limit.");
        }
        var elementBytes = checked(expectedComponents * sizeof(float));
        var stride = ReadInt(view, "byteStride", elementBytes);
        if (stride < elementBytes)
        {
            throw new InvalidDataException("GLB buffer view byte stride is smaller than its accessor element.");
        }
        var start = checked(ReadInt(view, "byteOffset", 0) + ReadInt(accessor, "byteOffset", 0));
        var end = checked(start + Math.Max(0, count - 1) * stride + elementBytes);
        var viewEnd = checked(ReadInt(view, "byteOffset", 0) + ReadInt(view, "byteLength", 0));
        if (start < 0 || end > viewEnd || end > binary.Length)
        {
            throw new InvalidDataException("GLB accessor range exceeds its buffer view or binary chunk.");
        }
        var span = binary.Span;
        var result = new float[count][];
        for (var item = 0; item < count; item++)
        {
            var values = new float[expectedComponents];
            for (var component = 0; component < expectedComponents; component++)
            {
                var bits = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(start + item * stride + component * 4, 4));
                values[component] = BitConverter.Int32BitsToSingle(bits);
                if (!float.IsFinite(values[component]))
                {
                    throw new InvalidDataException("GLB skeletal animation accessor contains a non-finite value.");
                }
            }
            result[item] = values;
        }
        return result;
    }

    private static GlbChunks ReadChunks(byte[] bytes)
    {
        if (bytes.Length < 20
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) != GlbMagic
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 2
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4)) != bytes.Length)
        {
            throw new InvalidDataException("GLB skeletal asset has an invalid header.");
        }
        ReadOnlyMemory<byte> json = default;
        ReadOnlyMemory<byte> binary = default;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
            var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;
            if (length < 0 || offset + length > bytes.Length)
            {
                throw new InvalidDataException("GLB chunk range exceeds the file.");
            }
            if (type == JsonChunkType && json.IsEmpty)
            {
                json = bytes.AsMemory(offset, length);
            }
            else if (type == BinChunkType && binary.IsEmpty)
            {
                binary = bytes.AsMemory(offset, length);
            }
            offset += length;
        }
        if (json.IsEmpty)
        {
            throw new InvalidDataException("GLB skeletal asset has no JSON chunk.");
        }
        return new GlbChunks(json, binary);
    }

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().ToArray()
            : [];
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int ReadInt(JsonElement element, string name, int fallback) =>
        TryReadInt(element, name, out var value) ? value : fallback;

    private static bool TryReadInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static Vector3 ReadVector3(JsonElement element, string name, Vector3 fallback)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3)
        {
            return fallback;
        }
        return new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
    }

    private static Quaternion ReadQuaternion(JsonElement element, string name, Quaternion fallback)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 4)
        {
            return fallback;
        }
        return Quaternion.Normalize(new Quaternion(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle(), value[3].GetSingle()));
    }

    private static int ComponentCount(string? type) => type switch
    {
        "SCALAR" => 1,
        "VEC3" => 3,
        "VEC4" => 4,
        "MAT4" => 16,
        _ => 0
    };

    private static void EnsureLimit(int count, int maximum, string subject)
    {
        if (count > maximum)
        {
            throw new InvalidDataException($"GLB {subject} count {count} exceeds the runtime limit {maximum}.");
        }
    }

    private readonly record struct GlbChunks(ReadOnlyMemory<byte> Json, ReadOnlyMemory<byte> Binary);
}
