using System.IO;
using System.Text.Json.Nodes;
using Rekall.Age.World;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioAnimationMixerSessionTests
{
    [Fact]
    public async Task OpensAnEntitysMixerAndAppliesAWeightEditThroughTheRealSceneDocumentPipeline()
    {
        var root = TemporaryRoot();
        try
        {
            var scene = SceneWithMixer();
            await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
            var session = new RekallAgeStudioAnimationMixerSession();

            await session.OpenAsync(root, "Main", "warden", CancellationToken.None);

            Assert.True(session.HasMixer);
            Assert.Equal("Warden", session.EntityName);
            var idle = Assert.Single(session.Layers, layer => layer.Name == "idle");
            Assert.Equal("warden-idle", idle.Clip);
            Assert.Equal("1", idle.Weight);

            var edited = session.Layers.Select(layer => layer.Name == "idle"
                ? new RekallAgeStudioAnimationMixerLayerModel(layer.Name, layer.Clip, "0.4", layer.LoopMode, layer.Speed)
                : layer).ToArray();
            await session.ApplyAsync(edited, CancellationToken.None);

            var persisted = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var mixer = persisted.Entities.Single(entity => entity.Id == "warden")
                .Components.Single(component => component.Type == "Rekall.AnimationMixer");
            var layers = (JsonArray)mixer.Properties["layers"]!;
            var idleJson = layers.OfType<JsonObject>().Single(layer => layer["name"]!.GetValue<string>() == "idle");
            Assert.Equal(0.4, idleJson["weight"]!.GetValue<double>(), precision: 6);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsApplyingADuplicateLayerName()
    {
        var root = TemporaryRoot();
        try
        {
            await new RekallAgeSceneStore().SaveAsync(root, SceneWithMixer(), CancellationToken.None);
            var session = new RekallAgeStudioAnimationMixerSession();
            await session.OpenAsync(root, "Main", "warden", CancellationToken.None);
            var duplicated = new[]
            {
                new RekallAgeStudioAnimationMixerLayerModel("idle", "warden-idle", "1", "loop", "1"),
                new RekallAgeStudioAnimationMixerLayerModel("idle", "warden-walk", "0", "loop", "1")
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await session.ApplyAsync(duplicated, CancellationToken.None));

            Assert.Contains("duplicated", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string TemporaryRoot() =>
        Path.Combine(Path.GetTempPath(), "rekall-age-studio-mixer-" + Guid.NewGuid().ToString("N"));

    private static RekallAgeSceneDocument SceneWithMixer() =>
        RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(
            RekallAgeEntityDocument.Create("Warden", ["player"]) with
            {
                Components =
                [
                    new RekallAgeComponentDocument("Rekall.AnimationMixer", new JsonObject
                    {
                        ["playing"] = true,
                        ["layers"] = new JsonArray(
                            new JsonObject { ["name"] = "idle", ["clip"] = "warden-idle", ["weight"] = 1, ["loopMode"] = "loop" },
                            new JsonObject { ["name"] = "walk", ["clip"] = "warden-walk", ["weight"] = 0, ["loopMode"] = "loop" })
                    })
                ]
            } with { Id = "warden" });
}
