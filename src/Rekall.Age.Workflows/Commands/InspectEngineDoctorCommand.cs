using System.Runtime.InteropServices;
using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Workflows.Commands;

public sealed record RekallAgeDoctorCheck(
    string Id,
    string Status,
    string Severity,
    string Summary,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<RekallAgeSuggestedCommand> NextActions);

public sealed record InspectEngineDoctorRequest(string? ProjectRoot = null);

public sealed record InspectEngineDoctorResult(
    RekallAgeProductMetadata Product,
    string OperatingSystem,
    string Architecture,
    IReadOnlyList<RekallAgeDoctorCheck> Checks);

public sealed class InspectEngineDoctorCommand
    : IRekallAgeCommand<InspectEngineDoctorRequest, InspectEngineDoctorResult>
{
    private static readonly string[] RequiredSdkAssemblies =
    [
        "Rekall.Age.Core.dll",
        "Rekall.Age.World.dll",
        "Rekall.Age.Runtime.Abstractions.dll",
        "Rekall.Age.Modules.dll"
    ];

    public string Name => "rekall.context.doctor";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Checks the Rekall AGE host and project SDK and returns agent-readable remediation.",
        typeof(InspectEngineDoctorRequest).FullName!,
        typeof(InspectEngineDoctorResult).FullName!);

    public ValueTask<RekallAgeCommandResult<InspectEngineDoctorResult>> ExecuteAsync(
        InspectEngineDoctorRequest request,
        RekallAgeCommandContext context)
    {
        var checks = new List<RekallAgeDoctorCheck>();
        var errors = new List<RekallAgeCommandError>();
        AddHostChecks(checks, errors);
        if (!string.IsNullOrWhiteSpace(request.ProjectRoot))
        {
            AddProjectSdkCheck(request.ProjectRoot, checks, errors);
        }

        var result = new InspectEngineDoctorResult(
            RekallAgeProductInfo.Current,
            OperatingSystem.IsWindows() ? "windows" : RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            checks);
        return ValueTask.FromResult(errors.Count == 0
            ? RekallAgeCommandResult<InspectEngineDoctorResult>.Success(
                result,
                "Rekall AGE doctor found no supported-core blockers.")
            : RekallAgeCommandResult<InspectEngineDoctorResult>.Failure(
                result,
                $"Rekall AGE doctor found {errors.Count} supported-core blocker(s).",
                errors));
    }

    private static void AddHostChecks(
        ICollection<RekallAgeDoctorCheck> checks,
        ICollection<RekallAgeCommandError> errors)
    {
        if (OperatingSystem.IsWindows())
        {
            checks.Add(Ready("host.os", "Windows host is supported.", ["platform=windows"]));
        }
        else
        {
            checks.Add(Blocked(
                "host.os",
                "Developer Preview 1 requires Windows.",
                [$"platform={RuntimeInformation.OSDescription}"]));
            errors.Add(new RekallAgeCommandError(
                "REKALL_HOST_OS_UNSUPPORTED",
                "Developer Preview 1 requires a Windows host.",
                "host.os"));
        }

        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            checks.Add(Ready("host.architecture", "The x64 process architecture is supported.", ["architecture=x64"]));
        }
        else
        {
            checks.Add(Blocked(
                "host.architecture",
                "Developer Preview 1 requires an x64 process.",
                [$"architecture={RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}"]));
            errors.Add(new RekallAgeCommandError(
                "REKALL_HOST_ARCHITECTURE_UNSUPPORTED",
                "Developer Preview 1 requires an x64 process.",
                "host.architecture"));
        }

        checks.Add(Ready(
            "product.identity",
            "Product metadata is available.",
            [
                $"version={RekallAgeProductInfo.Current.Version}",
                $"channel={RekallAgeProductInfo.Current.Channel}",
                $"sdkCompatibility={RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion}"
            ]));
    }

    private static void AddProjectSdkCheck(
        string projectRoot,
        ICollection<RekallAgeDoctorCheck> checks,
        ICollection<RekallAgeCommandError> errors)
    {
        var compatibilityVersion = RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion;
        var sdkLabel = Path.Combine(".rekall", "sdk", compatibilityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var sdkRoot = Path.Combine(Path.GetFullPath(projectRoot), sdkLabel);
        var manifestPath = Path.Combine(sdkRoot, "rekall.sdk.json");
        var missingFiles = RequiredSdkAssemblies
            .Append("Rekall.Age.Sdk.props")
            .Append("rekall.sdk.json")
            .Where(file => !File.Exists(Path.Combine(sdkRoot, file)))
            .ToArray();
        if (missingFiles.Length > 0 || !HasCompatibleManifest(manifestPath, compatibilityVersion))
        {
            var nextAction = new RekallAgeSuggestedCommand(
                "rekall.module.scaffold_runtime_system",
                new Dictionary<string, object?>
                {
                    ["projectRoot"] = projectRoot,
                    ["moduleId"] = "game.rules",
                    ["displayName"] = "Game Rules",
                    ["moduleName"] = "GameRules",
                    ["componentName"] = "GameState",
                    ["systemName"] = "GameRulesSystem"
                });
            checks.Add(new RekallAgeDoctorCheck(
                "sdk.module",
                "blocked",
                "blocking",
                "The compatible project-local module SDK is missing or incomplete.",
                [$"sdk={sdkLabel}", $"missingFiles={string.Join(',', missingFiles)}"],
                [nextAction]));
            errors.Add(new RekallAgeCommandError(
                "REKALL_SDK_MISSING",
                $"Rekall AGE module SDK compatibility version {compatibilityVersion} is missing or incomplete.",
                sdkLabel,
                [nextAction]));
            return;
        }

        checks.Add(Ready(
            "sdk.module",
            "The project-local module SDK is ready.",
            [$"sdk={sdkLabel}", $"compatibilityVersion={compatibilityVersion}"]));
    }

    private static bool HasCompatibleManifest(string manifestPath, int compatibilityVersion)
    {
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return document.RootElement.TryGetProperty("compatibilityVersion", out var version) &&
                version.GetInt32() == compatibilityVersion;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static RekallAgeDoctorCheck Ready(string id, string summary, IReadOnlyList<string> evidence)
    {
        return new RekallAgeDoctorCheck(id, "ready", "info", summary, evidence, []);
    }

    private static RekallAgeDoctorCheck Blocked(string id, string summary, IReadOnlyList<string> evidence)
    {
        return new RekallAgeDoctorCheck(id, "blocked", "blocking", summary, evidence, []);
    }
}
