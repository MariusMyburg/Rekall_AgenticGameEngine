namespace Rekall.Age.Modules.Hosting;

public sealed record RekallAgeModuleHostPayloadManifest(
    int SchemaVersion,
    int ProtocolVersion,
    string ProductVersion,
    string ExecutablePath,
    IReadOnlyList<RekallAgeModuleHostPayloadFile> Files)
{
    public const string FileName = "rekall.module-host.manifest.json";
}

public sealed record RekallAgeModuleHostPayloadFile(
    string Path,
    long SizeBytes,
    string Sha256);
