using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record ProbeOpenXrRuntimeRequest;

public sealed record ProbeOpenXrRuntimeResult(
    bool LoaderAvailable,
    bool RuntimeAvailable,
    string? LoaderName,
    int ExtensionCount,
    IReadOnlyList<RekallAgeOpenXrExtensionInfo> InstanceExtensions,
    bool VulkanEnable2Available,
    bool PrimaryStereoReady,
    bool HeadsetLaunchReady,
    IReadOnlyList<string> RequiredNextSteps,
    IReadOnlyList<string> Errors);

public sealed class ProbeOpenXrRuntimeCommand
    : IRekallAgeCommand<ProbeOpenXrRuntimeRequest, ProbeOpenXrRuntimeResult>
{
    private readonly IRekallAgeOpenXrRuntimeProbe _probe;

    public ProbeOpenXrRuntimeCommand()
        : this(new RekallAgeNativeOpenXrRuntimeProbe())
    {
    }

    public ProbeOpenXrRuntimeCommand(IRekallAgeOpenXrRuntimeProbe probe)
    {
        _probe = probe;
    }

    public string Name => "rekall.render.openxr.probe";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Probes the OpenXR loader/runtime and reports Vulkan headset-rendering readiness.",
        typeof(ProbeOpenXrRuntimeRequest).FullName!,
        typeof(ProbeOpenXrRuntimeResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ProbeOpenXrRuntimeResult>> ExecuteAsync(
        ProbeOpenXrRuntimeRequest request,
        RekallAgeCommandContext context)
    {
        var probe = await _probe.ProbeAsync(context.CancellationToken).ConfigureAwait(false);
        var nextSteps = BuildRequiredNextSteps(probe);
        var result = new ProbeOpenXrRuntimeResult(
            probe.LoaderAvailable,
            probe.RuntimeAvailable,
            probe.LoaderName,
            probe.InstanceExtensions.Count,
            probe.InstanceExtensions,
            probe.VulkanEnable2Available,
            probe.PrimaryStereoReady,
            probe.LoaderAvailable && probe.RuntimeAvailable && probe.VulkanEnable2Available,
            nextSteps,
            probe.Errors);

        if (result.HeadsetLaunchReady)
        {
            return RekallAgeCommandResult<ProbeOpenXrRuntimeResult>.Success(
                result,
                $"OpenXR runtime '{result.LoaderName}' is available with XR_KHR_vulkan_enable2.");
        }

        return RekallAgeCommandResult<ProbeOpenXrRuntimeResult>.Failure(
            result,
            "OpenXR headset launch is not ready.",
            [
                new RekallAgeCommandError(
                    "REKALL_OPENXR_NOT_READY",
                    probe.Errors.Count == 0
                        ? string.Join(" ", nextSteps)
                        : string.Join(" ", probe.Errors),
                    "openxr")
            ]);
    }

    private static IReadOnlyList<string> BuildRequiredNextSteps(RekallAgeOpenXrProbeResult probe)
    {
        var steps = new List<string>();
        if (!probe.LoaderAvailable)
        {
            steps.Add("Install an OpenXR loader/runtime and make it the active system OpenXR runtime.");
        }
        else if (!probe.RuntimeAvailable)
        {
            steps.Add("Select or start an OpenXR runtime so instance extension enumeration succeeds.");
        }

        if (probe.RuntimeAvailable && !probe.VulkanEnable2Available)
        {
            steps.Add("Use an OpenXR runtime that exposes XR_KHR_vulkan_enable2 for Vulkan session creation.");
        }

        if (probe.LoaderAvailable && probe.RuntimeAvailable && probe.VulkanEnable2Available)
        {
            steps.Add("Implement xrCreateInstance/xrGetSystem/xrCreateSession with XrGraphicsBindingVulkan2KHR.");
            steps.Add("Create OpenXR swapchains for XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO and submit frames to the compositor.");
        }

        return steps;
    }
}
