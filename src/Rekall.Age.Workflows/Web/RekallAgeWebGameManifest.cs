namespace Rekall.Age.Workflows.Web;

public sealed record RekallAgeWebGameManifest(
    string Kind,
    int SchemaVersion,
    RekallAgeWebEngineIdentity Engine,
    RekallAgeWebProjectIdentity Project,
    string EntryScenePath,
    RekallAgeWebViewportPolicy Viewport,
    IReadOnlyList<RekallAgeWebModuleIdentity> Modules,
    IReadOnlyList<string> RequiredRenderingCapabilities,
    IReadOnlyList<RekallAgeWebContentEntry> Content,
    string BuildIdentity);

public sealed record RekallAgeWebEngineIdentity(
    string ProductVersion,
    int ProjectSchemaVersion,
    int ModuleSdkCompatibilityVersion);

public sealed record RekallAgeWebProjectIdentity(
    string Name,
    string ProjectManifestSha256);

public sealed record RekallAgeWebModuleIdentity(
    string Id,
    string ModuleName,
    string AssemblyIdentity,
    string SourceFingerprint);

public sealed record RekallAgeWebViewportPolicy(
    int ReferenceWidth,
    int ReferenceHeight,
    string ResizeMode);

public sealed record RekallAgeWebContentEntry(
    string Path,
    string MediaType,
    long SizeBytes,
    string Sha256);
