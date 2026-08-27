using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules;

namespace Rekall.Age.Modeling;

public sealed record RekallAgeProjectMeshPlugins(
    IReadOnlyList<IRekallAgeMeshOperationPlugin> Operations,
    IReadOnlyList<IRekallAgeFractureAlgorithmPlugin> FractureAlgorithms)
{
    public static readonly RekallAgeProjectMeshPlugins Empty = new([], []);
}

/// <summary>
/// Discovers project-registered mesh operation/fracture algorithm plugins the same way
/// <c>RekallAgeProjectRuntimeSystemLoader</c> discovers gameplay components/systems: load every
/// built module assembly for the project, instantiate each concrete <see cref="RekallAgeModule"/>,
/// call its <c>Configure</c>, and collect whatever it registered.
/// </summary>
public sealed class RekallAgeProjectMeshPluginLoader
{
    public RekallAgeProjectMeshPlugins Load(string projectRoot)
    {
        var operations = new List<IRekallAgeMeshOperationPlugin>();
        var algorithms = new List<IRekallAgeFractureAlgorithmPlugin>();

        foreach (var assembly in RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(projectRoot))
        {
            foreach (var moduleType in assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(RekallAgeModule).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                var module = (RekallAgeModule?)Activator.CreateInstance(moduleType, nonPublic: true)
                    ?? throw new InvalidOperationException($"Module '{moduleType.FullName}' could not be created.");
                var builder = new RekallAgeModuleBuilder();
                module.Configure(builder);

                foreach (var operationType in builder.MeshOperationTypes
                    .OrderBy(type => type.FullName, StringComparer.Ordinal))
                {
                    var operation = CreatePlugin<IRekallAgeMeshOperationPlugin>(operationType);
                    RequireDottedId(operation.OperationId, operationType);
                    operations.Add(operation);
                }

                foreach (var algorithmType in builder.FractureAlgorithmTypes
                    .OrderBy(type => type.FullName, StringComparer.Ordinal))
                {
                    var algorithm = CreatePlugin<IRekallAgeFractureAlgorithmPlugin>(algorithmType);
                    RequireDottedId(algorithm.AlgorithmId, algorithmType);
                    algorithms.Add(algorithm);
                }
            }
        }

        return new RekallAgeProjectMeshPlugins(operations, algorithms);
    }

    private static TPlugin CreatePlugin<TPlugin>(Type pluginType)
    {
        if (!typeof(TPlugin).IsAssignableFrom(pluginType))
        {
            throw new InvalidOperationException(
                $"Registered type '{pluginType.FullName}' does not implement {typeof(TPlugin).Name}.");
        }

        return (TPlugin?)Activator.CreateInstance(pluginType, nonPublic: true)
            ?? throw new InvalidOperationException($"Plugin '{pluginType.FullName}' could not be created.");
    }

    private static void RequireDottedId(string id, Type pluginType)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.Contains('.', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginType.FullName}' has id '{id}', which must contain '.' " +
                "(bare ids are reserved for built-in operations/algorithms).");
        }
    }
}
