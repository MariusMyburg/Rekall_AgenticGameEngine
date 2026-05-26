using System.Reflection;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeProjectRuntimeSystemLoader
{
    public IReadOnlyList<IRekallAgeRuntimeWorldSystem> Load(string projectRoot)
    {
        return RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(projectRoot)
            .SelectMany(LoadFromAssembly)
            .OrderBy(system => system.Priority)
            .ThenBy(system => system.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<IRekallAgeRuntimeWorldSystem> LoadFromAssembly(Assembly assembly)
    {
        foreach (var moduleType in assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(RekallAgeModule).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var module = (RekallAgeModule?)Activator.CreateInstance(moduleType, nonPublic: true)
                ?? throw new InvalidOperationException($"Module '{moduleType.FullName}' could not be created.");
            var builder = new RekallAgeModuleBuilder();
            module.Configure(builder);

            foreach (var systemType in builder.RuntimeSystemTypes
                .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                if (!typeof(IRekallAgeRuntimeModuleSystem).IsAssignableFrom(systemType))
                {
                    throw new InvalidOperationException(
                        $"Runtime system '{systemType.FullName}' does not implement IRekallAgeRuntimeModuleSystem.");
                }

                var system = (IRekallAgeRuntimeModuleSystem?)Activator.CreateInstance(systemType, nonPublic: true)
                    ?? throw new InvalidOperationException($"Runtime system '{systemType.FullName}' could not be created.");
                yield return new ProjectRuntimeWorldSystemAdapter(system);
            }
        }
    }

    private sealed class ProjectRuntimeWorldSystemAdapter(
        IRekallAgeRuntimeModuleSystem system) : IRekallAgeRuntimeWorldSystem
    {
        public string Id => system.Id;

        public int Priority => system.Priority;

        public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
            RekallAgeRuntimeWorld world,
            RekallAgeRuntimeWorldFrameContext context)
        {
            return system.UpdateAsync(
                world,
                new RekallAgeRuntimeModuleFrameContext(
                    context.FrameIndex,
                    context.DeltaTime,
                    context.ElapsedTime,
                    context.CancellationToken)
                {
                    Input = context.Input
                });
        }
    }
}
