using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record BootstrapOpenXrSessionRequest;

public sealed record BootstrapOpenXrSessionResult(
    bool LoaderAvailable,
    bool RuntimeAvailable,
    bool InstanceCreated,
    bool HmdSystemAvailable,
    ulong? SystemId,
    bool VulkanEnable2Available,
    bool PrimaryStereoReady,
    bool HeadsetSessionReady,
    IReadOnlyList<string> RequiredExtensions,
    IReadOnlyList<string> EnabledExtensions,
    IReadOnlyList<string> MissingExtensions,
    IReadOnlyList<string> NextRenderSteps,
    IReadOnlyList<string> Errors);

public sealed class BootstrapOpenXrSessionCommand
    : IRekallAgeCommand<BootstrapOpenXrSessionRequest, BootstrapOpenXrSessionResult>
{
    private readonly IRekallAgeOpenXrSessionBootstrap _bootstrap;

    public BootstrapOpenXrSessionCommand()
        : this(new RekallAgeNativeOpenXrSessionBootstrap())
    {
    }

    public BootstrapOpenXrSessionCommand(IRekallAgeOpenXrSessionBootstrap bootstrap)
    {
        _bootstrap = bootstrap;
    }

    public string Name => "rekall.render.openxr.bootstrap_session";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a short-lived OpenXR instance and checks whether a Vulkan-capable HMD system is available.",
        typeof(BootstrapOpenXrSessionRequest).FullName!,
        typeof(BootstrapOpenXrSessionResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<BootstrapOpenXrSessionResult>> ExecuteAsync(
        BootstrapOpenXrSessionRequest request,
        RekallAgeCommandContext context)
    {
        var bootstrap = await _bootstrap.BootstrapAsync(context.CancellationToken).ConfigureAwait(false);
        var result = new BootstrapOpenXrSessionResult(
            bootstrap.LoaderAvailable,
            bootstrap.RuntimeAvailable,
            bootstrap.InstanceCreated,
            bootstrap.HmdSystemAvailable,
            bootstrap.SystemId,
            bootstrap.VulkanEnable2Available,
            bootstrap.PrimaryStereoReady,
            bootstrap.HeadsetSessionReady,
            bootstrap.RequiredExtensions,
            bootstrap.EnabledExtensions,
            bootstrap.MissingExtensions,
            bootstrap.NextRenderSteps,
            bootstrap.Errors);

        if (result.HeadsetSessionReady)
        {
            return RekallAgeCommandResult<BootstrapOpenXrSessionResult>.Success(
                result,
                $"OpenXR HMD system {result.SystemId} is available for Vulkan session creation.");
        }

        return RekallAgeCommandResult<BootstrapOpenXrSessionResult>.Failure(
            result,
            "OpenXR session bootstrap is not ready.",
            [
                new RekallAgeCommandError(
                    "REKALL_OPENXR_SESSION_NOT_READY",
                    result.Errors.Count > 0
                        ? string.Join(" ", result.Errors)
                        : string.Join(" ", result.NextRenderSteps),
                    "openxr")
            ]);
    }
}
