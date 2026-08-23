using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Product;

namespace Rekall.Age.World;

public static class RekallAgeSceneCodec
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };

    public static string Serialize(RekallAgeSceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var current = scene with { SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion };
        return JsonSerializer.Serialize(current, JsonOptions) + Environment.NewLine;
    }

    public static RekallAgeSceneDocument Deserialize(
        ReadOnlyMemory<byte> bytes,
        string expectedSceneName,
        string sourceName)
    {
        ValidateArguments(expectedSceneName, sourceName);
        RekallAgeDocumentSchemaProbe.Inspect(
            bytes,
            sourceName,
            "scene",
            RekallAgeProductInfo.Current.ProjectSchemaVersion);
        return DeserializeValidated(bytes, expectedSceneName, sourceName);
    }

    internal static RekallAgeSceneDocument DeserializeValidated(
        ReadOnlyMemory<byte> bytes,
        string expectedSceneName,
        string sourceName)
    {
        ValidateArguments(expectedSceneName, sourceName);
        var scene = JsonSerializer.Deserialize<RekallAgeSceneDocument>(bytes.Span, JsonOptions)
            ?? throw new InvalidDataException($"Scene document '{sourceName}' has an invalid required shape.");
        if (string.IsNullOrWhiteSpace(scene.Id)
            || !string.Equals(scene.Name, expectedSceneName, StringComparison.Ordinal)
            || scene.Capabilities is null
            || scene.Entities is null)
        {
            throw new InvalidDataException($"Scene document '{sourceName}' has an invalid required shape.");
        }

        return scene with { SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion };
    }

    private static void ValidateArguments(string expectedSceneName, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSceneName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
    }
}
