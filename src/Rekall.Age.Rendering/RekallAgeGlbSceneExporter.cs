using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeGlbSceneExportResult(
    string OutputPath,
    int NodeCount,
    int MeshCount,
    int MaterialCount,
    int ImageCount,
    long BytesWritten,
    IReadOnlyList<string> Warnings);

public sealed record RekallAgeGlbTextureAsset(
    string AssetId,
    string Name,
    string MimeType,
    byte[] Bytes);

public sealed record RekallAgeGlbModelAsset(
    string AssetId,
    string Name,
    string Path,
    byte[] Bytes);

public sealed class RekallAgeGlbSceneExporter
{
    private const uint GlbMagic = 0x46546C67;
    private const uint GlbVersion = 2;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinaryChunkType = 0x004E4942;
    private const int FloatComponentType = 5126;
    private const int UnsignedShortComponentType = 5123;
    private const int UnsignedIntComponentType = 5125;
    private const int ArrayBufferTarget = 34962;
    private const int ElementArrayBufferTarget = 34963;

    public async ValueTask<RekallAgeGlbSceneExportResult> ExportAsync(
        RekallAgeRuntimeViewportFrame frame,
        string outputPath,
        IReadOnlyDictionary<string, RekallAgeGlbTextureAsset>? textureAssets,
        IReadOnlyDictionary<string, RekallAgeGlbModelAsset>? modelAssets,
        CancellationToken cancellationToken)
    {
        var availableModelAssets = modelAssets ?? new Dictionary<string, RekallAgeGlbModelAsset>(StringComparer.Ordinal);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);
        var importedModelRenderables = frame.Renderables
            .Where(renderable => renderable.Kind.Equals("mesh", StringComparison.Ordinal))
            .Where(renderable => renderable.AssetId is not null && availableModelAssets.ContainsKey(renderable.AssetId))
            .ToArray();
        if (meshes.Count == 0 && importedModelRenderables.Length == 0)
        {
            return new RekallAgeGlbSceneExportResult(
                outputPath,
                0,
                0,
                0,
                0,
                0,
                ["Scene does not contain any supported mesh renderables."]);
        }

        var document = BuildDocument(
            frame,
            meshes,
            textureAssets ?? new Dictionary<string, RekallAgeGlbTextureAsset>(StringComparer.Ordinal),
            availableModelAssets,
            importedModelRenderables);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(
            document.Root,
            new JsonSerializerOptions { WriteIndented = false });
        var paddedJsonBytes = Pad(jsonBytes, 0x20);
        var binaryBytes = Pad(document.Binary.ToArray(), 0x00);
        var totalLength = checked(12 + 8 + paddedJsonBytes.Length + 8 + binaryBytes.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await using var stream = File.Create(outputPath);
        await WriteUInt32Async(stream, GlbMagic, cancellationToken);
        await WriteUInt32Async(stream, GlbVersion, cancellationToken);
        await WriteUInt32Async(stream, checked((uint)totalLength), cancellationToken);
        await WriteUInt32Async(stream, checked((uint)paddedJsonBytes.Length), cancellationToken);
        await WriteUInt32Async(stream, JsonChunkType, cancellationToken);
        await stream.WriteAsync(paddedJsonBytes, cancellationToken);
        await WriteUInt32Async(stream, checked((uint)binaryBytes.Length), cancellationToken);
        await WriteUInt32Async(stream, BinaryChunkType, cancellationToken);
        await stream.WriteAsync(binaryBytes, cancellationToken);

        return new RekallAgeGlbSceneExportResult(
            outputPath,
            CountArray(document.Root, "nodes"),
            CountArray(document.Root, "meshes"),
            CountArray(document.Root, "materials"),
            document.ImageCount,
            totalLength,
            document.Warnings);
    }

    private static GlbDocument BuildDocument(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        IReadOnlyDictionary<string, RekallAgeGlbTextureAsset> textureAssets,
        IReadOnlyDictionary<string, RekallAgeGlbModelAsset> modelAssets,
        IReadOnlyList<RekallAgeRuntimeViewportRenderable> importedModelRenderables)
    {
        var binary = new MemoryStream();
        var bufferViews = new JsonArray();
        var accessors = new JsonArray();
        var materials = new JsonArray();
        var gltfMeshes = new JsonArray();
        var nodes = new JsonArray();
        var sceneNodes = new JsonArray();
        var images = new JsonArray();
        var textures = new JsonArray();
        var samplers = new JsonArray();
        var cameras = new JsonArray();
        var skins = new JsonArray();
        var animations = new JsonArray();
        var extensionsUsed = new SortedSet<string>(StringComparer.Ordinal);
        var extensionsRequired = new SortedSet<string>(StringComparer.Ordinal);
        var textureIndexByAssetId = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnings = new List<string>();

        for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
        {
            var mesh = meshes[meshIndex];
            var renderable = frame.Renderables.FirstOrDefault(candidate =>
                candidate.EntityId.Equals(mesh.EntityId, StringComparison.Ordinal));
            if (renderable is null)
            {
                warnings.Add($"Mesh '{mesh.EntityName}' did not have a matching renderable; identity transform and default material were exported.");
            }

            var positionAccessor = AddFloatAccessor(
                binary,
                bufferViews,
                accessors,
                "POSITION",
                mesh.Vertices,
                3,
                vertex => [vertex.X, vertex.Y, vertex.Z],
                includeBounds: true);
            var normalAccessor = AddFloatAccessor(
                binary,
                bufferViews,
                accessors,
                "NORMAL",
                mesh.Vertices,
                3,
                vertex => [vertex.NormalX, vertex.NormalY, vertex.NormalZ],
                includeBounds: false);
            var uvAccessor = AddFloatAccessor(
                binary,
                bufferViews,
                accessors,
                "TEXCOORD_0",
                mesh.Vertices,
                2,
                vertex => [vertex.U, vertex.V],
                includeBounds: false);
            var colorAccessor = AddFloatAccessor(
                binary,
                bufferViews,
                accessors,
                "COLOR_0",
                mesh.Vertices,
                4,
                vertex => [vertex.R, vertex.G, vertex.B, vertex.A],
                includeBounds: false);
            var indexAccessor = AddIndexAccessor(binary, bufferViews, accessors, mesh.Indices);
            var materialIndex = materials.Count;
            materials.Add(CreateMaterial(
                mesh,
                renderable,
                textureAssets,
                binary,
                bufferViews,
                images,
                textures,
                samplers,
                textureIndexByAssetId,
                extensionsUsed,
                extensionsRequired,
                warnings));

            gltfMeshes.Add(new JsonObject
            {
                ["name"] = mesh.EntityName,
                ["primitives"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["attributes"] = new JsonObject
                        {
                            ["POSITION"] = positionAccessor,
                            ["NORMAL"] = normalAccessor,
                            ["TEXCOORD_0"] = uvAccessor,
                            ["COLOR_0"] = colorAccessor
                        },
                        ["indices"] = indexAccessor,
                        ["material"] = materialIndex,
                        ["mode"] = 4
                    }
                }
            });

            nodes.Add(CreateNode(mesh, renderable, meshIndex));
            sceneNodes.Add(meshIndex);
        }

        foreach (var renderable in importedModelRenderables)
        {
            if (renderable.AssetId is null || !modelAssets.TryGetValue(renderable.AssetId, out var asset))
            {
                continue;
            }

            MergeImportedModel(
                asset,
                renderable,
                binary,
                bufferViews,
                accessors,
                materials,
                gltfMeshes,
                nodes,
                sceneNodes,
                images,
                textures,
                samplers,
                cameras,
                skins,
                animations,
                extensionsUsed,
                extensionsRequired,
                warnings);
        }

        var root = new JsonObject
        {
            ["asset"] = new JsonObject
            {
                ["version"] = "2.0",
                ["generator"] = "Rekall AGE"
            },
            ["scene"] = 0,
            ["scenes"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = frame.SceneName,
                    ["nodes"] = sceneNodes
                }
            },
            ["nodes"] = nodes,
            ["meshes"] = gltfMeshes,
            ["materials"] = materials,
            ["buffers"] = new JsonArray
            {
                new JsonObject
                {
                    ["byteLength"] = binary.Length
                }
            },
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors
        };
        if (images.Count > 0)
        {
            root["images"] = images;
            root["textures"] = textures;
            root["samplers"] = samplers;
        }
        if (cameras.Count > 0)
        {
            root["cameras"] = cameras;
        }

        if (skins.Count > 0)
        {
            root["skins"] = skins;
        }

        if (animations.Count > 0)
        {
            root["animations"] = animations;
        }
        if (extensionsUsed.Count > 0)
        {
            root["extensionsUsed"] = ToJsonArray(extensionsUsed);
        }

        if (extensionsRequired.Count > 0)
        {
            root["extensionsRequired"] = ToJsonArray(extensionsRequired);
        }

        return new GlbDocument(root, binary, images.Count, warnings);
    }

    private static void MergeImportedModel(
        RekallAgeGlbModelAsset asset,
        RekallAgeRuntimeViewportRenderable renderable,
        MemoryStream binary,
        JsonArray bufferViews,
        JsonArray accessors,
        JsonArray materials,
        JsonArray gltfMeshes,
        JsonArray nodes,
        JsonArray sceneNodes,
        JsonArray images,
        JsonArray textures,
        JsonArray samplers,
        JsonArray cameras,
        JsonArray skins,
        JsonArray animations,
        SortedSet<string> extensionsUsed,
        SortedSet<string> extensionsRequired,
        List<string> warnings)
    {
        if (!TryReadImportedGlb(asset, warnings, out var imported))
        {
            return;
        }

        var source = imported.Root;
        MergeStringArray(source, "extensionsUsed", extensionsUsed);
        MergeStringArray(source, "extensionsRequired", extensionsRequired);
        var bufferViewOffset = bufferViews.Count;
        var accessorOffset = accessors.Count;
        var materialOffset = materials.Count;
        var meshOffset = gltfMeshes.Count;
        var nodeOffset = nodes.Count;
        var imageOffset = images.Count;
        var textureOffset = textures.Count;
        var samplerOffset = samplers.Count;
        var cameraOffset = cameras.Count;
        var skinOffset = skins.Count;

        var binaryOffset = checked((int)binary.Length);
        if (imported.Binary.Length > 0)
        {
            Align(binary, 4);
            binaryOffset = checked((int)binary.Length);
            binary.Write(imported.Binary);
        }

        foreach (var item in ReadArray(source, "bufferViews"))
        {
            var clone = CloneObject(item);
            clone["buffer"] = 0;
            clone["byteOffset"] = ReadInt(clone, "byteOffset") + binaryOffset;
            bufferViews.Add(clone);
        }

        foreach (var item in ReadArray(source, "accessors"))
        {
            var clone = CloneObject(item);
            OffsetIntProperty(clone, "bufferView", bufferViewOffset);
            accessors.Add(clone);
        }

        foreach (var item in ReadArray(source, "samplers"))
        {
            samplers.Add(CloneObject(item));
        }

        foreach (var item in ReadArray(source, "images"))
        {
            var clone = CloneObject(item);
            OffsetIntProperty(clone, "bufferView", bufferViewOffset);
            images.Add(clone);
        }

        foreach (var item in ReadArray(source, "textures"))
        {
            var clone = CloneObject(item);
            OffsetIntProperty(clone, "source", imageOffset);
            OffsetIntProperty(clone, "sampler", samplerOffset);
            textures.Add(clone);
        }

        foreach (var item in ReadArray(source, "materials"))
        {
            var clone = CloneObject(item);
            OffsetMaterialTextureReferences(clone, textureOffset);
            materials.Add(clone);
        }

        foreach (var item in ReadArray(source, "cameras"))
        {
            cameras.Add(CloneObject(item));
        }

        foreach (var item in ReadArray(source, "meshes"))
        {
            var clone = CloneObject(item);
            OffsetMeshReferences(clone, accessorOffset, materialOffset);
            gltfMeshes.Add(clone);
        }

        foreach (var item in ReadArray(source, "nodes"))
        {
            var clone = CloneObject(item);
            OffsetIntProperty(clone, "mesh", meshOffset);
            OffsetIntProperty(clone, "skin", skinOffset);
            OffsetIntProperty(clone, "camera", cameraOffset);
            OffsetIntArray(clone, "children", nodeOffset);
            nodes.Add(clone);
        }

        foreach (var item in ReadArray(source, "skins"))
        {
            var clone = CloneObject(item);
            OffsetSkinReferences(clone, nodeOffset, accessorOffset);
            skins.Add(clone);
        }

        foreach (var item in ReadArray(source, "animations"))
        {
            var clone = CloneObject(item);
            OffsetAnimationReferences(clone, nodeOffset, accessorOffset);
            animations.Add(clone);
        }

        var wrapperChildren = ResolveImportedSceneRootNodes(source)
            .Select(index => index + nodeOffset)
            .ToArray();
        if (wrapperChildren.Length == 0)
        {
            warnings.Add($"Imported GLB asset '{asset.AssetId}' did not contain scene root nodes.");
            return;
        }

        var wrapperNodeIndex = nodes.Count;
        var wrapper = CreateTransformWrapperNode(renderable, wrapperChildren);
        nodes.Add(wrapper);
        sceneNodes.Add(wrapperNodeIndex);
    }

    private static bool TryReadImportedGlb(
        RekallAgeGlbModelAsset asset,
        List<string> warnings,
        out ImportedGlb imported)
    {
        imported = new ImportedGlb(new JsonObject(), []);
        var bytes = asset.Bytes;
        if (bytes.Length < 20
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) != GlbMagic
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != GlbVersion)
        {
            warnings.Add($"Imported model asset '{asset.AssetId}' is not a GLB 2.0 file.");
            return false;
        }

        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        if (declaredLength != bytes.Length)
        {
            warnings.Add($"Imported model asset '{asset.AssetId}' has an invalid GLB length.");
            return false;
        }

        JsonObject? root = null;
        byte[] binary = [];
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;
            if (offset + chunkLength > bytes.Length)
            {
                warnings.Add($"Imported model asset '{asset.AssetId}' has an invalid GLB chunk.");
                return false;
            }

            if (chunkType == JsonChunkType)
            {
                root = JsonNode.Parse(Encoding.UTF8.GetString(bytes, offset, chunkLength)) as JsonObject;
            }
            else if (chunkType == BinaryChunkType)
            {
                binary = bytes.AsSpan(offset, chunkLength).ToArray();
            }

            offset += chunkLength;
        }

        if (root is null)
        {
            warnings.Add($"Imported model asset '{asset.AssetId}' does not contain a GLB JSON chunk.");
            return false;
        }

        imported = new ImportedGlb(root, binary);
        return true;
    }

    private static JsonObject CreateTransformWrapperNode(
        RekallAgeRuntimeViewportRenderable renderable,
        IReadOnlyList<int> children)
    {
        var childArray = new JsonArray();
        foreach (var child in children)
        {
            childArray.Add(child);
        }

        return new JsonObject
        {
            ["name"] = renderable.EntityName,
            ["children"] = childArray,
            ["translation"] = new JsonArray(renderable.X, renderable.Y, renderable.Z),
            ["rotation"] = ToJsonArray(ToQuaternion(renderable.RotationX, renderable.RotationY, renderable.RotationZ)),
            ["scale"] = new JsonArray(
                Math.Max(0.001, renderable.ScaleX),
                Math.Max(0.001, renderable.ScaleY),
                Math.Max(0.001, renderable.ScaleZ))
        };
    }

    private static IReadOnlyList<int> ResolveImportedSceneRootNodes(JsonObject root)
    {
        var sceneIndex = ReadInt(root, "scene");
        var scenes = ReadArray(root, "scenes");
        if (sceneIndex >= 0 && sceneIndex < scenes.Count)
        {
            var nodes = ReadIntArray(scenes[sceneIndex], "nodes");
            if (nodes.Count > 0)
            {
                return nodes;
            }
        }

        var nodeCount = ReadArray(root, "nodes").Count;
        if (nodeCount == 0)
        {
            return Array.Empty<int>();
        }

        var childNodes = new HashSet<int>();
        foreach (var node in ReadArray(root, "nodes"))
        {
            foreach (var child in ReadIntArray(node, "children"))
            {
                childNodes.Add(child);
            }
        }

        return Enumerable.Range(0, nodeCount)
            .Where(index => !childNodes.Contains(index))
            .ToArray();
    }

    private static void OffsetMeshReferences(JsonObject mesh, int accessorOffset, int materialOffset)
    {
        if (!mesh.TryGetPropertyValue("primitives", out var primitiveNode) || primitiveNode is not JsonArray primitives)
        {
            return;
        }

        foreach (var primitive in primitives.OfType<JsonObject>())
        {
            OffsetIntProperty(primitive, "indices", accessorOffset);
            OffsetIntProperty(primitive, "material", materialOffset);
            if (primitive.TryGetPropertyValue("attributes", out var attributesNode) && attributesNode is JsonObject attributes)
            {
                OffsetAccessorMap(attributes, accessorOffset);
            }

            if (primitive.TryGetPropertyValue("targets", out var targetsNode) && targetsNode is JsonArray targets)
            {
                foreach (var target in targets.OfType<JsonObject>())
                {
                    OffsetAccessorMap(target, accessorOffset);
                }
            }
        }
    }

    private static void OffsetAccessorMap(JsonObject attributes, int accessorOffset)
    {
        foreach (var property in attributes.ToArray())
        {
            if (property.Value is JsonValue value && value.TryGetValue<int>(out var index))
            {
                attributes[property.Key] = index + accessorOffset;
            }
        }
    }

    private static void OffsetMaterialTextureReferences(JsonObject material, int textureOffset)
    {
        if (textureOffset == 0)
        {
            return;
        }

        if (material.TryGetPropertyValue("pbrMetallicRoughness", out var pbrNode) && pbrNode is JsonObject pbr)
        {
            OffsetTextureInfo(pbr, "baseColorTexture", textureOffset);
            OffsetTextureInfo(pbr, "metallicRoughnessTexture", textureOffset);
        }

        OffsetTextureInfo(material, "normalTexture", textureOffset);
        OffsetTextureInfo(material, "occlusionTexture", textureOffset);
        OffsetTextureInfo(material, "emissiveTexture", textureOffset);
        if (material.TryGetPropertyValue("extensions", out var extensionsNode) && extensionsNode is JsonObject extensions)
        {
            OffsetExtensionTextureReferences(extensions, textureOffset);
        }
    }

    private static void OffsetExtensionTextureReferences(JsonNode node, int textureOffset)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Key.EndsWith("Texture", StringComparison.Ordinal)
                    && property.Value is JsonObject textureInfo)
                {
                    OffsetIntProperty(textureInfo, "index", textureOffset);
                }

                if (property.Value is not null)
                {
                    OffsetExtensionTextureReferences(property.Value, textureOffset);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                {
                    OffsetExtensionTextureReferences(child, textureOffset);
                }
            }
        }
    }

    private static void OffsetSkinReferences(JsonObject skin, int nodeOffset, int accessorOffset)
    {
        OffsetIntProperty(skin, "skeleton", nodeOffset);
        OffsetIntProperty(skin, "inverseBindMatrices", accessorOffset);
        OffsetIntArray(skin, "joints", nodeOffset);
    }

    private static void OffsetAnimationReferences(JsonObject animation, int nodeOffset, int accessorOffset)
    {
        if (animation.TryGetPropertyValue("samplers", out var samplerNode) && samplerNode is JsonArray animationSamplers)
        {
            foreach (var sampler in animationSamplers.OfType<JsonObject>())
            {
                OffsetIntProperty(sampler, "input", accessorOffset);
                OffsetIntProperty(sampler, "output", accessorOffset);
            }
        }

        if (animation.TryGetPropertyValue("channels", out var channelNode) && channelNode is JsonArray channels)
        {
            foreach (var channel in channels.OfType<JsonObject>())
            {
                if (channel.TryGetPropertyValue("target", out var targetNode) && targetNode is JsonObject target)
                {
                    OffsetIntProperty(target, "node", nodeOffset);
                }
            }
        }
    }

    private static void OffsetTextureInfo(JsonObject parent, string propertyName, int textureOffset)
    {
        if (parent.TryGetPropertyValue(propertyName, out var node) && node is JsonObject textureInfo)
        {
            OffsetIntProperty(textureInfo, "index", textureOffset);
        }
    }

    private static IReadOnlyList<JsonObject> ReadArray(JsonObject root, string propertyName)
    {
        return root.TryGetPropertyValue(propertyName, out var node) && node is JsonArray array
            ? array.OfType<JsonObject>().ToArray()
            : Array.Empty<JsonObject>();
    }

    private static IReadOnlyList<int> ReadIntArray(JsonObject root, string propertyName)
    {
        if (!root.TryGetPropertyValue(propertyName, out var node) || node is not JsonArray array)
        {
            return Array.Empty<int>();
        }

        return array
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<int>(out var integer) ? integer : -1)
            .Where(value => value >= 0)
            .ToArray();
    }

    private static JsonObject CloneObject(JsonObject value)
    {
        return value.DeepClone().AsObject();
    }

    private static void OffsetIntProperty(JsonObject value, string propertyName, int offset)
    {
        if (offset == 0)
        {
            return;
        }

        if (value.TryGetPropertyValue(propertyName, out var node)
            && node is JsonValue jsonValue
            && jsonValue.TryGetValue<int>(out var integer))
        {
            value[propertyName] = integer + offset;
        }
    }

    private static void OffsetIntArray(JsonObject value, string propertyName, int offset)
    {
        if (offset == 0
            || !value.TryGetPropertyValue(propertyName, out var node)
            || node is not JsonArray array)
        {
            return;
        }

        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var integer))
            {
                array[i] = integer + offset;
            }
        }
    }

    private static int ReadInt(JsonObject value, string propertyName)
    {
        return value.TryGetPropertyValue(propertyName, out var node)
            && node is JsonValue jsonValue
            && jsonValue.TryGetValue<int>(out var integer)
            ? integer
            : 0;
    }

    private static int CountArray(JsonObject root, string propertyName)
    {
        return root.TryGetPropertyValue(propertyName, out var node) && node is JsonArray array
            ? array.Count
            : 0;
    }

    private static int CountOptionalArray(JsonObject root, string propertyName)
    {
        return CountArray(root, propertyName);
    }

    private static int AddFloatAccessor(
        MemoryStream binary,
        JsonArray bufferViews,
        JsonArray accessors,
        string name,
        IReadOnlyList<RekallAgeVulkanSceneVertex> vertices,
        int componentCount,
        Func<RekallAgeVulkanSceneVertex, float[]> select,
        bool includeBounds)
    {
        Align(binary, 4);
        var byteOffset = checked((int)binary.Length);
        var min = Enumerable.Repeat(float.PositiveInfinity, componentCount).ToArray();
        var max = Enumerable.Repeat(float.NegativeInfinity, componentCount).ToArray();
        foreach (var vertex in vertices)
        {
            var values = select(vertex);
            for (var i = 0; i < componentCount; i++)
            {
                WriteSingle(binary, values[i]);
                min[i] = MathF.Min(min[i], values[i]);
                max[i] = MathF.Max(max[i], values[i]);
            }
        }

        var byteLength = checked((int)binary.Length - byteOffset);
        var bufferViewIndex = AddBufferView(bufferViews, byteOffset, byteLength, ArrayBufferTarget);
        var accessor = new JsonObject
        {
            ["bufferView"] = bufferViewIndex,
            ["byteOffset"] = 0,
            ["componentType"] = FloatComponentType,
            ["count"] = vertices.Count,
            ["type"] = ToAccessorType(componentCount),
            ["name"] = name
        };
        if (includeBounds)
        {
            accessor["min"] = ToJsonArray(min);
            accessor["max"] = ToJsonArray(max);
        }

        var accessorIndex = accessors.Count;
        accessors.Add(accessor);
        return accessorIndex;
    }

    private static int AddIndexAccessor(
        MemoryStream binary,
        JsonArray bufferViews,
        JsonArray accessors,
        IReadOnlyList<uint> indices)
    {
        Align(binary, 4);
        var byteOffset = checked((int)binary.Length);
        var useUInt32 = indices.Any(index => index > ushort.MaxValue);
        foreach (var index in indices)
        {
            if (useUInt32)
            {
                WriteUInt32(binary, index);
            }
            else
            {
                WriteUInt16(binary, checked((ushort)index));
            }
        }

        var byteLength = checked((int)binary.Length - byteOffset);
        var bufferViewIndex = AddBufferView(bufferViews, byteOffset, byteLength, ElementArrayBufferTarget);
        var accessor = new JsonObject
        {
            ["bufferView"] = bufferViewIndex,
            ["byteOffset"] = 0,
            ["componentType"] = useUInt32 ? UnsignedIntComponentType : UnsignedShortComponentType,
            ["count"] = indices.Count,
            ["type"] = "SCALAR",
            ["min"] = new JsonArray(indices.Min(index => checked((long)index))),
            ["max"] = new JsonArray(indices.Max(index => checked((long)index))),
            ["name"] = "indices"
        };

        var accessorIndex = accessors.Count;
        accessors.Add(accessor);
        return accessorIndex;
    }

    private static JsonObject CreateMaterial(
        RekallAgeVulkanSceneMesh mesh,
        RekallAgeRuntimeViewportRenderable? renderable,
        IReadOnlyDictionary<string, RekallAgeGlbTextureAsset> textureAssets,
        MemoryStream binary,
        JsonArray bufferViews,
        JsonArray images,
        JsonArray textures,
        JsonArray samplers,
        Dictionary<string, int> textureIndexByAssetId,
        SortedSet<string> extensionsUsed,
        SortedSet<string> extensionsRequired,
        List<string> warnings)
    {
        var color = ParseColor(renderable?.MaterialColor);
        var pbr = new JsonObject
        {
            ["baseColorFactor"] = new JsonArray(color.R, color.G, color.B, color.A),
            ["metallicFactor"] = 0,
            ["roughnessFactor"] = 0.7
        };
        var textureIndex = ResolveTextureIndex(
            renderable,
            textureAssets,
            binary,
            bufferViews,
            images,
            textures,
            samplers,
            textureIndexByAssetId,
            extensionsUsed,
            extensionsRequired,
            warnings);
        if (textureIndex is { } value)
        {
            pbr["baseColorTexture"] = new JsonObject
            {
                ["index"] = value,
                ["texCoord"] = 0
            };
        }

        return new JsonObject
        {
            ["name"] = $"{mesh.EntityName} Material",
            ["doubleSided"] = true,
            ["pbrMetallicRoughness"] = pbr
        };
    }

    private static int? ResolveTextureIndex(
        RekallAgeRuntimeViewportRenderable? renderable,
        IReadOnlyDictionary<string, RekallAgeGlbTextureAsset> textureAssets,
        MemoryStream binary,
        JsonArray bufferViews,
        JsonArray images,
        JsonArray textures,
        JsonArray samplers,
        Dictionary<string, int> textureIndexByAssetId,
        SortedSet<string> extensionsUsed,
        SortedSet<string> extensionsRequired,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(renderable?.TextureAssetId))
        {
            return null;
        }

        var assetId = renderable.TextureAssetId.Trim();
        if (textureIndexByAssetId.TryGetValue(assetId, out var existing))
        {
            return existing;
        }

        if (!textureAssets.TryGetValue(assetId, out var asset))
        {
            warnings.Add($"Texture asset '{assetId}' referenced by '{renderable.EntityName}' was not found or is not a PNG/JPEG/KTX2 image.");
            return null;
        }

        if (samplers.Count == 0)
        {
            samplers.Add(new JsonObject
            {
                ["magFilter"] = 9729,
                ["minFilter"] = 9987,
                ["wrapS"] = 10497,
                ["wrapT"] = 10497
            });
        }

        Align(binary, 4);
        var byteOffset = checked((int)binary.Length);
        binary.Write(asset.Bytes);
        var byteLength = checked((int)binary.Length - byteOffset);
        var bufferViewIndex = AddBufferView(bufferViews, byteOffset, byteLength, null);
        var imageIndex = images.Count;
        images.Add(new JsonObject
        {
            ["name"] = asset.Name,
            ["bufferView"] = bufferViewIndex,
            ["mimeType"] = asset.MimeType
        });
        var textureIndex = textures.Count;
        var texture = new JsonObject
        {
            ["name"] = asset.Name,
            ["sampler"] = 0
        };
        if (asset.MimeType.Equals("image/ktx2", StringComparison.Ordinal))
        {
            texture["extensions"] = new JsonObject
            {
                ["KHR_texture_basisu"] = new JsonObject
                {
                    ["source"] = imageIndex
                }
            };
            extensionsUsed.Add("KHR_texture_basisu");
            extensionsRequired.Add("KHR_texture_basisu");
        }
        else
        {
            texture["source"] = imageIndex;
        }

        textures.Add(texture);
        textureIndexByAssetId[assetId] = textureIndex;
        return textureIndex;
    }

    private static JsonObject CreateNode(
        RekallAgeVulkanSceneMesh mesh,
        RekallAgeRuntimeViewportRenderable? renderable,
        int meshIndex)
    {
        return new JsonObject
        {
            ["name"] = mesh.EntityName,
            ["mesh"] = meshIndex,
            ["translation"] = new JsonArray(
                renderable?.X ?? 0,
                renderable?.Y ?? 0,
                renderable?.Z ?? 0),
            ["rotation"] = ToJsonArray(ToQuaternion(
                renderable?.RotationX ?? 0,
                renderable?.RotationY ?? 0,
                renderable?.RotationZ ?? 0)),
            ["scale"] = new JsonArray(
                Math.Max(0.001, renderable?.ScaleX ?? 1),
                Math.Max(0.001, renderable?.ScaleY ?? 1),
                Math.Max(0.001, renderable?.ScaleZ ?? 1))
        };
    }

    private static int AddBufferView(JsonArray bufferViews, int byteOffset, int byteLength, int? target)
    {
        var index = bufferViews.Count;
        var bufferView = new JsonObject
        {
            ["buffer"] = 0,
            ["byteOffset"] = byteOffset,
            ["byteLength"] = byteLength
        };
        if (target is { } value)
        {
            bufferView["target"] = value;
        }

        bufferViews.Add(bufferView);
        return index;
    }

    private static string ToAccessorType(int componentCount)
    {
        return componentCount switch
        {
            1 => "SCALAR",
            2 => "VEC2",
            3 => "VEC3",
            4 => "VEC4",
            _ => throw new ArgumentOutOfRangeException(nameof(componentCount), componentCount, "Unsupported accessor component count.")
        };
    }

    private static JsonArray ToJsonArray(IReadOnlyList<float> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static void MergeStringArray(JsonObject root, string propertyName, ISet<string> target)
    {
        if (!root.TryGetPropertyValue(propertyName, out var node) || node is not JsonArray array)
        {
            return;
        }

        foreach (var value in array.OfType<JsonValue>())
        {
            if (value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                target.Add(text);
            }
        }
    }

    private static JsonArray ToJsonArray(Quaternion quaternion)
    {
        return new JsonArray(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
    }

    private static Quaternion ToQuaternion(double degreesX, double degreesY, double degreesZ)
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, ToRadians(degreesX))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, ToRadians(degreesY))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, ToRadians(degreesZ));
        return Quaternion.Normalize(rotation);
    }

    private static float ToRadians(double degrees)
    {
        return MathF.PI / 180f * (float)degrees;
    }

    private static SceneColor ParseColor(string? color)
    {
        if (color is { Length: 7 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new SceneColor(r / 255f, g / 255f, b / 255f, 1);
        }

        return new SceneColor(0.35f, 0.58f, 0.85f, 1);
    }

    private static void Align(Stream stream, int alignment)
    {
        while (stream.Length % alignment != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static byte[] Pad(byte[] bytes, byte value)
    {
        var padding = (4 - bytes.Length % 4) % 4;
        if (padding == 0)
        {
            return bytes;
        }

        var padded = new byte[bytes.Length + padding];
        bytes.CopyTo(padded, 0);
        for (var i = bytes.Length; i < padded.Length; i++)
        {
            padded[i] = value;
        }

        return padded;
    }

    private static void WriteSingle(Stream stream, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static async ValueTask WriteUInt32Async(Stream stream, uint value, CancellationToken cancellationToken)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private sealed record GlbDocument(
        JsonObject Root,
        MemoryStream Binary,
        int ImageCount,
        IReadOnlyList<string> Warnings);

    private sealed record ImportedGlb(JsonObject Root, byte[] Binary);

    private readonly record struct SceneColor(float R, float G, float B, float A);
}
