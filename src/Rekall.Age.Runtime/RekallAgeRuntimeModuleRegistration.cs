using Rekall.Age.Modules;

namespace Rekall.Age.Runtime;

public sealed record RekallAgeRuntimeSystemRegistration(
    Type SystemType,
    Func<IRekallAgeRuntimeModuleSystem> CreateSystem);

public sealed record RekallAgeRuntimeModuleRegistration(
    Type ModuleType,
    Func<RekallAgeModule> CreateModule,
    IReadOnlyList<RekallAgeRuntimeSystemRegistration> RuntimeSystems);
