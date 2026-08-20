using System.Reflection;
using Rekall.Age.Modules;
using Rekall.Age.Modules.Hosting;

namespace Rekall.Age.ModuleHost;

internal sealed class RekallAgeModuleHostSession(
    IReadOnlyList<RekallAgeModuleHostSession.SystemEntry> systems,
    IReadOnlyList<RekallAgeComponentSchema> componentSchemas,
    IRekallAgePlayableModule? playable)
{
    public IReadOnlyList<SystemEntry> Systems { get; } = systems;

    public IReadOnlyList<RekallAgeComponentSchema> ComponentSchemas { get; } = componentSchemas;

    public IRekallAgePlayableModule? Playable { get; } = playable;

    public RekallAgePlayableModuleState? PlayableState { get; set; }

    public static RekallAgeModuleHostSession Load(string loadPlanPath)
    {
        var loaded = RekallAgeModuleHostVerifiedAssemblyLoader.Load(loadPlanPath);
        var systems = new List<SystemEntry>();
        var playables = new List<IRekallAgePlayableModule>();
        foreach (var item in loaded)
        {
            foreach (var moduleType in item.Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(RekallAgeModule).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                var attribute = moduleType.GetCustomAttribute<RekallAgeModuleAttribute>()
                    ?? throw new InvalidOperationException($"Module '{moduleType.FullName}' has no module identity.");
                var module = (RekallAgeModule?)Activator.CreateInstance(moduleType, nonPublic: true)
                    ?? throw new InvalidOperationException($"Module '{moduleType.FullName}' could not be created.");
                var builder = new RekallAgeModuleBuilder();
                module.Configure(builder);
                foreach (var systemType in builder.RuntimeSystemTypes.OrderBy(type => type.FullName, StringComparer.Ordinal))
                {
                    var system = (IRekallAgeRuntimeModuleSystem?)Activator.CreateInstance(systemType, nonPublic: true)
                        ?? throw new InvalidOperationException($"Runtime system '{systemType.FullName}' could not be created.");
                    systems.Add(new SystemEntry(attribute.Id, system));
                }
            }

            playables.AddRange(item.Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(IRekallAgePlayableModule).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .Select(type => (IRekallAgePlayableModule?)Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException($"Playable module '{type.FullName}' could not be created.")));
        }

        var orderedSystems = systems
            .OrderBy(item => item.System.Priority)
            .ThenBy(item => item.System.Id, StringComparer.Ordinal)
            .ToArray();
        if (orderedSystems.Select(item => item.System.Id).Distinct(StringComparer.Ordinal).Count() != orderedSystems.Length
            || playables.Count > 1)
        {
            throw new RekallAgeModuleHostException(
                "REKALL_MODULE_HOST_OUTPUT_INVALID",
                "Module-host discovery produced duplicate system IDs or multiple playable modules.");
        }

        var schemas = RekallAgeModuleIndexer.IndexAssemblies(loaded.Select(item => item.Assembly)).Components;
        return new RekallAgeModuleHostSession(orderedSystems, schemas, playables.SingleOrDefault());
    }

    internal sealed record SystemEntry(string ModuleId, IRekallAgeRuntimeModuleSystem System);
}
