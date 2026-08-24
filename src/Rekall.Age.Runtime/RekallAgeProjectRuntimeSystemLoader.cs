using System.Reflection;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeProjectRuntimeSystemLoader
{
    public IReadOnlyList<IRekallAgeRuntimeWorldSystem> Load(string projectRoot)
    {
        return Order(RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(projectRoot)
            .SelectMany(LoadFromAssembly));
    }

    public IReadOnlyList<IRekallAgeRuntimeWorldSystem> Load(IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(moduleTypes);
        return Order(moduleTypes.SelectMany(LoadFromModuleType));
    }

    public IReadOnlyList<IRekallAgeRuntimeWorldSystem> Load(
        IEnumerable<RekallAgeRuntimeModuleRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        return Order(registrations.SelectMany(LoadFromRegistration));
    }

    private static IReadOnlyList<IRekallAgeRuntimeWorldSystem> Order(
        IEnumerable<IRekallAgeRuntimeWorldSystem> systems) =>
        systems
            .OrderBy(system => system.Priority)
            .ThenBy(system => system.Id, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<IRekallAgeRuntimeWorldSystem> LoadFromAssembly(Assembly assembly)
    {
        foreach (var moduleType in assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(RekallAgeModule).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            foreach (var system in LoadFromModuleType(moduleType))
            {
                yield return system;
            }
        }
    }

    private static IEnumerable<IRekallAgeRuntimeWorldSystem> LoadFromModuleType(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        if (moduleType.IsAbstract || !typeof(RekallAgeModule).IsAssignableFrom(moduleType))
        {
            throw new ArgumentException(
                $"Registered type '{moduleType.FullName}' must be a concrete {nameof(RekallAgeModule)}.",
                nameof(moduleType));
        }

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

    private static IEnumerable<IRekallAgeRuntimeWorldSystem> LoadFromRegistration(
        RekallAgeRuntimeModuleRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registration.ModuleType);
        ArgumentNullException.ThrowIfNull(registration.CreateModule);
        ArgumentNullException.ThrowIfNull(registration.RuntimeSystems);
        if (registration.ModuleType.IsAbstract
            || !typeof(RekallAgeModule).IsAssignableFrom(registration.ModuleType))
        {
            throw new ArgumentException(
                $"Registered type '{registration.ModuleType.FullName}' must be a concrete {nameof(RekallAgeModule)}.",
                nameof(registration));
        }

        var module = registration.CreateModule()
            ?? throw new InvalidOperationException(
                $"Module factory for '{registration.ModuleType.FullName}' returned null.");
        if (module.GetType() != registration.ModuleType)
        {
            throw new InvalidOperationException(
                $"Module factory for '{registration.ModuleType.FullName}' returned '{module.GetType().FullName}'.");
        }

        var builder = new RekallAgeModuleBuilder();
        module.Configure(builder);
        var factories = registration.RuntimeSystems.ToDictionary(
            item => item.SystemType,
            item => item.CreateSystem);
        var configuredTypes = builder.RuntimeSystemTypes
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        if (factories.Count != registration.RuntimeSystems.Count
            || factories.Count != configuredTypes.Distinct().Count()
            || configuredTypes.Any(type => !factories.ContainsKey(type)))
        {
            throw new InvalidOperationException(
                $"Static runtime-system registrations for module '{registration.ModuleType.FullName}' do not match its Configure output.");
        }

        foreach (var systemType in configuredTypes)
        {
            var factory = factories[systemType]
                ?? throw new InvalidOperationException(
                    $"Runtime system factory for '{systemType.FullName}' is null.");
            var system = factory()
                ?? throw new InvalidOperationException(
                    $"Runtime system factory for '{systemType.FullName}' returned null.");
            if (system.GetType() != systemType)
            {
                throw new InvalidOperationException(
                    $"Runtime system factory for '{systemType.FullName}' returned '{system.GetType().FullName}'.");
            }
            yield return new ProjectRuntimeWorldSystemAdapter(system);
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
