using Rekall.Age.Modules;

namespace Rekall.Age.Runtime;

public sealed record RekallAgeRuntimeSystemRegistration(
    Type SystemType,
    Func<IRekallAgeRuntimeModuleSystem> CreateSystem);

public sealed record RekallAgeRuntimeModuleRegistration(
    Type ModuleType,
    Func<RekallAgeModule> CreateModule,
    IReadOnlyList<RekallAgeRuntimeSystemRegistration> RuntimeSystems)
{
    public string? ModuleId { get; init; }

    public string? ModuleName { get; init; }

    public string? AssemblyIdentity { get; init; }

    public string? SourceFingerprint { get; init; }
}
