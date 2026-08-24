using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Rekall.Age.Tests.Examples;

public sealed class AetherfallCitadelAcceptanceTests
{
    [Fact]
    public void MainSceneExposesSubstantialInspectableWorldContract()
    {
        var scenePath = Path.Combine(
            FindRepositoryRoot(),
            "Examples",
            "AetherfallCitadel",
            "Scenes",
            "Main.age.scene.json");
        var scene = JsonNode.Parse(File.ReadAllText(scenePath))!.AsObject();
        var entities = scene["entities"]!.AsArray();
        var components = entities
            .SelectMany(entity => entity!["components"]!.AsArray())
            .ToArray();

        Assert.True(entities.Count >= 60, $"Expected at least 60 stable entities, found {entities.Count}.");
        Assert.Contains(entities, entity => ReadString(entity, "name") == "AetherWarden");
        Assert.Contains(components, component => ReadString(component, "type") == "Rekall.InputActionMap");
        Assert.Contains(
            components,
            component => ReadString(component, "type") == "Game.Modules.AetherfallRules.WardenState");
        Assert.Contains(entities, entity => HasTag(entity, "zone.arrival"));
        Assert.Contains(entities, entity => HasTag(entity, "zone.resonance"));
        Assert.Contains(entities, entity => HasTag(entity, "zone.observatory"));
        Assert.Single(
            components,
            component =>
                ReadString(component, "type") == "Rekall.Camera3D"
                && ReadBoolean(component, "active"));
        Assert.Contains(components, component => ReadString(component, "type") == "Rekall.UiCanvas");
    }

    private static bool HasTag(JsonNode? entity, string expected) =>
        entity?["tags"] is JsonArray tags
        && tags.Any(tag => string.Equals(tag?.GetValue<string>(), expected, StringComparison.Ordinal));

    private static string? ReadString(JsonNode? node, string propertyName) =>
        node?[propertyName]?.GetValue<string>();

    private static bool ReadBoolean(JsonNode? component, string propertyName) =>
        component?["properties"]?[propertyName]?.GetValue<bool>() == true;

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
