using System.Text.Json;
using System.Text.Json.Serialization;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Product;

namespace Rekall.Age.World;

public static class RekallAgeSceneCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };

    private static readonly RekallAgeSceneJsonContext JsonContext = new(JsonOptions);

    public static string Serialize(RekallAgeSceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var current = scene with { SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion };
        return JsonSerializer.Serialize(current, JsonContext.RekallAgeSceneDocument) + Environment.NewLine;
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
        var scene = JsonSerializer.Deserialize(bytes.Span, JsonContext.RekallAgeSceneDocument)
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

[JsonSerializable(typeof(RekallAgeSceneDocument), GenerationMode = JsonSourceGenerationMode.Metadata)]
internal sealed partial class RekallAgeSceneJsonContext : JsonSerializerContext;
