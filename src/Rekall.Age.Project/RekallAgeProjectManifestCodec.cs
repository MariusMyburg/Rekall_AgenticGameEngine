using System.Text.Json;
using System.Text.Json.Serialization;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Project;

public static class RekallAgeProjectManifestCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };

    private static readonly RekallAgeProjectManifestJsonContext JsonContext = new(JsonOptions);

    public static string Serialize(RekallAgeProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var current = manifest with { SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion };
        return JsonSerializer.Serialize(current, JsonContext.RekallAgeProjectManifest) + Environment.NewLine;
    }

    public static RekallAgeProjectManifest Deserialize(ReadOnlyMemory<byte> bytes, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        RekallAgeDocumentSchemaProbe.Inspect(
            bytes,
            sourceName,
            "project",
            RekallAgeProductInfo.Current.ProjectSchemaVersion);
        return DeserializeValidated(bytes, sourceName);
    }

    internal static RekallAgeProjectManifest DeserializeValidated(
        ReadOnlyMemory<byte> bytes,
        string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        var manifest = JsonSerializer.Deserialize(bytes.Span, JsonContext.RekallAgeProjectManifest)
            ?? throw new InvalidDataException($"Project document '{sourceName}' has an invalid required shape.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Capabilities is null)
        {
            throw new InvalidDataException($"Project document '{sourceName}' has an invalid required shape.");
        }
        return manifest with { SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion };
    }
}

[JsonSerializable(typeof(RekallAgeProjectManifest), GenerationMode = JsonSourceGenerationMode.Metadata)]
internal sealed partial class RekallAgeProjectManifestJsonContext : JsonSerializerContext;
