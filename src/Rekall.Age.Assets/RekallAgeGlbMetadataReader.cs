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
        var scenes = ReadArray(root, "scenes", item => new RekallAgeGlbSceneMetadata(
            ReadString(item, "name"),
            item.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array ? nodes.GetArrayLength() : 0));
        var nodes = ReadArray(root, "nodes", item => new RekallAgeGlbNodeMetadata(
            ReadString(item, "name"),
            ReadInt(item, "mesh")));
        var meshes = ReadArray(root, "meshes", item => new RekallAgeGlbMeshMetadata(
            ReadString(item, "name"),
            item.TryGetProperty("primitives", out var primitives) && primitives.ValueKind == JsonValueKind.Array
                ? primitives.GetArrayLength()
                : 0));
        var materials = ReadArray(root, "materials", item => new RekallAgeGlbMaterialMetadata(ReadString(item, "name")));
        var images = ReadArray(root, "images", item => new RekallAgeGlbImageMetadata(
            ReadString(item, "name"),
            ReadString(item, "mimeType"),
            ReadString(item, "uri")));
        var animations = ReadArray(root, "animations", item => new RekallAgeGlbAnimationMetadata(ReadString(item, "name")));

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
            animations);
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
