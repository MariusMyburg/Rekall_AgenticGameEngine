namespace Rekall.Age.Core.Product;

public static class RekallAgeCapabilityStability
{
    public const string Supported = "supported";
    public const string Experimental = "experimental";
    public const string Unavailable = "unavailable";
}

public sealed record RekallAgeProductMetadata(
    string Name,
    string Version,
    string Channel,
    int ProjectSchemaVersion,
    int ModuleSdkCompatibilityVersion,
    bool Proprietary,
    string SupportedHost);

public sealed record RekallAgeCapabilityStatus(
    string Id,
    string Stability,
    string Summary);

public static class RekallAgeProductInfo
{
    public static RekallAgeProductMetadata Current { get; } = new(
        "Rekall AGE",
        "0.1.0-preview.1",
        "preview",
        ProjectSchemaVersion: 1,
        ModuleSdkCompatibilityVersion: 1,
        Proprietary: true,
        SupportedHost: "windows-x64");

    public static IReadOnlyList<RekallAgeCapabilityStatus> Capabilities { get; } =
    [
        new(
            "authoring.core",
            RekallAgeCapabilityStability.Supported,
            "Project, scene, entity, component, transaction, and module authoring."),
        new(
            "runtime.desktop",
            RekallAgeCapabilityStability.Supported,
            "Windows desktop runtime and player."),
        new(
            "rendering.vulkan",
            RekallAgeCapabilityStability.Supported,
            "Vulkan-first desktop rendering."),
        new(
            "runtime.openxr",
            RekallAgeCapabilityStability.Experimental,
            "Windowed OpenXR play and diagnostics."),
        new(
            "runtime.multiplayer",
            RekallAgeCapabilityStability.Experimental,
            "Authoritative sessions, snapshots, deltas, and reconciliation."),
        new(
            "assets.tripo",
            RekallAgeCapabilityStability.Experimental,
            "External text-to-model provider bridge."),
        new(
            "rendering.virtual_geometry",
            RekallAgeCapabilityStability.Experimental,
            "CPU clustered mesh LOD.")
    ];

    public static RekallAgeCapabilityStatus Capability(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Capabilities.Single(item => item.Id.Equals(id, StringComparison.Ordinal));
    }
}
