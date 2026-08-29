using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Modules.Security;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Validation;

public sealed class RekallAgeBuiltInComponentPropertyAdmissionPolicy
    : IRekallAgeComponentPropertyAdmissionPolicy
{
    private static readonly IReadOnlyDictionary<string, RekallAgeComponentSchema> BuiltInSchemas =
        RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly)
            .Components.ToDictionary(component => component.TypeName, StringComparer.Ordinal);
    private readonly object _projectSchemaGate = new();
    private readonly Dictionary<string, ProjectSchemaCacheEntry> _projectSchemas =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public ValueTask<IReadOnlyList<RekallAgeCommandError>> ValidateAsync(
        string projectRoot,
        string componentType,
        JsonObject properties,
        string target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedComponentType = componentType.Trim();
        var resolution = TryGetSchema(projectRoot, normalizedComponentType, out var schema);
        if (resolution == SchemaResolution.Unavailable)
        {
            return ValueTask.FromResult<IReadOnlyList<RekallAgeCommandError>>([
                new RekallAgeCommandError(
                    "REKALL_PROJECT_COMPONENT_SCHEMA_UNAVAILABLE",
                    $"Project component schema validation is unavailable for '{normalizedComponentType}' because the module sources or verified build receipt changed. Build the project modules before authoring component properties.",
                    target,
                    [new RekallAgeSuggestedCommand("rekall.build.modules", new Dictionary<string, object?> { ["projectRoot"] = projectRoot })])
            ]);
        }
        if (resolution == SchemaResolution.NotFound)
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

            if (!HasExpectedPrimitiveType(authoredProperty.Value, propertySchema.Kind))
            {
                errors.Add(Error(
                    "REKALL_COMPONENT_PROPERTY_TYPE_INVALID",
                    $"Component '{schema.TypeName}' property '{propertySchema.Name}' must be a native JSON {ExpectedPrimitiveDescription(propertySchema.Kind)}.",
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

    private SchemaResolution TryGetSchema(
        string projectRoot,
        string componentType,
        out RekallAgeComponentSchema schema)
    {
        if (BuiltInSchemas.TryGetValue(componentType, out schema!))
        {
            return SchemaResolution.Found;
        }

        var fingerprint = ProjectSchemaFingerprint(projectRoot);
        lock (_projectSchemaGate)
        {
            if (_projectSchemas.TryGetValue(projectRoot, out var cached)
                && cached.Fingerprint.Equals(fingerprint, StringComparison.Ordinal))
            {
                return cached.Schemas.TryGetValue(componentType, out schema!)
                    ? SchemaResolution.Found
                    : SchemaResolution.NotFound;
            }

            try
            {
                var discovered = RekallAgeModuleIndexer
                    .IndexAssemblies(RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(projectRoot))
                    .Components
                    .GroupBy(component => component.TypeName, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
                _projectSchemas[projectRoot] = new ProjectSchemaCacheEntry(fingerprint, discovered);
                return discovered.TryGetValue(componentType, out schema!)
                    ? SchemaResolution.Found
                    : SchemaResolution.NotFound;
            }
            catch (RekallAgeModuleTrustException)
            {
                schema = null!;
                return ProjectContainsModules(projectRoot)
                    ? SchemaResolution.Unavailable
                    : SchemaResolution.NotFound;
            }
        }
    }

    private static bool ProjectContainsModules(string projectRoot)
    {
        var modulesRoot = Path.Combine(projectRoot, "Modules");
        if (IsReparsePoint(modulesRoot))
        {
            return true;
        }
        return Directory.Exists(modulesRoot) && Directory.EnumerateFiles(
            modulesRoot,
            "*.csproj",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true
            }).Any();
    }

    private static string ProjectSchemaFingerprint(string projectRoot)
    {
        var modulesRoot = Path.Combine(projectRoot, "Modules");
        if (!Directory.Exists(modulesRoot))
        {
            return "none";
        }
        if (IsReparsePoint(modulesRoot))
        {
            return "reparse-root";
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };
        return string.Join('|', Directory
            .EnumerateFiles(modulesRoot, "*", options)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).Equals(RekallAgeModuleBuildReceiptService.ReceiptFileName, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return $"{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            }));
    }

    private static bool IsReparsePoint(string path) =>
        Directory.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static bool HasExpectedPrimitiveType(JsonNode? node, string kind)
    {
        if (ExpectedStructuredShapeKind(kind))
        {
            return true;
        }

        if (node is null)
        {
            return kind is not ("number" or "integer" or "boolean" or "string" or "color" or "assetRef");
        }

        return kind switch
        {
            "number" => TryReadNativeNumber(node, out _),
            "integer" => TryReadNativeInteger(node),
            "boolean" => node is JsonValue boolean && boolean.TryGetValue<bool>(out _),
            "string" or "color" or "assetRef" =>
                node is JsonValue text && text.TryGetValue<string>(out _),
            _ => true
        };
    }

    private static bool ExpectedStructuredShapeKind(string kind) =>
        kind is "animationGraphParameters"
        || kind.EndsWith("s", StringComparison.Ordinal) && kind is not "string";

    private static string ExpectedPrimitiveDescription(string kind) => kind switch
    {
        "number" => "number",
        "integer" => "integer",
        "boolean" => "boolean",
        _ => "string"
    };

    private static bool TryReadNativeNumber(JsonNode? node, out double number)
    {
        if (node is JsonValue value
            && !value.TryGetValue<string>(out _)
            && TryReadNumber(value, out number))
        {
            return true;
        }

        number = default;
        return false;
    }

    private static bool TryReadNativeInteger(JsonNode? node)
    {
        if (!TryReadNativeNumber(node, out var number))
        {
            return false;
        }

        return number == Math.Truncate(number);
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

    private sealed record ProjectSchemaCacheEntry(
        string Fingerprint,
        IReadOnlyDictionary<string, RekallAgeComponentSchema> Schemas);

    private enum SchemaResolution
    {
        NotFound,
        Found,
        Unavailable
    }
}
