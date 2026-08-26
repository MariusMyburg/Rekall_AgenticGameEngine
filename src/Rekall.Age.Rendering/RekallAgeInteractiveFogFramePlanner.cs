namespace Rekall.Age.Rendering;

/// <summary>
/// Selects a bounded fog-volume set for interactive per-fragment ray integration.
/// Froxel-authored quality remains explicit in diagnostics rather than being silently
/// represented as if a compute grid had executed.
/// </summary>
public sealed class RekallAgeInteractiveFogFramePlanner
{
    public const int MaximumVolumeCount = 8;

    public RekallAgeInteractiveFogFrame Plan(RekallAgeVulkanFogPlan? fogPlan)
    {
        if (fogPlan is not { Enabled: true })
        {
            return RekallAgeInteractiveFogFrame.Disabled;
        }

        var volumes = fogPlan.Volumes.Take(MaximumVolumeCount).ToArray();
        var diagnostics = new List<RekallAgeInteractiveFogDiagnostic>();
        if (fogPlan.UsesFroxelGrid)
        {
            diagnostics.Add(new RekallAgeInteractiveFogDiagnostic(
                "REKALL_INTERACTIVE_FOG_ANALYTIC_EXECUTION",
                $"Authored {fogPlan.Mode} fog is executing as bounded per-fragment analytic ray integration in this interactive backend."));
        }

        if (fogPlan.Volumes.Count > volumes.Length)
        {
            diagnostics.Add(new RekallAgeInteractiveFogDiagnostic(
                "REKALL_INTERACTIVE_FOG_VOLUME_LIMIT",
                $"Interactive analytic fog retained {volumes.Length} of {fogPlan.Volumes.Count} planned fog volumes."));
        }

        return new RekallAgeInteractiveFogFrame(
            volumes.Length > 0,
            fogPlan.Mode,
            "analytic-ray",
            volumes,
            diagnostics);
    }
}

public sealed record RekallAgeInteractiveFogFrame(
    bool Enabled,
    string RequestedMode,
    string ExecutedMode,
    IReadOnlyList<RekallAgeVulkanFogVolume> Volumes,
    IReadOnlyList<RekallAgeInteractiveFogDiagnostic> Diagnostics)
{
    public static RekallAgeInteractiveFogFrame Disabled { get; } = new(false, "disabled", "disabled", [], []);
}

public sealed record RekallAgeInteractiveFogDiagnostic(string Code, string Message);
