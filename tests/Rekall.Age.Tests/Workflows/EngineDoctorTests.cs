using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Sdk;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class EngineDoctorTests
{
    [Fact]
    public async Task DoctorReportsProductHostAndPortableSdkEvidence()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeModuleSdkInstaller().InstallAsync(root, CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("doctor"),
            CancellationToken.None);

        var result = await new InspectEngineDoctorCommand().ExecuteAsync(
            new InspectEngineDoctorRequest(root),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("0.1.0-preview.1", result.Value.Product.Version);
        Assert.Contains(result.Value.Checks, check =>
            check.Id == "host.os" && check.Severity == "info");
        Assert.Contains(result.Value.Checks, check =>
            check.Id == "sdk.module" && check.Status == "ready");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.DoesNotContain(
            result.Value.Checks.SelectMany(check => check.Evidence),
            item => !string.IsNullOrWhiteSpace(userProfile) &&
                item.Contains(userProfile, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DoctorBlocksProjectWhenPortableSdkIsMissing()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("doctor missing sdk"),
            CancellationToken.None);

        var result = await new InspectEngineDoctorCommand().ExecuteAsync(
            new InspectEngineDoctorRequest(root),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Value.Checks, check =>
            check.Id == "sdk.module" && check.Status == "blocked" && check.Severity == "blocking");
        var error = Assert.Single(result.Errors, error => error.Code == "REKALL_SDK_MISSING");
        Assert.Contains(error.SuggestedCommands!, command => command.Tool == "rekall.module.scaffold_runtime_system");
    }
}
