using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Validation;

public sealed class RekallAgeBuiltInComponentPropertyAdmissionPolicy
    : IRekallAgeComponentPropertyAdmissionPolicy
{
    private static readonly IReadOnlyDictionary<string, RekallAgeComponentSchema> Schemas =
        RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly)
            .Components.ToDictionary(component => component.TypeName, StringComparer.Ordinal);

    public ValueTask<IReadOnlyList<RekallAgeCommandError>> ValidateAsync(
        string projectRoot,
        string componentType,
        JsonObject properties,
        string target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Schemas.TryGetValue(componentType.Trim(), out var schema))
        {
            return ValueTask.FromResult<IReadOnlyList<RekallAgeCommandError>>([]);
        }

        var errors = new List<RekallAgeCommandError>();
        var allowed = schema.Properties.ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
        var authoredSchemaProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var authoredProperty in properties)
        {
            if (!allowed.TryGetValue(authoredProperty.Key, out var propertySchema))
            {
                errors.Add(Error(
                    "REKALL_COMPONENT_PROPERTY_UNKNOWN",
                    $"Component '{schema.TypeName}' has no property '{authoredProperty.Key}'. Allowed properties: {string.Join(", ", allowed.Keys.OrderBy(name => name, StringComparer.Ordinal))}.",
                    $"{target}.properties.{authoredProperty.Key}",
                    schema.TypeName));
                continue;
            }

            if (!authoredSchemaProperties.Add(propertySchema.Name))
            {
                errors.Add(Error(
                    "REKALL_COMPONENT_PROPERTY_DUPLICATE",
                    $"Component '{schema.TypeName}' property '{propertySchema.Name}' was authored more than once with different casing.",
                    $"{target}.properties.{authoredProperty.Key}",
                    schema.TypeName));
                continue;
            }

            if (ExpectedStructuredShape(propertySchema) is { } expectedShape
                && !HasStructuredShape(authoredProperty.Value, expectedShape))
            {
                errors.Add(Error(
                    "REKALL_COMPONENT_PROPERTY_SHAPE_INVALID",
                    $"Component '{schema.TypeName}' property '{propertySchema.Name}' must be a native JSON {expectedShape}.",
                    $"{target}.properties.{authoredProperty.Key}",
                    schema.TypeName));
                continue;
            }

            if (TryReadNumber(authoredProperty.Value, out var number)
                && ((propertySchema.Minimum is not null && number < propertySchema.Minimum)
                    || (propertySchema.Maximum is not null && number > propertySchema.Maximum)))
            {
                errors.Add(Error(
                    "REKALL_COMPONENT_PROPERTY_OUT_OF_RANGE",
                    $"Component '{schema.TypeName}' property '{propertySchema.Name}' value {number} is outside its declared range.",
                    $"{target}.properties.{authoredProperty.Key}",
                    schema.TypeName));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<RekallAgeCommandError>>(errors);
    }

    private static RekallAgeCommandError Error(string code, string message, string target, string componentType) =>
        new(
            code,
            message,
            target,
            [
                new RekallAgeSuggestedCommand(
                    "rekall.module.search_component_schemas",
                    new Dictionary<string, object?> { ["query"] = componentType, ["limit"] = 20 })
            ]);

    private static string? ExpectedStructuredShape(RekallAgePropertySchema property) =>
        property.TypeName.EndsWith("[]", StringComparison.Ordinal)
            ? "array"
            : property.TypeName.Equals(nameof(JsonObject), StringComparison.Ordinal)
                || property.Kind.Equals("animationGraphParameters", StringComparison.Ordinal)
                ? "object"
                : null;

    private static bool HasStructuredShape(JsonNode? node, string expectedShape) =>
        expectedShape == "array" ? node is JsonArray : node is JsonObject;

    private static bool TryReadNumber(JsonNode? node, out double number)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out number))
            {
                return double.IsFinite(number);
            }
            if (value.TryGetValue<float>(out var single))
            {
                number = single;
                return float.IsFinite(single);
            }
            if (value.TryGetValue<int>(out var integer))
            {
                number = integer;
                return true;
            }
            if (value.TryGetValue<long>(out var longInteger))
            {
                number = longInteger;
                return true;
            }
            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                number = (double)decimalValue;
                return double.IsFinite(number);
            }
            if (value.TryGetValue<string>(out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return double.IsFinite(number);
            }
        }

        number = default;
        return false;
    }
}
