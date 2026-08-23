using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModifierStackValidator
{
    public const int MaximumModifiers = 2_048;
    private readonly RekallAgeModifierCatalog _catalog = RekallAgeModifierCatalog.CreateDefault();
    public IReadOnlyList<RekallAgeModelingGraphDiagnostic> Validate(RekallAgeModifierStackAsset stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var diagnostics = new List<RekallAgeModelingGraphDiagnostic>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        if (stack.SchemaVersion != RekallAgeModifierStackAsset.CurrentSchemaVersion) diagnostics.Add(Error("REKALL_MODIFIER_STACK_SCHEMA_UNSUPPORTED", $"Modifier stack schema {stack.SchemaVersion} is unsupported."));
        if (!ValidId(stack.AssetId) || !ValidId(stack.SourceMeshAssetId) || string.IsNullOrWhiteSpace(stack.Name) || stack.Name.Length > 256 || stack.Revision < 1) diagnostics.Add(Error("REKALL_MODIFIER_STACK_DOCUMENT_INVALID", "Modifier stack identity, name, revision, or source identity is invalid."));
        if (!RekallAgeDocumentRevision.IsValid(stack.SourceMeshFileRevision) || stack.SourceMeshFileRevision == RekallAgeDocumentRevision.Missing) diagnostics.Add(Error("REKALL_MODIFIER_STACK_SOURCE_REVISION_INVALID", "Modifier stack source mesh revision must be an exact SHA-256 revision."));
        if (stack.Modifiers.Count > MaximumModifiers) diagnostics.Add(Error("REKALL_MODIFIER_STACK_BOUNDS", $"Modifier stacks support at most {MaximumModifiers} modifiers."));
        foreach (var modifier in stack.Modifiers)
        {
            if (!ValidId(modifier.ModifierId) || !ids.Add(modifier.ModifierId)) { diagnostics.Add(Error("REKALL_MODIFIER_ID_DUPLICATE", $"Modifier ID '{modifier.ModifierId}' is invalid or duplicated.", modifier.ModifierId)); continue; }
            var descriptor = _catalog.Find(modifier.TypeId, modifier.TypeVersion);
            if (descriptor is null) { diagnostics.Add(Error("REKALL_MODIFIER_TYPE_UNKNOWN", $"Modifier type '{modifier.TypeId}@{modifier.TypeVersion}' is unknown.", modifier.ModifierId)); continue; }
            var allowed = descriptor.Parameters.ToDictionary(item => item.ParameterId, StringComparer.Ordinal);
            foreach (var parameter in modifier.Parameters)
            {
                if (!allowed.TryGetValue(parameter.Key, out var contract)) { diagnostics.Add(Error("REKALL_MODIFIER_PARAMETER_UNKNOWN", $"Parameter '{parameter.Key}' is unknown.", modifier.ModifierId)); continue; }
                if (contract.ValueType == RekallAgeModelingValueType.Scalar && (parameter.Value is not JsonValue value || !value.TryGetValue<double>(out var number) || !double.IsFinite(number) || number < (contract.Minimum ?? double.NegativeInfinity) || number > (contract.Maximum ?? double.PositiveInfinity))) diagnostics.Add(Error("REKALL_MODIFIER_PARAMETER_INVALID", $"Parameter '{parameter.Key}' is outside its finite range.", modifier.ModifierId));
                if (contract.ValueType == RekallAgeModelingValueType.String && (parameter.Value is not JsonValue textValue || !textValue.TryGetValue<string>(out var text) || text.Length > 128)) diagnostics.Add(Error("REKALL_MODIFIER_PARAMETER_INVALID", $"Parameter '{parameter.Key}' is not a bounded string.", modifier.ModifierId));
            }
        }
        return diagnostics;
    }
    private static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && char.IsAsciiLetterOrDigit(id[0]) && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    private static RekallAgeModelingGraphDiagnostic Error(string code, string message, string? id = null) => new(code, RekallAgeModelingDiagnosticSeverity.Error, message, id);
}
