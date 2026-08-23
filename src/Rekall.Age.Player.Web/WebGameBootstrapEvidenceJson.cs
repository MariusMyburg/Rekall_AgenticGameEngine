using System.Text.Json;
using System.Text.Json.Serialization;
using Rekall.Age.Workflows.Web;

namespace Rekall.Age.Player.Web;

internal static class WebGameBootstrapEvidenceJson
{
    private static readonly WebGameBootstrapJsonContext JsonContext = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    public static string Serialize(RekallAgeWebBootstrapEvidence evidence) =>
        JsonSerializer.Serialize(evidence, JsonContext.RekallAgeWebBootstrapEvidence);
}

[JsonSerializable(typeof(RekallAgeWebBootstrapEvidence))]
internal sealed partial class WebGameBootstrapJsonContext : JsonSerializerContext;
