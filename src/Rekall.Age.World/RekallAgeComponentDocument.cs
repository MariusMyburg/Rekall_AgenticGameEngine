using System.Text.Json.Nodes;

namespace Rekall.Age.World;

public sealed record RekallAgeComponentDocument(string Type, JsonObject Properties)
{
    public static RekallAgeComponentDocument Create(string type, JsonObject? properties = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Component type is required.", nameof(type));
        }

        return new RekallAgeComponentDocument(type.Trim(), properties?.DeepClone().AsObject() ?? []);
    }
}
