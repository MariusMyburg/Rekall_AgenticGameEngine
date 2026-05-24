using System.Reflection;

namespace Rekall.Age.Modules;

public static class RekallAgeModuleIndexer
{
    public static RekallAgeModuleIndex IndexAssembly(Assembly assembly)
    {
        var modules = assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(RekallAgeModule).IsAssignableFrom(type))
            .Select(IndexModule)
            .Where(module => module is not null)
            .Cast<RekallAgeModuleMetadata>()
            .OrderBy(module => module.Id, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeModuleIndex(modules);
    }

    public static RekallAgeModuleIndex IndexAssemblies(IEnumerable<Assembly> assemblies)
    {
        var modules = assemblies
            .SelectMany(assembly => IndexAssembly(assembly).Modules)
            .GroupBy(module => module.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(module => module.Id, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeModuleIndex(modules);
    }

    private static RekallAgeModuleMetadata? IndexModule(Type type)
    {
        var moduleAttribute = type.GetCustomAttribute<RekallAgeModuleAttribute>();
        if (moduleAttribute is null)
        {
            return null;
        }

        var builder = new RekallAgeModuleBuilder();
        var module = (RekallAgeModule?)Activator.CreateInstance(type, nonPublic: true)
            ?? throw new InvalidOperationException($"Module '{type.FullName}' could not be created.");
        module.Configure(builder);

        var capabilities = type.GetCustomAttributes<RekallAgeRequiresCapabilityAttribute>()
            .Select(attribute => attribute.Capability.Trim().ToLowerInvariant())
            .Where(capability => capability.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeModuleMetadata(
            moduleAttribute.Id,
            moduleAttribute.DisplayName,
            type.FullName!,
            capabilities,
            builder.ComponentTypes.Select(IndexComponent).OrderBy(component => component.TypeName, StringComparer.Ordinal).ToArray());
    }

    private static RekallAgeComponentSchema IndexComponent(Type type)
    {
        var componentAttribute = type.GetCustomAttribute<RekallAgeComponentAttribute>()
            ?? throw new InvalidOperationException($"Component '{type.FullName}' is missing RekallAgeComponentAttribute.");

        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<RekallAgePropertyAttribute>()))
            .Where(item => item.Attribute is not null)
            .Select(item => IndexProperty(item.Property, item.Attribute!))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeComponentSchema(type.FullName!, componentAttribute.DisplayName, properties);
    }

    private static RekallAgePropertySchema IndexProperty(PropertyInfo property, RekallAgePropertyAttribute attribute)
    {
        return new RekallAgePropertySchema(
            property.Name,
            SimplifyTypeName(property.PropertyType),
            attribute.Kind ?? InferKind(property.PropertyType),
            attribute.AssetKind,
            double.IsNaN(attribute.Minimum) ? null : attribute.Minimum,
            double.IsNaN(attribute.Maximum) ? null : attribute.Maximum);
    }

    private static string InferKind(Type type)
    {
        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return "number";
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short))
        {
            return "integer";
        }

        return "object";
    }

    private static string SimplifyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.Name;
    }
}
