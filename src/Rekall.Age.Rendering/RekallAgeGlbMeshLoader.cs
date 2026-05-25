using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using StbImageSharp;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeGlbMeshLoader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;
    private const int MaxVerticesPerMesh = 16_000_000;

    public async ValueTask<IReadOnlyList<RekallAgeVulkanSceneMesh>> LoadAsync(
        string assetId,
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var glb = ReadGlb(bytes);
        using var document = JsonDocument.Parse(glb.Json);
        var root = document.RootElement;
        if (!root.TryGetProperty("meshes", out var meshesElement) || meshesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var bufferViews = ReadArray(root, "bufferViews");
        var accessors = ReadArray(root, "accessors");
        var nodes = ReadArray(root, "nodes");
        var textures = ReadTextures(root, bufferViews, glb.Bin);
        var materials = ReadMaterials(assetId, root, textures);
        var rootNodeIndexes = ReadSceneRootNodes(root, nodes.Count);
        var meshes = new List<RekallAgeVulkanSceneMesh>();
        foreach (var nodeIndex in rootNodeIndexes)
        {
            AddNodeMeshes(
                assetId,
                meshesElement,
                nodes,
                bufferViews,
                accessors,
                materials,
                glb.Bin,
                nodeIndex,
                Matrix4x4.Identity,
                meshes);
        }

        if (meshes.Count == 0)
        {
            for (var meshIndex = 0; meshIndex < meshesElement.GetArrayLength(); meshIndex++)
            {
                AddMeshPrimitives(
                    assetId,
                    meshesElement[meshIndex],
                    meshIndex,
                    Matrix4x4.Identity,
                    bufferViews,
                    accessors,
                    materials,
                    glb.Bin,
                    meshes);
            }
        }

        return meshes;
    }

    private static void AddNodeMeshes(
        string assetId,
        JsonElement meshesElement,
        IReadOnlyList<JsonElement> nodes,
        IReadOnlyList<JsonElement> bufferViews,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<MaterialInfo> materials,
        ReadOnlyMemory<byte> bin,
        int nodeIndex,
        Matrix4x4 parentTransform,
        List<RekallAgeVulkanSceneMesh> meshes)
    {
        if (nodeIndex < 0 || nodeIndex >= nodes.Count)
        {
            return;
        }

        var node = nodes[nodeIndex];
        var transform = ReadNodeTransform(node) * parentTransform;
        if (node.TryGetProperty("mesh", out var meshElement)
            && meshElement.ValueKind == JsonValueKind.Number
            && meshElement.TryGetInt32(out var meshIndex)
            && meshIndex >= 0
            && meshIndex < meshesElement.GetArrayLength())
        {
            AddMeshPrimitives(
                assetId,
                meshesElement[meshIndex],
                meshIndex,
                transform,
                bufferViews,
                accessors,
                materials,
                bin,
                meshes);
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out var childIndex))
                {
                    AddNodeMeshes(
                        assetId,
                        meshesElement,
                        nodes,
                        bufferViews,
                        accessors,
                        materials,
                        bin,
                        childIndex,
                        transform,
                        meshes);
                }
            }
        }
    }

    private static void AddMeshPrimitives(
        string assetId,
        JsonElement mesh,
        int meshIndex,
        Matrix4x4 transform,
        IReadOnlyList<JsonElement> bufferViews,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<MaterialInfo> materials,
        ReadOnlyMemory<byte> bin,
        List<RekallAgeVulkanSceneMesh> output)
    {
        if (!mesh.TryGetProperty("primitives", out var primitives) || primitives.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var primitiveIndex = 0;
        foreach (var primitive in primitives.EnumerateArray())
        {
            if (ReadInt(primitive, "mode", 4) != 4
                || !primitive.TryGetProperty("attributes", out var attributes)
                || !attributes.TryGetProperty("POSITION", out var positionAccessorElement)
                || !positionAccessorElement.TryGetInt32(out var positionAccessor))
            {
                primitiveIndex++;
                continue;
            }

            var positions = ReadVec3Accessor(positionAccessor, accessors, bufferViews, bin);
            if (positions.Count == 0)
            {
                primitiveIndex++;
                continue;
            }

            var normals = attributes.TryGetProperty("NORMAL", out var normalAccessorElement)
                && normalAccessorElement.TryGetInt32(out var normalAccessor)
                ? ReadVec3Accessor(normalAccessor, accessors, bufferViews, bin)
                : [];
            var colors = attributes.TryGetProperty("COLOR_0", out var colorAccessorElement)
                && colorAccessorElement.TryGetInt32(out var colorAccessor)
                ? ReadColorAccessor(colorAccessor, accessors, bufferViews, bin)
                : [];
            var uvs = attributes.TryGetProperty("TEXCOORD_0", out var uvAccessorElement)
                && uvAccessorElement.TryGetInt32(out var uvAccessor)
                ? ReadVec2Accessor(uvAccessor, accessors, bufferViews, bin)
                : [];
            var material = ResolveMaterial(primitive, materials);
            var indices = primitive.TryGetProperty("indices", out var indicesElement)
                && indicesElement.TryGetInt32(out var indicesAccessor)
                ? ReadIndexAccessor(indicesAccessor, accessors, bufferViews, bin)
                : Enumerable.Range(0, positions.Count).ToArray();

            AddChunkedIndexedTriangles(
                assetId,
                meshIndex,
                primitiveIndex,
                transform,
                positions,
                normals,
                colors,
                uvs,
                material,
                indices,
                output);
            primitiveIndex++;
        }
    }

    private static void AddChunkedIndexedTriangles(
        string assetId,
        int meshIndex,
        int primitiveIndex,
        Matrix4x4 transform,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector4> colors,
        IReadOnlyList<Vector2> uvs,
        MaterialInfo material,
        IReadOnlyList<int> sourceIndices,
        List<RekallAgeVulkanSceneMesh> output)
    {
        var vertices = new List<RekallAgeVulkanSceneVertex>(Math.Min(sourceIndices.Count, MaxVerticesPerMesh));
        var indices = new List<uint>(Math.Min(sourceIndices.Count, MaxVerticesPerMesh));
        var remap = new Dictionary<int, uint>();
        var chunk = 0;
        for (var i = 0; i + 2 < sourceIndices.Count; i += 3)
        {
            var a = sourceIndices[i];
            var b = sourceIndices[i + 1];
            var c = sourceIndices[i + 2];
            if (!IsValidIndex(a) || !IsValidIndex(b) || !IsValidIndex(c))
            {
                continue;
            }

            var newVertexCount = CountMissing(a, b, c);
            if (vertices.Count > 0 && vertices.Count + newVertexCount > MaxVerticesPerMesh)
            {
                Flush();
            }

            indices.Add(GetOrAddVertex(a));
            indices.Add(GetOrAddVertex(b));
            indices.Add(GetOrAddVertex(c));
        }

        Flush();

        bool IsValidIndex(int sourceIndex)
        {
            return sourceIndex >= 0 && sourceIndex < positions.Count;
        }

        int CountMissing(int a, int b, int c)
        {
            var count = 0;
            if (!remap.ContainsKey(a))
            {
                count++;
            }

            if (b != a && !remap.ContainsKey(b))
            {
                count++;
            }

            if (c != a && c != b && !remap.ContainsKey(c))
            {
                count++;
            }

            return count;
        }

        uint GetOrAddVertex(int sourceIndex)
        {
            if (remap.TryGetValue(sourceIndex, out var existing))
            {
                return existing;
            }

            var position = Vector3.Transform(positions[sourceIndex], transform);
            var normal = sourceIndex < normals.Count
                ? Vector3.TransformNormal(normals[sourceIndex], transform)
                : Vector3.UnitY;
            if (normal.LengthSquared() <= 0.000001f)
            {
                normal = Vector3.UnitY;
            }
            else
            {
                normal = Vector3.Normalize(normal);
            }

            var uv = sourceIndex < uvs.Count ? uvs[sourceIndex] : Vector2.Zero;
            var color = sourceIndex < colors.Count
                ? Multiply(colors[sourceIndex], material.BaseColor)
                : material.BaseColor;

            var vertexIndex = checked((uint)vertices.Count);
            vertices.Add(new RekallAgeVulkanSceneVertex(
                position.X,
                position.Y,
                position.Z,
                normal.X,
                normal.Y,
                normal.Z,
                color.X,
                color.Y,
                color.Z,
                color.W,
                uv.X,
                uv.Y));
            remap[sourceIndex] = vertexIndex;
            return vertexIndex;
        }

        void Flush()
        {
            if (vertices.Count == 0)
            {
                return;
            }

            output.Add(new RekallAgeVulkanSceneMesh(
                assetId,
                $"{assetId} mesh {meshIndex}:{primitiveIndex}:{chunk++}",
                "glb",
                vertices.ToArray(),
                indices.ToArray(),
                material.BaseColorTexture,
                material.MetallicRoughnessTexture,
                material.NormalTexture,
                material.OcclusionTexture,
                material.MetallicFactor,
                material.RoughnessFactor,
                material.NormalScale,
                material.OcclusionStrength));
            vertices.Clear();
            indices.Clear();
            remap.Clear();
        }
    }

    private static IReadOnlyList<Vector3> ReadVec3Accessor(
        int accessorIndex,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> bin)
    {
        if (!TryResolveAccessor(accessorIndex, accessors, bufferViews, bin, out var accessor, out var view, out var bytes)
            || ReadString(accessor, "type") != "VEC3"
            || ReadInt(accessor, "componentType", 0) != 5126)
        {
            return [];
        }

        var count = ReadInt(accessor, "count", 0);
        var stride = ReadInt(view, "byteStride", 12);
        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var offset = i * stride;
            if (offset + 12 > bytes.Length)
            {
                break;
            }

            result[i] = new Vector3(
                ReadSingle(bytes.Span, offset),
                ReadSingle(bytes.Span, offset + 4),
                ReadSingle(bytes.Span, offset + 8));
        }

        return result;
    }

    private static IReadOnlyList<Vector2> ReadVec2Accessor(
        int accessorIndex,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> bin)
    {
        if (!TryResolveAccessor(accessorIndex, accessors, bufferViews, bin, out var accessor, out var view, out var bytes)
            || ReadString(accessor, "type") != "VEC2"
            || ReadInt(accessor, "componentType", 0) != 5126)
        {
            return [];
        }

        var count = ReadInt(accessor, "count", 0);
        var stride = ReadInt(view, "byteStride", 8);
        var result = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var offset = i * stride;
            if (offset + 8 > bytes.Length)
            {
                break;
            }

            result[i] = new Vector2(
                ReadSingle(bytes.Span, offset),
                ReadSingle(bytes.Span, offset + 4));
        }

        return result;
    }


    private static IReadOnlyList<Vector4> ReadColorAccessor(
        int accessorIndex,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> bin)
    {
        if (!TryResolveAccessor(accessorIndex, accessors, bufferViews, bin, out var accessor, out var view, out var bytes)
            || ReadInt(accessor, "componentType", 0) != 5126)
        {
            return [];
        }

        var type = ReadString(accessor, "type");
        var components = type == "VEC4" ? 4 : type == "VEC3" ? 3 : 0;
        if (components == 0)
        {
            return [];
        }

        var count = ReadInt(accessor, "count", 0);
        var stride = ReadInt(view, "byteStride", components * 4);
        var result = new Vector4[count];
        for (var i = 0; i < count; i++)
        {
            var offset = i * stride;
            if (offset + components * 4 > bytes.Length)
            {
                break;
            }

            result[i] = new Vector4(
                ReadSingle(bytes.Span, offset),
                ReadSingle(bytes.Span, offset + 4),
                ReadSingle(bytes.Span, offset + 8),
                components == 4 ? ReadSingle(bytes.Span, offset + 12) : 1);
        }

        return result;
    }

    private static IReadOnlyList<int> ReadIndexAccessor(
        int accessorIndex,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> bin)
    {
        if (!TryResolveAccessor(accessorIndex, accessors, bufferViews, bin, out var accessor, out var view, out var bytes))
        {
            return [];
        }

        var count = ReadInt(accessor, "count", 0);
        var componentType = ReadInt(accessor, "componentType", 0);
        var componentBytes = componentType switch
        {
            5121 => 1,
            5123 => 2,
            5125 => 4,
            _ => 0
        };
        if (componentBytes == 0)
        {
            return [];
        }

        var stride = ReadInt(view, "byteStride", componentBytes);
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            var offset = i * stride;
            if (offset + componentBytes > bytes.Length)
            {
                break;
            }

            result[i] = componentType switch
            {
                5121 => bytes.Span[offset],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Span.Slice(offset, 2)),
                _ => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span.Slice(offset, 4)))
            };
        }

        return result;
    }

    private static bool TryResolveAccessor(
        int accessorIndex,
        IReadOnlyList<JsonElement> accessors,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> bin,
        out JsonElement accessor,
        out JsonElement bufferView,
        out ReadOnlyMemory<byte> bytes)
    {
        accessor = default;
        bufferView = default;
        bytes = default;
        if (accessorIndex < 0 || accessorIndex >= accessors.Count)
        {
            return false;
        }

        accessor = accessors[accessorIndex];
        var viewIndex = ReadInt(accessor, "bufferView", -1);
        if (viewIndex < 0 || viewIndex >= bufferViews.Count)
        {
            return false;
        }

        bufferView = bufferViews[viewIndex];
        var offset = ReadInt(bufferView, "byteOffset", 0) + ReadInt(accessor, "byteOffset", 0);
        var length = ReadInt(bufferView, "byteLength", 0) - ReadInt(accessor, "byteOffset", 0);
        if (offset < 0 || length <= 0 || offset + length > bin.Length)
        {
            return false;
        }

        bytes = bin.Slice(offset, length);
        return true;
    }

    private static bool TryResolveBufferView(
        int viewIndex,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> bin,
        out ReadOnlyMemory<byte> bytes)
    {
        bytes = default;
        if (viewIndex < 0 || viewIndex >= bufferViews.Count)
        {
            return false;
        }

        var bufferView = bufferViews[viewIndex];
        var offset = ReadInt(bufferView, "byteOffset", 0);
        var length = ReadInt(bufferView, "byteLength", 0);
        if (offset < 0 || length <= 0 || offset + length > bin.Length)
        {
            return false;
        }

        bytes = bin.Slice(offset, length);
        return true;
    }

    private static IReadOnlyList<TextureInfo> ReadTextures(
        JsonElement root,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> bin)
    {
        var images = ReadImages(root, bufferViews, bin);
        if (!root.TryGetProperty("textures", out var textures) || textures.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return textures.EnumerateArray()
            .Select(texture =>
            {
                var source = ReadInt(texture, "source", -1);
                return source >= 0 && source < images.Count
                    ? new TextureInfo(images[source], ReadSampler(root, ReadInt(texture, "sampler", -1)))
                    : new TextureInfo(null, DefaultSampler());
            })
            .ToArray();
    }

    private static IReadOnlyList<TextureImage?> ReadImages(
        JsonElement root,
        IReadOnlyList<JsonElement> bufferViews,
        ReadOnlyMemory<byte> bin)
    {
        if (!root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return images.EnumerateArray()
            .Select(image =>
            {
                var viewIndex = ReadInt(image, "bufferView", -1);
                if (viewIndex < 0
                    || viewIndex >= bufferViews.Count
                    || !TryResolveBufferView(viewIndex, bufferViews, bin, out var bytes))
                {
                    return null;
                }

                try
                {
                    var decoded = ImageResult.FromMemory(bytes.ToArray(), ColorComponents.RedGreenBlueAlpha);
                    return new TextureImage(decoded.Width, decoded.Height, decoded.Data);
                }
                catch (Exception ex) when (ex is InvalidOperationException
                    or NotSupportedException
                    or ArgumentException)
                {
                    return null;
                }
            })
            .ToArray();
    }

    private static IReadOnlyList<MaterialInfo> ReadMaterials(
        string assetId,
        JsonElement root,
        IReadOnlyList<TextureInfo> textures)
    {
        if (!root.TryGetProperty("materials", out var materials) || materials.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return materials.EnumerateArray()
            .Select(material =>
            {
                RekallAgeVulkanSceneTexture? baseColorTexture = null;
                RekallAgeVulkanSceneTexture? metallicRoughnessTexture = null;
                RekallAgeVulkanSceneTexture? normalTexture = null;
                RekallAgeVulkanSceneTexture? occlusionTexture = null;
                var baseColor = new Vector4(0.72f, 0.78f, 0.86f, 1);
                var metallicFactor = 0f;
                var roughnessFactor = 1f;
                var normalScale = 1f;
                var occlusionStrength = 1f;
                if (material.TryGetProperty("pbrMetallicRoughness", out var pbr)
                    && pbr.ValueKind == JsonValueKind.Object)
                {
                    if (pbr.TryGetProperty("baseColorFactor", out var color)
                        && color.ValueKind == JsonValueKind.Array)
                    {
                        var values = color.EnumerateArray().Select(item => item.GetSingle()).ToArray();
                        if (values.Length >= 3)
                        {
                            baseColor = new Vector4(values[0], values[1], values[2], values.Length >= 4 ? values[3] : 1);
                        }
                    }

                    if (pbr.TryGetProperty("baseColorTexture", out var texture)
                        && texture.ValueKind == JsonValueKind.Object)
                    {
                        var textureIndex = ReadInt(texture, "index", -1);
                        if (textureIndex >= 0
                            && textureIndex < textures.Count)
                        {
                            baseColorTexture = textures[textureIndex].ToSceneTexture($"{assetId}/texture/{textureIndex}");
                        }
                    }

                    metallicFactor = ReadFloat(pbr, "metallicFactor", metallicFactor);
                    roughnessFactor = ReadFloat(pbr, "roughnessFactor", roughnessFactor);
                    if (pbr.TryGetProperty("metallicRoughnessTexture", out var metallicRoughness)
                        && metallicRoughness.ValueKind == JsonValueKind.Object)
                    {
                        var textureIndex = ReadInt(metallicRoughness, "index", -1);
                        if (textureIndex >= 0
                            && textureIndex < textures.Count)
                        {
                            metallicRoughnessTexture = textures[textureIndex].ToSceneTexture($"{assetId}/texture/{textureIndex}");
                        }
                    }
                }

                if (material.TryGetProperty("normalTexture", out var normal)
                    && normal.ValueKind == JsonValueKind.Object)
                {
                    var textureIndex = ReadInt(normal, "index", -1);
                    normalScale = ReadFloat(normal, "scale", normalScale);
                    if (textureIndex >= 0
                        && textureIndex < textures.Count)
                    {
                        normalTexture = textures[textureIndex].ToSceneTexture($"{assetId}/texture/{textureIndex}");
                    }
                }

                if (material.TryGetProperty("occlusionTexture", out var occlusion)
                    && occlusion.ValueKind == JsonValueKind.Object)
                {
                    var textureIndex = ReadInt(occlusion, "index", -1);
                    occlusionStrength = ReadFloat(occlusion, "strength", occlusionStrength);
                    if (textureIndex >= 0
                        && textureIndex < textures.Count)
                    {
                        occlusionTexture = textures[textureIndex].ToSceneTexture($"{assetId}/texture/{textureIndex}");
                    }
                }

                return new MaterialInfo(
                    baseColor,
                    baseColorTexture,
                    metallicRoughnessTexture,
                    normalTexture,
                    occlusionTexture,
                    Math.Clamp(metallicFactor, 0, 1),
                    Math.Clamp(roughnessFactor, 0.04f, 1),
                    Math.Clamp(normalScale, 0, 4),
                    Math.Clamp(occlusionStrength, 0, 1));
            })
            .ToArray();
    }

    private static MaterialInfo ResolveMaterial(JsonElement primitive, IReadOnlyList<MaterialInfo> materials)
    {
        var materialIndex = ReadInt(primitive, "material", -1);
        return materialIndex >= 0 && materialIndex < materials.Count
            ? materials[materialIndex]
            : MaterialInfo.Default;
    }

    private static Vector4 Multiply(Vector4 left, Vector4 right)
    {
        return new Vector4(
            left.X * right.X,
            left.Y * right.Y,
            left.Z * right.Z,
            left.W * right.W);
    }

    private static RekallAgeVulkanSceneSampler ReadSampler(JsonElement root, int samplerIndex)
    {
        if (samplerIndex < 0
            || !root.TryGetProperty("samplers", out var samplers)
            || samplers.ValueKind != JsonValueKind.Array
            || samplerIndex >= samplers.GetArrayLength())
        {
            return DefaultSampler();
        }

        var sampler = samplers[samplerIndex];
        return new RekallAgeVulkanSceneSampler(
            ToFilter(ReadInt(sampler, "minFilter", 9729)),
            ToFilter(ReadInt(sampler, "magFilter", 9729)),
            ToWrap(ReadInt(sampler, "wrapS", 10497)),
            ToWrap(ReadInt(sampler, "wrapT", 10497)));
    }

    private static RekallAgeVulkanSceneSampler DefaultSampler()
    {
        return new RekallAgeVulkanSceneSampler(
            RekallAgeVulkanSceneFilter.Linear,
            RekallAgeVulkanSceneFilter.Linear,
            RekallAgeVulkanSceneWrapMode.Repeat,
            RekallAgeVulkanSceneWrapMode.Repeat);
    }

    private static RekallAgeVulkanSceneFilter ToFilter(int filter)
    {
        return filter is 9728 or 9984 or 9986
            ? RekallAgeVulkanSceneFilter.Nearest
            : RekallAgeVulkanSceneFilter.Linear;
    }

    private static RekallAgeVulkanSceneWrapMode ToWrap(int wrap)
    {
        return wrap switch
        {
            33071 => RekallAgeVulkanSceneWrapMode.ClampToEdge,
            33648 => RekallAgeVulkanSceneWrapMode.MirroredRepeat,
            _ => RekallAgeVulkanSceneWrapMode.Repeat
        };
    }

    private static IReadOnlyList<int> ReadSceneRootNodes(JsonElement root, int nodeCount)
    {
        if (root.TryGetProperty("scenes", out var scenes)
            && scenes.ValueKind == JsonValueKind.Array
            && scenes.GetArrayLength() > 0)
        {
            var sceneIndex = ReadInt(root, "scene", 0);
            if (sceneIndex >= 0
                && sceneIndex < scenes.GetArrayLength()
                && scenes[sceneIndex].TryGetProperty("nodes", out var nodes)
                && nodes.ValueKind == JsonValueKind.Array)
            {
                return nodes.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Number)
                    .Select(item => item.GetInt32())
                    .ToArray();
            }
        }

        return Enumerable.Range(0, nodeCount).ToArray();
    }

    private static Matrix4x4 ReadNodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var matrix) && matrix.ValueKind == JsonValueKind.Array)
        {
            var values = matrix.EnumerateArray().Select(item => item.GetSingle()).ToArray();
            if (values.Length == 16)
            {
                return new Matrix4x4(
                    values[0], values[1], values[2], values[3],
                    values[4], values[5], values[6], values[7],
                    values[8], values[9], values[10], values[11],
                    values[12], values[13], values[14], values[15]);
            }
        }

        var translation = ReadVector3(node, "translation", Vector3.Zero);
        var scale = ReadVector3(node, "scale", Vector3.One);
        var rotation = ReadQuaternion(node, "rotation", Quaternion.Identity);
        return Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation);
    }

    private static Vector3 ReadVector3(JsonElement element, string name, Vector3 fallback)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var values = array.EnumerateArray().Select(item => item.GetSingle()).ToArray();
        return values.Length >= 3 ? new Vector3(values[0], values[1], values[2]) : fallback;
    }

    private static Quaternion ReadQuaternion(JsonElement element, string name, Quaternion fallback)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var values = array.EnumerateArray().Select(item => item.GetSingle()).ToArray();
        return values.Length >= 4 ? new Quaternion(values[0], values[1], values[2], values[3]) : fallback;
    }

    private static GlbPayload ReadGlb(byte[] bytes)
    {
        if (bytes.Length < 20
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) != GlbMagic
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 2)
        {
            throw new InvalidDataException("GLB file has an invalid header.");
        }

        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        if (declaredLength != bytes.Length)
        {
            throw new InvalidDataException("GLB declared length does not match file size.");
        }

        ReadOnlyMemory<byte> json = default;
        ReadOnlyMemory<byte> bin = default;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;
            if (chunkLength < 0 || offset + chunkLength > bytes.Length)
            {
                throw new InvalidDataException("GLB chunk length is invalid.");
            }

            if (chunkType == JsonChunkType)
            {
                json = bytes.AsMemory(offset, chunkLength);
            }
            else if (chunkType == BinChunkType)
            {
                bin = bytes.AsMemory(offset, chunkLength);
            }

            offset += chunkLength;
        }

        if (json.IsEmpty)
        {
            throw new InvalidDataException("GLB file has no JSON chunk.");
        }

        return new GlbPayload(json, bin);
    }

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().ToArray()
            : [];
    }

    private static int ReadInt(JsonElement element, string name, int fallback)
    {
        return element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
            ? result
            : fallback;
    }

    private static float ReadFloat(JsonElement element, string name, float fallback)
    {
        return element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetSingle(out var result)
            ? result
            : fallback;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes, int offset)
    {
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4)));
    }

    private readonly record struct GlbPayload(ReadOnlyMemory<byte> Json, ReadOnlyMemory<byte> Bin);

    private sealed record MaterialInfo(
        Vector4 BaseColor,
        RekallAgeVulkanSceneTexture? BaseColorTexture,
        RekallAgeVulkanSceneTexture? MetallicRoughnessTexture,
        RekallAgeVulkanSceneTexture? NormalTexture,
        RekallAgeVulkanSceneTexture? OcclusionTexture,
        float MetallicFactor,
        float RoughnessFactor,
        float NormalScale,
        float OcclusionStrength)
    {
        public static MaterialInfo Default { get; } = new(
            new Vector4(0.72f, 0.78f, 0.86f, 1),
            null,
            null,
            null,
            null,
            0,
            1,
            1,
            1);
    }

    private sealed record TextureInfo(TextureImage? Image, RekallAgeVulkanSceneSampler Sampler)
    {
        public RekallAgeVulkanSceneTexture? ToSceneTexture(string id)
        {
            return Image is null
                ? null
                : new RekallAgeVulkanSceneTexture(id, Image.Width, Image.Height, Image.Rgba, Sampler);
        }
    }

    private sealed record TextureImage(int Width, int Height, byte[] Rgba);
}
