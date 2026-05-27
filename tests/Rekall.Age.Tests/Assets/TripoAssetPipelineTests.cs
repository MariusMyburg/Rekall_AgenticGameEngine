using System.Text.Json.Nodes;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Assets;

public sealed class TripoAssetPipelineTests
{
    [Fact]
    public void TextToModelPayloadUsesOfficialTripoTaskShape()
    {
        var payload = RekallAgeTripoTaskPayloadBuilder.BuildTextToModel(new RekallAgeTripoTextToModelOptions(
            "a compact brass telescope",
            ModelVersion: "Turbo-v1.0-20250506",
            NegativePrompt: "blurry",
            ModelSeed: 42,
            Texture: true,
            FaceLimit: 12000));

        Assert.Equal("text_to_model", payload["type"]!.GetValue<string>());
        Assert.Equal("a compact brass telescope", payload["prompt"]!.GetValue<string>());
        Assert.Equal("Turbo-v1.0-20250506", payload["model_version"]!.GetValue<string>());
        Assert.Equal("blurry", payload["negative_prompt"]!.GetValue<string>());
        Assert.Equal(42, payload["model_seed"]!.GetValue<int>());
        Assert.True(payload["texture"]!.GetValue<bool>());
        Assert.Equal(12000, payload["face_limit"]!.GetValue<int>());
    }

    [Fact]
    public async Task GenerateTripoModelRequiresApiKeyBeforeCallingProvider()
    {
        var root = TestPaths.CreateTempDirectory();
        var client = new FakeTripoClient();
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("tripo missing key"),
            CancellationToken.None);

        var result = await new GenerateTripoModelCommand(client).ExecuteAsync(
            new GenerateTripoModelRequest(root, "a small robot", ApiKey: null, UseEnvironmentApiKey: false),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "TRIPO_API_KEY_MISSING");
        Assert.Equal(0, client.CreateCalls);
    }

    [Fact]
    public async Task GenerateTripoModelDownloadsAndImportsGlbIntoAssetPipeline()
    {
        var root = TestPaths.CreateTempDirectory();
        var client = new FakeTripoClient
        {
            CreatedTaskId = "task-123",
            Statuses = new Queue<RekallAgeTripoTaskStatus>([
                new RekallAgeTripoTaskStatus("task-123", "text_to_model", "queued", 0, null, null, null, null, null),
                new RekallAgeTripoTaskStatus("task-123", "text_to_model", "success", 100, "https://example.invalid/model.glb", null, null, null, 20)
            ]),
            DownloadBytes = CreateMinimalGlb()
        };
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("tripo generate"),
            CancellationToken.None);

        var result = await new GenerateTripoModelCommand(client).ExecuteAsync(
            new GenerateTripoModelRequest(
                root,
                "a modular sci-fi crate",
                DisplayName: "Sci-Fi Crate",
                ApiKey: "test-key",
                PollAttempts: 2,
                PollDelayMilliseconds: 0),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("task-123", result.Value.TaskId);
        Assert.Equal("success", result.Value.Status);
        Assert.NotNull(result.Value.Asset);
        Assert.Equal("model", result.Value.Asset.Kind);
        Assert.Equal("Sci-Fi Crate", result.Value.Asset.DisplayName);
        Assert.True(File.Exists(result.Value.Asset.ImportedPath));
        Assert.NotNull(result.Value.Asset.GlbMetadata);
        Assert.Equal("a modular sci-fi crate", client.CreatedOptions!.Prompt);
        Assert.Contains(context.Transaction.ChangedResources, path => path.EndsWith("asset-pipeline.age.json", StringComparison.Ordinal));

        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(root, CancellationToken.None);
        Assert.Contains(catalog.Assets, asset => asset.Id == result.Value.Asset.Id);
    }

    private static byte[] CreateMinimalGlb()
    {
        const string json = """
        {
          "asset": { "version": "2.0", "generator": "Tripo Fake" },
          "meshes": [{ "name": "GeneratedMesh", "primitives": [{ "attributes": { "POSITION": 0 } }] }]
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

    private sealed class FakeTripoClient : IRekallAgeTripoClient
    {
        public int CreateCalls { get; private set; }

        public string CreatedTaskId { get; init; } = "task";

        public RekallAgeTripoTextToModelOptions? CreatedOptions { get; private set; }

        public Queue<RekallAgeTripoTaskStatus> Statuses { get; init; } = new();

        public byte[] DownloadBytes { get; init; } = [];

        public ValueTask<string> CreateTextToModelTaskAsync(
            RekallAgeTripoTextToModelOptions options,
            string apiKey,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            CreatedOptions = options;
            return ValueTask.FromResult(CreatedTaskId);
        }

        public ValueTask<RekallAgeTripoTaskStatus> GetTaskAsync(
            string taskId,
            string apiKey,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Statuses.Dequeue());
        }

        public ValueTask<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(DownloadBytes);
        }
    }
}
