namespace Rekall.Age.Modules.Security;

public static class RekallAgeModuleTrustPostures
{
    public const string InProcessFullTrust = "in-process-full-trust";
}

public sealed record RekallAgeModuleArtifactIntegrity(
    string Path,
    long SizeBytes,
    string Sha256,
    string? AssemblyIdentity = null);

public sealed record RekallAgeModuleBuildReceipt(
    int SchemaVersion,
    string TrustPosture,
    string ProductVersion,
    int SdkCompatibilityVersion,
    string ModuleName,
    string ProjectPath,
    string OutputRoot,
    IReadOnlyList<string> SourceFiles,
    string SourceFingerprint,
    IReadOnlyList<RekallAgeModuleArtifactIntegrity> OutputFiles);

public sealed record RekallAgeModuleTrustIssue(
    string Code,
    string Message,
    string Target);

public sealed record RekallAgeModuleTrustInspection(
    string ModuleName,
    string ModuleDirectory,
    string ReceiptPath,
    string TrustPosture,
    string SourceFingerprint,
    IReadOnlyList<RekallAgeModuleArtifactIntegrity> OutputFiles,
    bool Ready);

public sealed record RekallAgeProjectModuleTrustInspection(
    bool Ready,
    string TrustPosture,
    IReadOnlyList<RekallAgeModuleTrustInspection> Modules,
    IReadOnlyList<RekallAgeModuleTrustIssue> Issues);

public sealed record RekallAgeModuleTrustLimits(
    int MaximumModules = 256,
    int MaximumOutputFilesPerModule = 256,
    long MaximumOutputFileBytes = 512L * 1024 * 1024,
    long MaximumTotalOutputBytesPerModule = 2L * 1024 * 1024 * 1024,
    long MaximumReceiptBytes = 1024L * 1024);

public sealed class RekallAgeModuleReceiptException : Exception
{
    public RekallAgeModuleReceiptException(string code, string message, string target)
        : base(message)
    {
        Code = code;
        Target = target;
    }

    public string Code { get; }

    public string Target { get; }
}
