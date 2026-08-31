using System.Text.Json;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio;

internal sealed class RekallAgeCodexApprovalSession
{
    private static readonly HashSet<string> KeyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "command", "cwd", "path", "filePath", "server", "serverName", "mcpServer",
        "tool", "toolName", "message"
    };

    private readonly HashSet<string> _approvedActions = new(StringComparer.Ordinal);

    public bool ApproveAll { get; set; }

    public bool IsApproved(RekallAgeCodexApprovalRequest request) =>
        ApproveAll || _approvedActions.Contains(ActionKey(request));

    public void ApproveAction(RekallAgeCodexApprovalRequest request) =>
        _approvedActions.Add(ActionKey(request));

    public void Clear()
    {
        ApproveAll = false;
        _approvedActions.Clear();
    }

    private static string ActionKey(RekallAgeCodexApprovalRequest request)
    {
        var facts = new List<string> { request.Method };
        Collect(request.Parameters, facts);
        return string.Join('\n', facts);
    }

    private static void Collect(JsonElement element, List<string> facts)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (KeyFields.Contains(property.Name))
                {
                    facts.Add(property.Name.ToLowerInvariant() + "=" + Display(property.Value));
                }
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    Collect(property.Value, facts);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) Collect(item, facts);
        }
    }

    private static string Display(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => string.Join(" ", value.EnumerateArray().Select(Display)),
        JsonValueKind.String => value.GetString() ?? string.Empty,
        _ => value.GetRawText()
    };
}
