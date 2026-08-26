using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Mcp;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshAuthoringClosedLoopTests
{
    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task McpAuthorsExtrudesRendersPicksUndoesAndRedoesAdvancedEditableMesh()
    {
        var root = TestPaths.CreateTempDirectory();
        var server = new RekallAgeMcpJsonRpcServer(RekallAgeDefaultCommandRegistry.Create());
        await CallAsync(server, "rekall.project.create", new { projectRoot = root, name = "Mesh Loop", capabilities = new[] { "world", "rendering3d" } });
        await CallAsync(server, "rekall.scene.create", new { projectRoot = root, name = "Main", capabilities = new[] { "world", "rendering3d" } });
        var created = await CallAsync(server, "rekall.mesh.create_asset", CreateMeshRequest(root));
        var revision = created.Content.GetProperty("value").GetProperty("mesh").GetProperty("fileRevision").GetString()!;
        var inspected = await CallAsync(server, "rekall.mesh.inspect", new { projectRoot = root, assetId = "agent-world", maximumSamples = 16 });
        Assert.Equal(2, inspected.Content.GetProperty("value").GetProperty("mesh").GetProperty("topology").GetProperty("faceCount").GetInt32());

        var applied = await CallAsync(server, "rekall.mesh.operation.apply", new
        {
            projectRoot = root,
            assetId = "agent-world",
            expectedRevision = revision,
            operation = new
            {
                operationId = "extrude_faces",
                domain = (int)RekallAgeGeometryDomain.Face,
                elementIds = new ulong[] { 21 },
                parameters = new { x = 0, y = 0, z = 1 }
            }
        });
        var historyAfterApply = await CallAsync(server, "rekall.transaction.history", new { projectRoot = root, limit = 20 });
        var applyTransaction = historyAfterApply.Content.GetProperty("value").GetProperty("transactions")
            .EnumerateArray()
            .Single(transaction => transaction.GetProperty("name").GetString() == "rekall.mesh.operation.apply");
        var applyTransactionId = applyTransaction.GetProperty("id").GetString()!;
        var relativeMeshPath = applyTransaction.GetProperty("resourcePreimages")[0].GetProperty("relativePath").GetString()!;
        var validated = await CallAsync(server, "rekall.mesh.validate", new { projectRoot = root, assetId = "agent-world" });
        Assert.True(validated.Content.GetProperty("value").GetProperty("isValid").GetBoolean());
        var compiled = await CallAsync(server, "rekall.mesh.inspect_compiled", new { projectRoot = root, assetId = "agent-world", maximumTriangles = 64 });
        var compiledValue = compiled.Content.GetProperty("value");
        Assert.True(compiledValue.GetProperty("triangleCount").GetInt32() >= 9);
        Assert.Equal(2, compiledValue.GetProperty("surfaceCount").GetInt32());
        Assert.Equal(
            [0, 1],
            compiledValue.GetProperty("surfaces").EnumerateArray()
                .Select(surface => surface.GetProperty("materialSlotIndex").GetInt32())
                .Distinct()
                .Order()
                .ToArray());
        Assert.Contains(compiledValue.GetProperty("triangles").EnumerateArray(), triangle => triangle.GetProperty("sourceFaceId").GetUInt64() == 21);
        var picked = await CallAsync(server, "rekall.mesh.pick_compiled", new
        {
            projectRoot = root,
            assetId = "agent-world",
            origin = new { x = 0.0, y = 0.0, z = -2.0 },
            direction = new { x = 0.0, y = 0.0, z = 1.0 },
            maximumDistance = 10.0,
            maximumHits = 16
        });
        var pickValue = picked.Content.GetProperty("value");
        Assert.True(pickValue.GetProperty("totalHitCount").GetInt32() > 0);
        Assert.Equal(21UL, pickValue.GetProperty("hits")[0].GetProperty("sourceFaceId").GetUInt64());

        var meshEntity = await CallAsync(server, "rekall.entity.create", new { projectRoot = root, sceneName = "Main", name = "Editable World", tags = new[] { "geometry", "acceptance" } });
        var meshEntityId = meshEntity.Content.GetProperty("value").GetProperty("entityId").GetString()!;
        await AddComponent(server, root, meshEntityId, "Rekall.Transform3D", new { scaleX = 2.0, scaleY = 2.0, scaleZ = 2.0 });
        await AddComponent(server, root, meshEntityId, "Rekall.MeshAssetReference", new { assetId = "agent-world" });
        await AddComponent(server, root, meshEntityId, "Rekall.MeshRenderer", new { mesh = "agent-world" });
        await AddComponent(server, root, meshEntityId, "Rekall.Material", new { baseColor = "#35c8ff", metallicFactor = 0.15, roughnessFactor = 0.55 });
        var camera = await CallAsync(server, "rekall.entity.create", new { projectRoot = root, sceneName = "Main", name = "Camera", tags = new[] { "camera" } });
        var cameraId = camera.Content.GetProperty("value").GetProperty("entityId").GetString()!;
        await AddComponent(server, root, cameraId, "Rekall.Transform3D", new { x = 0, y = 0, z = -6 });
        await AddComponent(server, root, cameraId, "Rekall.Camera3D", new { active = true, clearColor = "#101820", fieldOfView = 55 });
        var capture = await CallAsync(server, "rekall.render.capture_runtime_viewport", new
        {
            projectRoot = root,
            sceneName = "Main",
            frames = 0,
            outputDirectory = Path.Combine(root, "Evidence"),
            width = 640,
            height = 360,
            debugOverlay = false,
            backendId = "software"
        });
        var captureValue = capture.Content.GetProperty("value");
        Assert.True(captureValue.GetProperty("captured").GetBoolean());
        Assert.True(captureValue.GetProperty("nonBlank").GetBoolean());
        Assert.Equal(1, captureValue.GetProperty("renderableCount").GetInt32());
        var screenshotPath = captureValue.GetProperty("screenshotPath").GetString()!;
        var image = await RekallAgePngReader.ReadRgbaAsync(screenshotPath, CancellationToken.None);
        var pixels = image.Rgba.Chunk(4).Select(pixel => (pixel[0], pixel[1], pixel[2], pixel[3])).ToArray();
        var distinctPixelCount = pixels.Distinct().Count();
        var foregroundPixelCount = pixels.Count(pixel => pixel != (16, 24, 32, 255));
        var cyanPixelCount = pixels.Count(pixel => pixel.Item3 > pixel.Item1 + 70 && pixel.Item2 > pixel.Item1 + 40);
        Assert.True(distinctPixelCount >= 2);
        Assert.True(foregroundPixelCount > image.Width * image.Height / 100);
        Assert.True(cyanPixelCount > foregroundPixelCount / 2);

        await CallAsync(server, "rekall.transaction.restore_preimage", new
        {
            projectRoot = root,
            transactionId = applyTransactionId,
            relativePath = relativeMeshPath
        });
        var afterUndo = await CallAsync(server, "rekall.mesh.assert", new { projectRoot = root, assetId = "agent-world", expectedLogicalRevision = 1, minimumPointCount = 6, minimumFaceCount = 2 });
        Assert.True(afterUndo.Content.GetProperty("value").GetProperty("passed").GetBoolean());
        var historyAfterUndo = await CallAsync(server, "rekall.transaction.history", new { projectRoot = root, limit = 20 });
        var undoTransactionId = historyAfterUndo.Content.GetProperty("value").GetProperty("transactions")
            .EnumerateArray()
            .First(transaction => transaction.GetProperty("name").GetString() == "rekall.transaction.restore_preimage")
            .GetProperty("id").GetString()!;
        await CallAsync(server, "rekall.transaction.restore_preimage", new
        {
            projectRoot = root,
            transactionId = undoTransactionId,
            relativePath = relativeMeshPath
        });
        var afterRedo = await CallAsync(server, "rekall.mesh.assert", new { projectRoot = root, assetId = "agent-world", expectedLogicalRevision = 2, minimumPointCount = 11, minimumFaceCount = 7 });
        Assert.True(afterRedo.Content.GetProperty("value").GetProperty("passed").GetBoolean());
        var redoneMesh = await CallAsync(server, "rekall.mesh.inspect", new { projectRoot = root, assetId = "agent-world", maximumSamples = 16 });
        await PublishEvidenceIfRequestedAsync(
            screenshotPath,
            image.Width,
            image.Height,
            distinctPixelCount,
            foregroundPixelCount,
            cyanPixelCount,
            compiledValue,
            pickValue,
            redoneMesh.Content.GetProperty("value").GetProperty("mesh"));
    }

    private static async ValueTask PublishEvidenceIfRequestedAsync(
        string screenshotPath,
        int width,
        int height,
        int distinctPixelCount,
        int foregroundPixelCount,
        int cyanPixelCount,
        JsonElement compiled,
        JsonElement pick,
        JsonElement mesh)
    {
        var evidenceDirectory = Environment.GetEnvironmentVariable("REKALL_AGE_MODELING_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(evidenceDirectory);
        var imagePath = Path.Combine(evidenceDirectory, "agentic-modelling-closed-loop.png");
        File.Copy(screenshotPath, imagePath, overwrite: true);
        var imageBytes = await File.ReadAllBytesAsync(imagePath, CancellationToken.None);
        var topology = mesh.GetProperty("topology");
        var nearestHit = pick.GetProperty("hits")[0];
        var report = new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            transport = "MCP JSON-RPC default command registry",
            directAssetJsonEditing = false,
            assetId = "agent-world",
            finalLogicalRevision = mesh.GetProperty("logicalRevision").GetInt64(),
            topology = new
            {
                pointCount = topology.GetProperty("pointCount").GetInt32(),
                edgeCount = topology.GetProperty("edgeCount").GetInt32(),
                faceCount = topology.GetProperty("faceCount").GetInt32(),
                cornerCount = topology.GetProperty("cornerCount").GetInt32()
            },
            compiled = new
            {
                vertexCount = compiled.GetProperty("vertexCount").GetInt32(),
                hasVertexColors = compiled.GetProperty("hasVertexColors").GetBoolean(),
                indexCount = compiled.GetProperty("indexCount").GetInt32(),
                triangleCount = compiled.GetProperty("triangleCount").GetInt32(),
                surfaceCount = compiled.GetProperty("surfaceCount").GetInt32(),
                materialSlotIndices = compiled.GetProperty("surfaces").EnumerateArray()
                    .Select(surface => surface.GetProperty("materialSlotIndex").GetInt32()).Distinct().Order().ToArray()
            },
            pick = new
            {
                totalHitCount = pick.GetProperty("totalHitCount").GetInt32(),
                nearestDistance = nearestHit.GetProperty("distance").GetDouble(),
                sourceFaceId = nearestHit.GetProperty("sourceFaceId").GetUInt64(),
                sourceCornerIds = nearestHit.GetProperty("sourceCornerIds").EnumerateArray().Select(item => item.GetUInt64()).ToArray(),
                sourcePointIds = nearestHit.GetProperty("sourcePointIds").EnumerateArray().Select(item => item.GetUInt64()).ToArray(),
                surfaceIndex = nearestHit.GetProperty("surfaceIndex").GetInt32()
            },
            render = new
            {
                width,
                height,
                distinctPixelCount,
                foregroundPixelCount,
                cyanPixelCount,
                foregroundRatio = foregroundPixelCount / (double)(width * height),
                sha256 = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant(),
                file = Path.GetFileName(imagePath)
            },
            undo = new { restoredLogicalRevision = 1 },
            redo = new { restoredLogicalRevision = 2 }
        };
        await File.WriteAllTextAsync(
            Path.Combine(evidenceDirectory, "agentic-modelling-closed-loop.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            CancellationToken.None);
    }

    private static object CreateMeshRequest(string root)
    {
        var uv = new[]
        {
            new[] { 0.0, 0.0 }, new[] { 1.0, 0.0 }, new[] { 1.0, 0.5 }, new[] { 0.5, 1.0 }, new[] { 0.0, 0.5 },
            new[] { 1.0, 1.0 }, new[] { 0.25, 0.75 }, new[] { 0.5, 0.0 }
        }.Select(value => JsonSerializer.SerializeToElement(value)).ToArray();
        var materials = new[] { JsonSerializer.SerializeToElement(0), JsonSerializer.SerializeToElement(1) };
        return new
        {
            projectRoot = root,
            assetId = "agent-world",
            name = "Agent World",
            topology = new
            {
                pointIds = new ulong[] { 1, 2, 3, 4, 5, 6 },
                positions = new[] { new { x = -1.5, y = -1.0, z = 0.0 }, new { x = 1.5, y = -1.0, z = 0.0 }, new { x = 2.0, y = 0.2, z = 0.0 }, new { x = 0.0, y = 1.8, z = 0.0 }, new { x = -2.0, y = 0.2, z = 0.0 }, new { x = 0.0, y = -2.0, z = 0.0 } },
                edgeIds = new ulong[] { 11, 12, 13, 14, 15, 16, 17 },
                edgePointIndices = new[] { new { a = 0, b = 1 }, new { a = 1, b = 2 }, new { a = 2, b = 3 }, new { a = 3, b = 4 }, new { a = 4, b = 0 }, new { a = 1, b = 5 }, new { a = 5, b = 0 } },
                faceIds = new ulong[] { 21, 22 },
                faceOffsets = new[] { 0, 5, 8 },
                cornerIds = new ulong[] { 31, 32, 33, 34, 35, 36, 37, 38 },
                cornerPointIndices = new[] { 0, 1, 2, 3, 4, 1, 0, 5 },
                cornerEdgeIndices = new[] { 0, 1, 2, 3, 4, 0, 6, 5 }
            },
            attributes = new object[]
            {
                new { name = "uv", domain = (int)RekallAgeGeometryDomain.Corner, valueType = (int)RekallAgeGeometryValueType.Float2, values = uv, semantic = "texcoord-0" },
                new { name = "material.index", domain = (int)RekallAgeGeometryDomain.Face, valueType = (int)RekallAgeGeometryValueType.Int32, values = materials, semantic = "material-index" }
            },
            materialSlots = new[] { new { name = "cyan", materialAssetId = "material.cyan" }, new { name = "amber", materialAssetId = "material.amber" } },
            selectionSets = new[] { new { name = "extrude-top", domain = (int)RekallAgeGeometryDomain.Face, elementIds = new ulong[] { 21 } } }
        };
    }

    private static ValueTask<(JsonElement Content, RekallAgeCommandContext Context)> AddComponent(
        RekallAgeMcpJsonRpcServer server,
        string root,
        string entityId,
        string componentType,
        object properties) =>
        CallAsync(server, "rekall.component.add", new { projectRoot = root, sceneName = "Main", entityId, componentType, properties });

    private static async ValueTask<(JsonElement Content, RekallAgeCommandContext Context)> CallAsync(
        RekallAgeMcpJsonRpcServer server,
        string name,
        object arguments)
    {
        var context = new RekallAgeCommandContext("mcp-agent", RekallAgeTransaction.Begin(name), CancellationToken.None);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name, arguments }
        }, RequestJson);
        var response = await server.HandleJsonLineAsync(request, context);
        using var document = JsonDocument.Parse(response!);
        var result = document.RootElement.GetProperty("result");
        var content = result.GetProperty("structuredContent");
        Assert.True(content.GetProperty("ok").GetBoolean(), content.GetProperty("summary").GetString());
        return (content.Clone(), context);
    }
}
