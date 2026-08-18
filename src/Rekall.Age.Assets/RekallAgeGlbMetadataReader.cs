using System.Buffers.Binary;
using System.Text.Json;

namespace Rekall.Age.Assets;

public static class RekallAgeGlbMetadataReader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;

    public static async ValueTask<RekallAgeGlbMetadata?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length < 20)
        {
            throw new InvalidOperationException("GLB file is too small to contain a valid header and JSON chunk.");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        if (magic != GlbMagic || version != 2 || length != bytes.Length)
        {
            throw new InvalidOperationException("GLB file has an invalid header.");
        }

        var jsonChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
        var jsonChunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4));
        if (jsonChunkType != JsonChunkType || 20 + jsonChunkLength > bytes.Length)
        {
            throw new InvalidOperationException("GLB file has an invalid JSON chunk.");
        }

        using var document = JsonDocument.Parse(bytes.AsMemory(20, (int)jsonChunkLength));
        var root = document.RootElement;
        ValidateMorphJson(root);
        var scenes = ReadArray(root, "scenes", item => new RekallAgeGlbSceneMetadata(
            ReadString(item, "name"),
            item.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array ? nodes.GetArrayLength() : 0));
        var nodes = ReadArray(root, "nodes", item => new RekallAgeGlbNodeMetadata(
            ReadString(item, "name"),
            ReadInt(item, "mesh"))
        {
            SkinIndex = ReadInt(item, "skin"),
            ChildCount = ArrayLength(item, "children"),
            MorphWeights = ReadFiniteNumbers(item, "weights", 64)
        });
        var meshes = ReadArray(root, "meshes", ReadMesh);
        ValidateMorphLayouts(nodes, meshes);
        var materials = ReadArray(root, "materials", item => new RekallAgeGlbMaterialMetadata(ReadString(item, "name")));
        var images = ReadArray(root, "images", item => new RekallAgeGlbImageMetadata(
            ReadString(item, "name"),
            ReadString(item, "mimeType"),
            ReadString(item, "uri")));
        var skins = ReadArray(root, "skins", item => new RekallAgeGlbSkinMetadata(
            ReadString(item, "name"),
            ArrayLength(item, "joints"),
            ReadInt(item, "skeleton"),
            ReadInt(item, "inverseBindMatrices")));
        var animations = ReadArray(root, "animations", item => new RekallAgeGlbAnimationMetadata(ReadString(item, "name"))
        {
            SamplerCount = ArrayLength(item, "samplers"),
            ChannelCount = ArrayLength(item, "channels"),
            Targets = ReadAnimationTargets(item)
        });

        return new RekallAgeGlbMetadata(
            scenes.Count,
            nodes.Count,
            meshes.Count,
            materials.Count,
            images.Count,
            animations.Count,
            scenes,
            nodes,
            meshes,
            materials,
            images,
            animations)
        {
            SkinCount = skins.Count,
            Skins = skins
        };
    }

    private static IReadOnlyList<RekallAgeGlbAnimationTargetMetadata> ReadAnimationTargets(JsonElement animation)
    {
        if (!animation.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RekallAgeGlbAnimationTargetMetadata>();
        }

        return channels.EnumerateArray()
            .Select(channel => channel.TryGetProperty("target", out var target)
                ? new RekallAgeGlbAnimationTargetMetadata(ReadInt(target, "node"), ReadString(target, "path"))
                : new RekallAgeGlbAnimationTargetMetadata(null, null))
            .ToArray();
    }

    private static RekallAgeGlbMeshMetadata ReadMesh(JsonElement mesh)
    {
        var primitiveCount = ArrayLength(mesh, "primitives");
        var targetCount = 0;
        if (mesh.TryGetProperty("primitives", out var primitives) && primitives.ValueKind == JsonValueKind.Array)
        {
            foreach (var primitive in primitives.EnumerateArray())
            {
                var count = ArrayLength(primitive, "targets");
                if (count > 64)
                {
                    throw new InvalidDataException("GLB morph target count exceeds the supported limit of 64.");
                }
                if (count > 0 && targetCount > 0 && count != targetCount)
                {
                    throw new InvalidDataException("GLB mesh primitives use incompatible morph target counts.");
                }
                if (count > 0) targetCount = count;
            }
        }
        var names = ReadTargetNames(mesh, targetCount);
        var weights = ReadFiniteNumbers(mesh, "weights", 64);
        if (weights.Count > 0 && weights.Count != targetCount)
        {
            throw new InvalidDataException("GLB mesh morph weight count does not match its target count.");
        }
        return new RekallAgeGlbMeshMetadata(ReadString(mesh, "name"), primitiveCount)
        {
            MorphTargetCount = targetCount,
            MorphTargetNames = names,
            DefaultMorphWeights = weights
        };
    }

    private static void ValidateMorphJson(JsonElement root)
    {
        var accessors = root.TryGetProperty("accessors", out var accessorArray) && accessorArray.ValueKind == JsonValueKind.Array
            ? accessorArray.EnumerateArray().ToArray()
            : [];
        if (!root.TryGetProperty("meshes", out var meshes) || meshes.ValueKind != JsonValueKind.Array) return;
        foreach (var mesh in meshes.EnumerateArray())
        {
            if (!mesh.TryGetProperty("primitives", out var primitives) || primitives.ValueKind != JsonValueKind.Array) continue;
            foreach (var primitive in primitives.EnumerateArray())
            {
                if (!primitive.TryGetProperty("targets", out var targets)) continue;
                if (targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() is < 1 or > 64)
                    throw new InvalidDataException("GLB morph targets must be an array with 1 to 64 entries.");
                var baseCount = ReadBaseAccessorCount(primitive, "POSITION", accessors);
                var normalCount = ReadBaseAccessorCount(primitive, "NORMAL", accessors);
                long vectorCount = 0;
                foreach (var target in targets.EnumerateArray())
                {
                    if (target.ValueKind != JsonValueKind.Object || !target.TryGetProperty("POSITION", out _))
                        throw new InvalidDataException("GLB morph target must be an object with POSITION deltas.");
                    foreach (var semantic in target.EnumerateObject())
                    {
                        if (semantic.Name is not ("POSITION" or "NORMAL"))
                            throw new InvalidDataException($"GLB morph semantic '{semantic.Name}' is unsupported.");
                        if (!semantic.Value.TryGetInt32(out var index) || index < 0 || index >= accessors.Length)
                            throw new InvalidDataException($"GLB morph {semantic.Name} accessor is invalid.");
                        var accessor = accessors[index];
                        var count = ReadInt(accessor, "count") ?? 0;
                        if (accessor.TryGetProperty("sparse", out _)
                            || ReadString(accessor, "type") != "VEC3"
                            || ReadInt(accessor, "componentType") != 5126
                            || count != baseCount
                            || (semantic.Name == "NORMAL" && normalCount != baseCount))
                            throw new InvalidDataException($"GLB morph {semantic.Name} accessor must be non-sparse float VEC3 with exactly {baseCount} entries.");
                        vectorCount += count;
                    }
                }
                if (vectorCount > 4_194_304)
                    throw new InvalidDataException("GLB morph primitive exceeds the 4194304 delta-vector limit.");
            }
        }
    }

    private static int ReadBaseAccessorCount(
        JsonElement primitive,
        string semantic,
        IReadOnlyList<JsonElement> accessors)
    {
        if (!primitive.TryGetProperty("attributes", out var attributes)
            || !attributes.TryGetProperty(semantic, out var value)
            || !value.TryGetInt32(out var index)
            || index < 0 || index >= accessors.Count)
            return 0;
        return ReadInt(accessors[index], "count") ?? 0;
    }

    private static void ValidateMorphLayouts(
        IReadOnlyList<RekallAgeGlbNodeMetadata> nodes,
        IReadOnlyList<RekallAgeGlbMeshMetadata> meshes)
    {
        IReadOnlyList<string>? expected = null;
        foreach (var mesh in meshes.Where(item => item.MorphTargetCount > 0))
        {
            if (expected is not null && !expected.SequenceEqual(mesh.MorphTargetNames, StringComparer.Ordinal))
            {
                throw new InvalidDataException("GLB morph-bearing meshes use incompatible ordered target layouts.");
            }
            expected ??= mesh.MorphTargetNames;
        }
        foreach (var node in nodes.Where(item => item.MorphWeights.Count > 0))
        {
            if (node.MeshIndex is not int meshIndex || meshIndex < 0 || meshIndex >= meshes.Count
                || node.MorphWeights.Count != meshes[meshIndex].MorphTargetCount)
            {
                throw new InvalidDataException("GLB node morph weight count does not match its mesh target count.");
            }
        }
    }

    private static IReadOnlyList<string> ReadTargetNames(JsonElement mesh, int count)
    {
        JsonElement names = default;
        var hasNames = mesh.TryGetProperty("extras", out var extras)
            && extras.ValueKind == JsonValueKind.Object
            && extras.TryGetProperty("targetNames", out names);
        if (!hasNames)
        {
            return Enumerable.Range(0, count).Select(index => $"target-{index}").ToArray();
        }
        if (names.ValueKind != JsonValueKind.Array || names.GetArrayLength() != count)
        {
            throw new InvalidDataException("GLB morph target names must exactly match the target count.");
        }
        return names.EnumerateArray().Select((name, index) =>
        {
            var value = name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            {
                throw new InvalidDataException($"GLB morph target name {index} must contain 1 to 128 characters.");
            }
            return value;
        }).ToArray()!;
    }

    private static IReadOnlyList<double> ReadFiniteNumbers(JsonElement source, string name, int maximumCount)
    {
        if (!source.TryGetProperty(name, out var array)) return Array.Empty<double>();
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > maximumCount)
        {
            throw new InvalidDataException($"GLB {name} must be an array with at most {maximumCount} entries.");
        }
        return array.EnumerateArray().Select((item, index) =>
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var value)
                || !double.IsFinite(value) || Math.Abs(value) > 1_000_000)
            {
                throw new InvalidDataException($"GLB {name} entry {index} must be finite with absolute value at most 1000000.");
            }
            return value;
        }).ToArray();
    }

    private static int ArrayLength(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;
    }

    private static IReadOnlyList<T> ReadArray<T>(
        JsonElement root,
        string propertyName,
        Func<JsonElement, T> map)
    {
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<T>();
        }

        return array.EnumerateArray().Select(map).ToArray();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }
}
