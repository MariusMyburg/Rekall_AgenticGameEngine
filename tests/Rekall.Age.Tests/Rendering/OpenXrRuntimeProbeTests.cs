using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class OpenXrRuntimeProbeTests
{
    [Fact]
    public void LoaderCandidateNamesIncludeCurrentPlatformLoader()
    {
        var names = RekallAgeOpenXrLoaderCandidateNames.ForCurrentPlatform();

        Assert.NotEmpty(names);
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("openxr_loader.dll", names);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.Contains("libopenxr_loader.so.1", names);
        }
    }

    [Fact]
    public async Task ProbeCommandReportsHeadsetLaunchReadyWhenVulkanEnable2IsPresent()
    {
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("probe openxr"),
            CancellationToken.None);
        var probe = new FakeOpenXrProbe(new RekallAgeOpenXrProbeResult(
            true,
            true,
            "fake-openxr",
            [
                new RekallAgeOpenXrExtensionInfo("XR_KHR_vulkan_enable2", 2),
                new RekallAgeOpenXrExtensionInfo("XR_EXT_hand_tracking", 1)
            ],
            true,
            true,
            []));

        var result = await new ProbeOpenXrRuntimeCommand(probe).ExecuteAsync(new ProbeOpenXrRuntimeRequest(), context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.LoaderAvailable);
        Assert.True(result.Value.RuntimeAvailable);
        Assert.True(result.Value.VulkanEnable2Available);
        Assert.True(result.Value.HeadsetLaunchReady);
        Assert.Equal(2, result.Value.ExtensionCount);
        Assert.Contains(result.Value.RequiredNextSteps, step => step.Contains("xrCreateSession", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeCommandReportsMissingRuntimeWithoutThrowing()
    {
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("probe missing openxr"),
            CancellationToken.None);
        var probe = new FakeOpenXrProbe(new RekallAgeOpenXrProbeResult(
            false,
            false,
            null,
            [],
            false,
            false,
            ["OpenXR loader was not found."]));

        var result = await new ProbeOpenXrRuntimeCommand(probe).ExecuteAsync(new ProbeOpenXrRuntimeRequest(), context);

        Assert.False(result.Ok);
        Assert.False(result.Value.LoaderAvailable);
        Assert.False(result.Value.HeadsetLaunchReady);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_OPENXR_NOT_READY");
    }

    [Fact]
    public async Task BootstrapSessionCommandReportsReadyWhenInstanceAndHmdSystemAreAvailable()
    {
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("bootstrap openxr session"),
            CancellationToken.None);
        var bootstrap = new FakeOpenXrSessionBootstrap(new RekallAgeOpenXrSessionBootstrapResult(
            true,
            true,
            true,
            true,
            42,
            true,
            true,
            true,
            ["XR_KHR_vulkan_enable2"],
            ["XR_KHR_vulkan_enable2"],
            [],
            [
                "Create Vulkan graphics binding with XrGraphicsBindingVulkan2KHR.",
                "Create primary-stereo swapchains and submit frames with xrEndFrame."
            ],
            []));

        var result = await new BootstrapOpenXrSessionCommand(bootstrap).ExecuteAsync(
            new BootstrapOpenXrSessionRequest(),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.InstanceCreated);
        Assert.True(result.Value.HmdSystemAvailable);
        Assert.Equal(42UL, result.Value.SystemId);
        Assert.True(result.Value.HeadsetSessionReady);
        Assert.Contains(result.Value.NextRenderSteps, step => step.Contains("swapchains", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BootstrapSessionCommandReportsUnavailableHmdAsNotReady()
    {
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("bootstrap missing hmd"),
            CancellationToken.None);
        var bootstrap = new FakeOpenXrSessionBootstrap(new RekallAgeOpenXrSessionBootstrapResult(
            true,
            true,
            true,
            false,
            null,
            true,
            true,
            false,
            ["XR_KHR_vulkan_enable2"],
            ["XR_KHR_vulkan_enable2"],
            [],
            ["Connect or wake a headset and make it available to the active OpenXR runtime."],
            ["xrGetSystem did not return a head-mounted display system."]));

        var result = await new BootstrapOpenXrSessionCommand(bootstrap).ExecuteAsync(
            new BootstrapOpenXrSessionRequest(),
            context);

        Assert.False(result.Ok);
        Assert.True(result.Value.InstanceCreated);
        Assert.False(result.Value.HmdSystemAvailable);
        Assert.False(result.Value.HeadsetSessionReady);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_OPENXR_SESSION_NOT_READY");
    }

    private sealed class FakeOpenXrProbe : IRekallAgeOpenXrRuntimeProbe
    {
        private readonly RekallAgeOpenXrProbeResult _result;

        public FakeOpenXrProbe(RekallAgeOpenXrProbeResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeOpenXrProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }

    private sealed class FakeOpenXrSessionBootstrap : IRekallAgeOpenXrSessionBootstrap
    {
        private readonly RekallAgeOpenXrSessionBootstrapResult _result;

        public FakeOpenXrSessionBootstrap(RekallAgeOpenXrSessionBootstrapResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeOpenXrSessionBootstrapResult> BootstrapAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
