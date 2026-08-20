using Rekall.Age.Modules.Security;

namespace Rekall.Age.Modules.Hosting;

public sealed record RekallAgeModuleHostLoadPlan(
    int SchemaVersion,
    int ProtocolVersion,
    string TrustPosture,
    IReadOnlyList<RekallAgeModuleHostLoadModule> Modules);

public sealed record RekallAgeModuleHostLoadModule(
    string ModuleName,
    string RelativeDirectory,
    string MainAssembly,
    IReadOnlyList<RekallAgeModuleArtifactIntegrity> Artifacts);
