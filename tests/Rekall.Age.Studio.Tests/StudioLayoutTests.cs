using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioLayoutTests
{
    [Fact]
    public void DefaultAndPresetsExposeEveryNamedPanelWithUsefulBounds()
    {
        var layout = RekallAgeStudioLayout.Default;

        Assert.Equal(RekallAgeStudioLayout.CurrentVersion, layout.Version);
        Assert.Equal(["Hierarchy", "Inspector", "Output"], layout.Panels.Select(panel => panel.Id).Order().ToArray());
        Assert.All(layout.Panels, panel => Assert.True(panel.Visible));
        Assert.InRange(layout.WindowWidth, 1120, 3840);
        Assert.InRange(layout.WindowHeight, 700, 2160);

        var authoring = RekallAgeStudioLayout.CreatePreset(RekallAgeStudioLayoutPreset.Authoring);
        Assert.Equal(RekallAgeStudioDockRegion.Left, authoring.Panel("Hierarchy").Region);
        Assert.Equal(RekallAgeStudioDockRegion.Right, authoring.Panel("Inspector").Region);
        Assert.Equal(RekallAgeStudioDockRegion.Bottom, authoring.Panel("Output").Region);
        Assert.Equal("AI Agent", authoring.ActiveOutputTab);

        var debug = RekallAgeStudioLayout.CreatePreset(RekallAgeStudioLayoutPreset.Debug);
        Assert.True(debug.Panel("Output").Size > authoring.Panel("Output").Size);
        Assert.Equal("Runtime", debug.ActiveOutputTab);
    }

    [Fact]
    public async Task LayoutStoreRoundTripsAValidatedLayoutAndReplacesThePreviousFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-layout-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "layout.json");
        try
        {
            var store = new RekallAgeStudioLayoutStore(path);
            var first = RekallAgeStudioLayout.Default with { WindowWidth = 1400, ActiveOutputTab = "Assets" };
            await store.SaveAsync(first, CancellationToken.None);
            var second = first with
            {
                WindowWidth = 1660,
                Panels = first.Panels.Select(panel => panel.Id == "Inspector"
                    ? panel with { Region = RekallAgeStudioDockRegion.Left, Visible = false, Size = 420 }
                    : panel).ToArray()
            };

            await store.SaveAsync(second, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal(1660, loaded.WindowWidth);
            Assert.Equal("Assets", loaded.ActiveOutputTab);
            Assert.False(loaded.Panel("Inspector").Visible);
            Assert.Equal(RekallAgeStudioDockRegion.Left, loaded.Panel("Inspector").Region);
            Assert.Equal(420, loaded.Panel("Inspector").Size);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"version\":999,\"panels\":[]}")]
    [InlineData("{\"version\":1,\"windowWidth\":0,\"windowHeight\":0,\"panels\":[]}")]
    public async Task LayoutStoreFallsBackForCorruptFutureOrIncompleteDocuments(string content)
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-layout-invalid-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "layout.json");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(path, content);

            var loaded = await new RekallAgeStudioLayoutStore(path).LoadAsync(CancellationToken.None);

            Assert.Equal(RekallAgeStudioLayout.Default, loaded);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LayoutStoreClampsFiniteSizesAndRejectsUnknownPanelData()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-layout-bounds-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "layout.json");
        try
        {
            var invalid = RekallAgeStudioLayout.Default with
            {
                WindowWidth = 99_000,
                WindowHeight = double.NaN,
                Panels =
                [
                    new("Hierarchy", RekallAgeStudioDockRegion.Left, true, 9_999, 0),
                    new("Inspector", RekallAgeStudioDockRegion.Right, true, -4, 0),
                    new("Output", RekallAgeStudioDockRegion.Bottom, true, 9_999, 0)
                ]
            };
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(invalid, new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            }));

            var loaded = await new RekallAgeStudioLayoutStore(path).LoadAsync(CancellationToken.None);

            Assert.Equal(3840, loaded.WindowWidth);
            Assert.Equal(RekallAgeStudioLayout.Default.WindowHeight, loaded.WindowHeight);
            Assert.Equal(720, loaded.Panel("Hierarchy").Size);
            Assert.Equal(180, loaded.Panel("Inspector").Size);
            Assert.Equal(640, loaded.Panel("Output").Size);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
