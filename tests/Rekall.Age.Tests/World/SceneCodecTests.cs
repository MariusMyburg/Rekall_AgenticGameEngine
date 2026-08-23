using System.Text;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.World;

namespace Rekall.Age.Tests.World;

public sealed class SceneCodecTests
{
    [Fact]
    public async Task MemoryBytesAndFilesystemStoreDecodeTheSameScene()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"]));
        await store.SaveAsync(root, scene, CancellationToken.None);
        var bytes = await File.ReadAllBytesAsync(store.GetScenePath(root, "Main"));

        var fromBytes = RekallAgeSceneCodec.Deserialize(bytes, "Main", "Scenes/Main.age.scene.json");
        var fromFile = await store.LoadAsync(root, "Main", CancellationToken.None);

        Assert.Equal(fromFile.SchemaVersion, fromBytes.SchemaVersion);
        Assert.Equal(fromFile.Id, fromBytes.Id);
        Assert.Equal(fromFile.Name, fromBytes.Name);
        Assert.Equal(fromFile.Capabilities, fromBytes.Capabilities);
        Assert.Equal(fromFile.Entities.Select(entity => entity.Id), fromBytes.Entities.Select(entity => entity.Id));
    }

    [Fact]
    public void CodecNormalizesLegacySchemaWithoutRewritingBytes()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """{"id":"scene_legacy","name":"Main","capabilities":[],"entities":[]}""");

        var scene = RekallAgeSceneCodec.Deserialize(bytes, "Main", "memory:Main");

        Assert.Equal(1, scene.SchemaVersion);
        Assert.DoesNotContain("schemaVersion", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]", "REKALL_DOCUMENT_SCHEMA_INVALID")]
    [InlineData("{", "REKALL_DOCUMENT_JSON_MALFORMED")]
    [InlineData("{\"schemaVersion\":2,\"id\":\"future\",\"name\":\"Main\",\"capabilities\":[],\"entities\":[]}", "REKALL_DOCUMENT_SCHEMA_FUTURE")]
    public void CodecPreservesDocumentCompatibilityFailures(string json, string expectedCode)
    {
        var error = Assert.Throws<RekallAgeDocumentCompatibilityException>(() =>
            RekallAgeSceneCodec.Deserialize(Encoding.UTF8.GetBytes(json), "Main", "memory:Main"));

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("memory:Main", error.DocumentPath);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"id\":\"\",\"name\":\"Main\",\"capabilities\":[],\"entities\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"scene_wrong\",\"name\":\"Other\",\"capabilities\":[],\"entities\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"scene_null\",\"name\":\"Main\",\"capabilities\":null,\"entities\":[]}")]
    public void CodecRejectsInvalidRequiredSceneShape(string json)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            RekallAgeSceneCodec.Deserialize(Encoding.UTF8.GetBytes(json), "Main", "memory:Main"));

        Assert.Contains("invalid required shape", error.Message, StringComparison.Ordinal);
    }
}
