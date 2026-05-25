using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Assets.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class ExportSceneGlbCommandTests
{
    [Fact]
    public async Task ExportSceneGlbCommandWritesRenderableAuthoredMeshWithMaterialAndTransform()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]), CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("export glb"), CancellationToken.None);
        var sourceTexture = Path.Combine(root, "source-texture.png");
        await RekallAgePngWriter.WriteRgbaAsync(
            sourceTexture,
            1,
            1,
            [51, 102, 204, 255],
            CancellationToken.None);
        var texture = await new ImportAssetCommand().ExecuteAsync(
            new ImportAssetRequest(root, sourceTexture, "texture", "Paint"),
            context);
        Assert.True(texture.Ok, texture.Summary);

        await new CreateGeometryMeshCommand().ExecuteAsync(
            new CreateGeometryMeshRequest(
                root,
                "Main",
                "Agent Triangle",
                [
                    new CreateGeometryMeshVertex(0, 0, 0, U: 0, V: 1),
                    new CreateGeometryMeshVertex(1, 0, 0, R: 1, G: 0, B: 0, U: 1, V: 1),
                    new CreateGeometryMeshVertex(0, 1, 0, R: 0, G: 1, B: 0, U: 0, V: 0)
                ],
                [0, 1, 2],
                X: 1,
                Y: 2,
                Z: 3,
                Pitch: 10,
                Yaw: 20,
                Roll: 30,
                ScaleX: 2,
                ScaleY: 3,
                ScaleZ: 4,
                Color: "#3366cc",
                TextureAssetId: texture.Value.Asset.Id),
            context);
        var outputPath = Path.Combine(root, "Artifacts", "Exports", "triangle.glb");
        var command = new ExportSceneGlbCommand();

        var result = await command.ExecuteAsync(
            new ExportSceneGlbRequest(root, "Main", outputPath),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(File.Exists(outputPath));
        Assert.Equal(1, result.Value.NodeCount);
        Assert.Equal(1, result.Value.MeshCount);
        Assert.Equal(1, result.Value.MaterialCount);
        Assert.Equal(1, result.Value.ImageCount);
        Assert.Contains(outputPath, context.Transaction.ChangedResources);

        var metadata = await RekallAgeGlbMetadataReader.ReadAsync(outputPath, CancellationToken.None);
        Assert.NotNull(metadata);
        Assert.Equal(1, metadata.SceneCount);
        Assert.Equal(1, metadata.NodeCount);
        Assert.Equal(1, metadata.MeshCount);
        Assert.Equal(1, metadata.MaterialCount);
        Assert.Equal(1, metadata.ImageCount);

        using var json = await ReadGlbJsonAsync(outputPath);
        var rootElement = json.RootElement;
        Assert.Equal("2.0", rootElement.GetProperty("asset").GetProperty("version").GetString());
        var primitive = rootElement.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var attributes = primitive.GetProperty("attributes");
        Assert.True(attributes.TryGetProperty("POSITION", out _));
        Assert.True(attributes.TryGetProperty("NORMAL", out _));
        Assert.True(attributes.TryGetProperty("TEXCOORD_0", out _));
        Assert.True(attributes.TryGetProperty("COLOR_0", out _));
        Assert.True(primitive.TryGetProperty("indices", out _));
        Assert.Equal(0, primitive.GetProperty("material").GetInt32());

        var material = rootElement.GetProperty("materials")[0];
        var pbr = material.GetProperty("pbrMetallicRoughness");
        var baseColor = pbr.GetProperty("baseColorFactor");
        Assert.Equal(0.2, baseColor[0].GetDouble(), 3);
        Assert.Equal(0.4, baseColor[1].GetDouble(), 3);
        Assert.Equal(0.8, baseColor[2].GetDouble(), 3);
        Assert.Equal(1.0, baseColor[3].GetDouble(), 3);
        Assert.Equal(0, pbr.GetProperty("baseColorTexture").GetProperty("index").GetInt32());
        Assert.Equal("image/png", rootElement.GetProperty("images")[0].GetProperty("mimeType").GetString());
        Assert.Equal(0, rootElement.GetProperty("textures")[0].GetProperty("source").GetInt32());

        var node = rootElement.GetProperty("nodes")[0];
        Assert.Equal("Agent Triangle", node.GetProperty("name").GetString());
        Assert.Equal(0, node.GetProperty("mesh").GetInt32());
        Assert.Equal(1, node.GetProperty("translation")[0].GetDouble(), 3);
        Assert.Equal(2, node.GetProperty("translation")[1].GetDouble(), 3);
        Assert.Equal(3, node.GetProperty("translation")[2].GetDouble(), 3);
        Assert.Equal(2, node.GetProperty("scale")[0].GetDouble(), 3);
        Assert.Equal(3, node.GetProperty("scale")[1].GetDouble(), 3);
        Assert.Equal(4, node.GetProperty("scale")[2].GetDouble(), 3);
        Assert.Equal(4, node.GetProperty("rotation").GetArrayLength());
    }

    [Fact]
    public async Task ExportSceneGlbCommandPreservesImportedGlbBinaryBuffersAfterGeneratedGeometry()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("export imported binary glb"), CancellationToken.None);
        var sourceModel = Path.Combine(root, "binary-robot.glb");
        var importedPayload = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        await File.WriteAllBytesAsync(sourceModel, CreateImportedModelGlbWithBinary("BinaryRobotMesh", importedPayload), CancellationToken.None);
        var imported = await new ImportAssetCommand().ExecuteAsync(
            new ImportAssetRequest(root, sourceModel, "model", "Binary Robot"),
            context);
        Assert.True(imported.Ok, imported.Summary);

        var generated = RekallAgeEntityDocument.Create("Generated Triangle", ["geometry", "mesh"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.GeometryMesh",
                new JsonObject
                {
                    ["color"] = "#44ccff",
                    ["vertices"] = new JsonArray
                    {
                        new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 },
                        new JsonObject { ["x"] = 1, ["y"] = 0, ["z"] = 0 },
                        new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0 }
                    },
                    ["indices"] = new JsonArray(0, 1, 2)
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject { ["mesh"] = "rekall.geometry.mesh" }));
        var importedEntity = RekallAgeEntityDocument.Create("Imported Binary Robot", ["model"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject { ["mesh"] = imported.Value.Asset.Id }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(generated)
                .AddEntity(importedEntity),
            CancellationToken.None);

        var outputPath = Path.Combine(root, "Artifacts", "Exports", "binary-import.glb");
        var result = await new ExportSceneGlbCommand().ExecuteAsync(
            new ExportSceneGlbRequest(root, "Main", outputPath),
            context);

        Assert.True(result.Ok, result.Summary);
        using var json = await ReadGlbJsonAsync(outputPath);
        var rootElement = json.RootElement;
        var importedMeshIndex = -1;
        for (var i = 0; i < rootElement.GetProperty("meshes").GetArrayLength(); i++)
        {
            if (rootElement.GetProperty("meshes")[i].GetProperty("name").GetString() == "BinaryRobotMesh")
            {
                importedMeshIndex = i;
                break;
            }
        }

        Assert.True(importedMeshIndex >= 0);
        var accessorIndex = rootElement.GetProperty("meshes")[importedMeshIndex]
            .GetProperty("primitives")[0]
            .GetProperty("attributes")
            .GetProperty("POSITION")
            .GetInt32();
        var accessor = rootElement.GetProperty("accessors")[accessorIndex];
        var bufferView = rootElement.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var byteOffset = bufferView.GetProperty("byteOffset").GetInt32()
            + (accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0);
        var binary = await ReadGlbBinaryAsync(outputPath);

        Assert.True(byteOffset > importedPayload.Length);
        Assert.Equal(importedPayload, binary.Skip(byteOffset).Take(importedPayload.Length).ToArray());
    }

    [Fact]
    public async Task ExportSceneGlbCommandReindexesImportedMaterialExtensionTextures()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("export extension textures"), CancellationToken.None);
        var sourceTexture = Path.Combine(root, "generated-texture.png");
        await RekallAgePngWriter.WriteRgbaAsync(sourceTexture, 1, 1, [255, 255, 255, 255], CancellationToken.None);
        var generatedTexture = await new ImportAssetCommand().ExecuteAsync(
            new ImportAssetRequest(root, sourceTexture, "texture", "Generated Paint"),
            context);
        Assert.True(generatedTexture.Ok, generatedTexture.Summary);

        var sourceModel = Path.Combine(root, "clearcoat.glb");
        await File.WriteAllBytesAsync(sourceModel, CreateImportedModelGlbWithMaterialExtensionTexture("ClearcoatMesh"), CancellationToken.None);
        var imported = await new ImportAssetCommand().ExecuteAsync(
            new ImportAssetRequest(root, sourceModel, "model", "Clearcoat Model"),
            context);
        Assert.True(imported.Ok, imported.Summary);

        var generated = RekallAgeEntityDocument.Create("Textured Triangle", ["geometry", "mesh"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.GeometryMesh",
                new JsonObject
                {
                    ["color"] = "#ffffff",
                    ["textureAssetId"] = generatedTexture.Value.Asset.Id,
                    ["vertices"] = new JsonArray
                    {
                        new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 },
                        new JsonObject { ["x"] = 1, ["y"] = 0, ["z"] = 0 },
                        new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0 }
                    },
                    ["indices"] = new JsonArray(0, 1, 2)
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject { ["mesh"] = "rekall.geometry.mesh" }));
        var importedEntity = RekallAgeEntityDocument.Create("Imported Clearcoat", ["model"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject { ["mesh"] = imported.Value.Asset.Id }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(generated)
                .AddEntity(importedEntity),
            CancellationToken.None);

        var outputPath = Path.Combine(root, "Artifacts", "Exports", "extension-textures.glb");
        var result = await new ExportSceneGlbCommand().ExecuteAsync(
            new ExportSceneGlbRequest(root, "Main", outputPath),
            context);

        Assert.True(result.Ok, result.Summary);
        using var json = await ReadGlbJsonAsync(outputPath);
        var rootElement = json.RootElement;
        Assert.Contains("KHR_materials_clearcoat", rootElement.GetProperty("extensionsUsed").EnumerateArray().Select(item => item.GetString()));
        var importedMaterial = rootElement.GetProperty("materials").EnumerateArray()
            .Single(material => material.GetProperty("name").GetString() == "Clearcoat");
        var extensionTextureIndex = importedMaterial
            .GetProperty("extensions")
            .GetProperty("KHR_materials_clearcoat")
            .GetProperty("clearcoatTexture")
            .GetProperty("index")
            .GetInt32();
        Assert.Equal(1, extensionTextureIndex);
    }

    [Fact]
    public async Task ExportSceneGlbCommandWritesKtx2TexturesWithBasisuExtension()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("export ktx2"), CancellationToken.None);
        var sourceTexture = Path.Combine(root, "planet.ktx2");
        await File.WriteAllBytesAsync(sourceTexture, CreateKtx2Header(146, 1024, 512, 7, 2), CancellationToken.None);
        var texture = await new ImportAssetCommand().ExecuteAsync(
            new ImportAssetRequest(root, sourceTexture, "texture", "Planet KTX2"),
            context);
        Assert.True(texture.Ok, texture.Summary);

        var planet = RekallAgeEntityDocument.Create("Exported Planet", ["planet"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.PlanetRenderer",
                new JsonObject
                {
                    ["radius"] = 1,
                    ["surfaceTexture"] = texture.Value.Asset.Id,
                    ["color"] = "#ffffff"
                }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(planet),
            CancellationToken.None);
        var outputPath = Path.Combine(root, "Artifacts", "Exports", "planet.glb");

        var result = await new ExportSceneGlbCommand().ExecuteAsync(
            new ExportSceneGlbRequest(root, "Main", outputPath),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(1, result.Value.ImageCount);
        using var json = await ReadGlbJsonAsync(outputPath);
        var rootElement = json.RootElement;
        Assert.Contains("KHR_texture_basisu", rootElement.GetProperty("extensionsUsed").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("KHR_texture_basisu", rootElement.GetProperty("extensionsRequired").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("image/ktx2", rootElement.GetProperty("images")[0].GetProperty("mimeType").GetString());
        Assert.False(rootElement.GetProperty("textures")[0].TryGetProperty("source", out _));
        Assert.Equal(
            0,
            rootElement.GetProperty("textures")[0]
                .GetProperty("extensions")
                .GetProperty("KHR_texture_basisu")
                .GetProperty("source")
                .GetInt32());
        Assert.Equal(
            0,
            rootElement.GetProperty("materials")[0]
                .GetProperty("pbrMetallicRoughness")
                .GetProperty("baseColorTexture")
                .GetProperty("index")
                .GetInt32());
    }

    [Fact]
    public async Task ExportSceneGlbCommandMergesImportedGlbModelWithEntityTransform()
    {
        var root = TestPaths.CreateTempDirectory();
        var sourceModel = Path.Combine(root, "robot.glb");
        await File.WriteAllBytesAsync(sourceModel, CreateImportedModelGlb("RobotMesh", "Paint", "PaintTexture"), CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("export imported glb"), CancellationToken.None);
        var imported = await new ImportAssetCommand().ExecuteAsync(
            new ImportAssetRequest(root, sourceModel, "model", "Robot"),
            context);
        Assert.True(imported.Ok, imported.Summary);

        var entity = RekallAgeEntityDocument.Create("Placed Robot", ["model"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject
                {
                    ["x"] = 4,
                    ["y"] = 5,
                    ["z"] = 6,
                    ["pitch"] = 15,
                    ["yaw"] = 25,
                    ["roll"] = 35,
                    ["scaleX"] = 2,
                    ["scaleY"] = 2,
                    ["scaleZ"] = 2
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject { ["mesh"] = imported.Value.Asset.Id }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(entity),
            CancellationToken.None);
        var outputPath = Path.Combine(root, "Artifacts", "Exports", "imported.glb");

        var result = await new ExportSceneGlbCommand().ExecuteAsync(
            new ExportSceneGlbRequest(root, "Main", outputPath),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(File.Exists(outputPath));
        Assert.Equal(2, result.Value.NodeCount);
        Assert.Equal(1, result.Value.MeshCount);
        Assert.Equal(1, result.Value.MaterialCount);
        Assert.Equal(1, result.Value.ImageCount);

        var metadata = await RekallAgeGlbMetadataReader.ReadAsync(outputPath, CancellationToken.None);
        Assert.NotNull(metadata);
        Assert.Equal(2, metadata.NodeCount);
        Assert.Equal(1, metadata.MeshCount);
        Assert.Equal(1, metadata.MaterialCount);
        Assert.Equal(1, metadata.ImageCount);
        Assert.Equal(1, metadata.AnimationCount);
        Assert.Contains(metadata.Meshes, mesh => mesh.Name == "RobotMesh");
        Assert.Contains(metadata.Materials, material => material.Name == "Paint");
        Assert.Contains(metadata.Images, image => image.Name == "PaintTexture");

        using var json = await ReadGlbJsonAsync(outputPath);
        var rootElement = json.RootElement;
        var wrapper = rootElement.GetProperty("nodes")[1];
        Assert.Equal("Placed Robot", wrapper.GetProperty("name").GetString());
        Assert.Equal(4, wrapper.GetProperty("translation")[0].GetDouble(), 3);
        Assert.Equal(5, wrapper.GetProperty("translation")[1].GetDouble(), 3);
        Assert.Equal(6, wrapper.GetProperty("translation")[2].GetDouble(), 3);
        Assert.Equal(2, wrapper.GetProperty("scale")[0].GetDouble(), 3);
        Assert.Equal(0, wrapper.GetProperty("children")[0].GetInt32());
        Assert.Equal(1, rootElement.GetProperty("scenes")[0].GetProperty("nodes")[0].GetInt32());
    }

    [Fact]
    public async Task ExportSceneGlbCommandFailsWhenSceneHasNoExportableMeshes()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);
        var outputPath = Path.Combine(root, "empty.glb");

        var result = await new ExportSceneGlbCommand().ExecuteAsync(
            new ExportSceneGlbRequest(root, "Main", outputPath),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("empty export"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.False(File.Exists(outputPath));
        Assert.Contains(result.Errors, error => error.Code == "REKALL_GLTF_EXPORT_NO_MESHES");
    }

    private static async Task<JsonDocument> ReadGlbJsonAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
        return JsonDocument.Parse(bytes.AsMemory(20, checked((int)jsonLength)));
    }

    private static async Task<byte[]> ReadGlbBinaryAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;
            if (chunkType == 0x004E4942)
            {
                return bytes.AsSpan(offset, chunkLength).ToArray();
            }

            offset += chunkLength;
        }

        return [];
    }

    private static byte[] CreateImportedModelGlb(string meshName, string materialName, string imageName)
    {
        var json = $$"""
        {
          "asset": { "version": "2.0", "generator": "Rekall AGE Test" },
          "scene": 0,
          "scenes": [{ "name": "ImportedScene", "nodes": [0] }],
          "nodes": [{ "name": "ImportedRoot", "mesh": 0 }],
          "meshes": [
            { "name": "{{meshName}}", "primitives": [{ "attributes": { "POSITION": 0 }, "material": 0 }] }
          ],
          "materials": [
            {
              "name": "{{materialName}}",
              "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } }
            }
          ],
          "images": [{ "name": "{{imageName}}", "mimeType": "image/png" }],
          "textures": [{ "source": 0 }],
          "animations": [{ "name": "Idle", "channels": [], "samplers": [] }]
        }
        """;
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var paddedJsonLength = (jsonBytes.Length + 3) / 4 * 4;
        var bytes = new byte[12 + 8 + paddedJsonLength];
        WriteUInt32(bytes, 0, 0x46546C67);
        WriteUInt32(bytes, 4, 2);
        WriteUInt32(bytes, 8, (uint)bytes.Length);
        WriteUInt32(bytes, 12, (uint)paddedJsonLength);
        WriteUInt32(bytes, 16, 0x4E4F534A);
        Array.Copy(jsonBytes, 0, bytes, 20, jsonBytes.Length);
        Array.Fill<byte>(bytes, 0x20, 20 + jsonBytes.Length, paddedJsonLength - jsonBytes.Length);
        return bytes;
    }

    private static byte[] CreateKtx2Header(uint vkFormat, uint width, uint height, uint mipLevels, uint supercompression)
    {
        var bytes = new byte[80];
        var identifier = new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(identifier, bytes, identifier.Length);
        WriteUInt32(bytes, 12, vkFormat);
        WriteUInt32(bytes, 16, 1);
        WriteUInt32(bytes, 20, width);
        WriteUInt32(bytes, 24, height);
        WriteUInt32(bytes, 36, 1);
        WriteUInt32(bytes, 40, mipLevels);
        WriteUInt32(bytes, 44, supercompression);
        return bytes;
    }

    private static byte[] CreateImportedModelGlbWithBinary(string meshName, byte[] binaryPayload)
    {
        var json = $$"""
        {
          "asset": { "version": "2.0", "generator": "Rekall AGE Test" },
          "scene": 0,
          "scenes": [{ "name": "ImportedScene", "nodes": [0] }],
          "nodes": [{ "name": "ImportedRoot", "mesh": 0 }],
          "meshes": [
            { "name": "{{meshName}}", "primitives": [{ "attributes": { "POSITION": 0 } }] }
          ],
          "buffers": [{ "byteLength": {{binaryPayload.Length}} }],
          "bufferViews": [{ "buffer": 0, "byteOffset": 0, "byteLength": {{binaryPayload.Length}}, "target": 34962 }],
          "accessors": [{ "bufferView": 0, "componentType": 5126, "count": 1, "type": "VEC3" }]
        }
        """;
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var paddedJsonLength = (jsonBytes.Length + 3) / 4 * 4;
        var paddedBinaryLength = (binaryPayload.Length + 3) / 4 * 4;
        var bytes = new byte[12 + 8 + paddedJsonLength + 8 + paddedBinaryLength];
        WriteUInt32(bytes, 0, 0x46546C67);
        WriteUInt32(bytes, 4, 2);
        WriteUInt32(bytes, 8, (uint)bytes.Length);
        WriteUInt32(bytes, 12, (uint)paddedJsonLength);
        WriteUInt32(bytes, 16, 0x4E4F534A);
        Array.Copy(jsonBytes, 0, bytes, 20, jsonBytes.Length);
        Array.Fill<byte>(bytes, 0x20, 20 + jsonBytes.Length, paddedJsonLength - jsonBytes.Length);
        var binaryHeader = 20 + paddedJsonLength;
        WriteUInt32(bytes, binaryHeader, (uint)paddedBinaryLength);
        WriteUInt32(bytes, binaryHeader + 4, 0x004E4942);
        Array.Copy(binaryPayload, 0, bytes, binaryHeader + 8, binaryPayload.Length);
        return bytes;
    }

    private static byte[] CreateImportedModelGlbWithMaterialExtensionTexture(string meshName)
    {
        var json = $$"""
        {
          "asset": { "version": "2.0", "generator": "Rekall AGE Test" },
          "scene": 0,
          "extensionsUsed": ["KHR_materials_clearcoat"],
          "scenes": [{ "name": "ImportedScene", "nodes": [0] }],
          "nodes": [{ "name": "ImportedRoot", "mesh": 0 }],
          "meshes": [
            { "name": "{{meshName}}", "primitives": [{ "attributes": { "POSITION": 0 }, "material": 0 }] }
          ],
          "materials": [
            {
              "name": "Clearcoat",
              "extensions": {
                "KHR_materials_clearcoat": {
                  "clearcoatTexture": { "index": 0 }
                }
              }
            }
          ],
          "images": [{ "name": "ClearcoatTexture", "mimeType": "image/png" }],
          "textures": [{ "source": 0 }]
        }
        """;
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var paddedJsonLength = (jsonBytes.Length + 3) / 4 * 4;
        var bytes = new byte[12 + 8 + paddedJsonLength];
        WriteUInt32(bytes, 0, 0x46546C67);
        WriteUInt32(bytes, 4, 2);
        WriteUInt32(bytes, 8, (uint)bytes.Length);
        WriteUInt32(bytes, 12, (uint)paddedJsonLength);
        WriteUInt32(bytes, 16, 0x4E4F534A);
        Array.Copy(jsonBytes, 0, bytes, 20, jsonBytes.Length);
        Array.Fill<byte>(bytes, 0x20, 20 + jsonBytes.Length, paddedJsonLength - jsonBytes.Length);
        return bytes;
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xff);
        bytes[offset + 1] = (byte)((value >> 8) & 0xff);
        bytes[offset + 2] = (byte)((value >> 16) & 0xff);
        bytes[offset + 3] = (byte)((value >> 24) & 0xff);
    }
}
