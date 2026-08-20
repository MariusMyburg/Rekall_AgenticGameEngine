using Rekall.Age.Core.Commands;
using Rekall.Age.Modules.Sdk;

namespace Rekall.Age.Modules.Commands;

public sealed record InstallModuleSdkRequest(string ProjectRoot);

public sealed class InstallModuleSdkCommand
    : IRekallAgeCommand<InstallModuleSdkRequest, RekallAgeModuleSdkInstallation>
{
    public string Name => "rekall.module.install_sdk";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Installs or explicitly refreshes the project-local, integrity-manifested C# module SDK from the running Rekall AGE product.",
        typeof(InstallModuleSdkRequest).FullName!,
        typeof(RekallAgeModuleSdkInstallation).FullName!);

    public async ValueTask<RekallAgeCommandResult<RekallAgeModuleSdkInstallation>> ExecuteAsync(
        InstallModuleSdkRequest request,
        RekallAgeCommandContext context)
    {
        if (!Directory.Exists(request.ProjectRoot))
        {
            return RekallAgeCommandResult<RekallAgeModuleSdkInstallation>.Failure(
                new RekallAgeModuleSdkInstallation(0, string.Empty, string.Empty, string.Empty, [], []),
                $"Project root '{request.ProjectRoot}' does not exist.",
                [new RekallAgeCommandError(
                    "REKALL_PROJECT_NOT_FOUND",
                    $"Project root '{request.ProjectRoot}' does not exist.",
                    request.ProjectRoot)]);
        }

        var installation = await new RekallAgeModuleSdkInstaller()
            .InstallAsync(request.ProjectRoot, context.CancellationToken);
        foreach (var resource in installation.Resources)
        {
            context.Transaction.RecordChangedResource(resource);
        }

        return RekallAgeCommandResult<RekallAgeModuleSdkInstallation>.Success(
            installation,
            $"Installed module SDK compatibility version {installation.CompatibilityVersion}.");
    }
}
