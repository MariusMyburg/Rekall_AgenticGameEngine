using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Build.Commands;

public sealed record BuildModulesRequest(string ProjectRoot);

public sealed record BuildModulesResult(IReadOnlyList<BuildModuleResult> Modules);

public sealed record BuildModuleResult(
    string ModuleName,
    string ProjectPath,
    string AssemblyPath,
    bool Succeeded,
    string Output,
    int ExitCode,
    string SdkVersion)
{
    public bool TimedOut { get; init; }

    public string ReceiptPath { get; init; } = string.Empty;

    public string TrustPosture { get; init; } = string.Empty;
}

public sealed class BuildModulesCommand
    : IRekallAgeCommand<BuildModulesRequest, BuildModulesResult>
{
    private readonly RekallAgeModuleBuildPolicy _buildPolicy;
    private readonly RekallAgeModuleSdkIntegrityVerifier _sdkIntegrityVerifier;
    private readonly RekallAgeModuleBuildReceiptService _receiptService;
    private readonly TimeSpan _buildTimeout;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;

    private static readonly TimeSpan DefaultBuildTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessCleanupTimeout = TimeSpan.FromSeconds(5);

    public BuildModulesCommand()
        : this(
            new RekallAgeModuleBuildPolicy(),
            new RekallAgeModuleSdkIntegrityVerifier(),
            new RekallAgeModuleBuildReceiptService(),
            DefaultBuildTimeout,
            Process.Start)
    {
    }

    internal BuildModulesCommand(RekallAgeModuleBuildPolicy buildPolicy)
        : this(
            buildPolicy,
            new RekallAgeModuleSdkIntegrityVerifier(),
            new RekallAgeModuleBuildReceiptService(),
            DefaultBuildTimeout,
            Process.Start)
    {
    }

    internal BuildModulesCommand(
        RekallAgeModuleBuildPolicy buildPolicy,
        RekallAgeModuleSdkIntegrityVerifier sdkIntegrityVerifier)
        : this(
            buildPolicy,
            sdkIntegrityVerifier,
            new RekallAgeModuleBuildReceiptService(),
            DefaultBuildTimeout,
            Process.Start)
    {
    }

    internal BuildModulesCommand(
        RekallAgeModuleBuildPolicy buildPolicy,
        RekallAgeModuleSdkIntegrityVerifier sdkIntegrityVerifier,
        RekallAgeModuleBuildReceiptService receiptService)
        : this(
            buildPolicy,
            sdkIntegrityVerifier,
            receiptService,
            DefaultBuildTimeout,
            Process.Start)
    {
    }

    internal BuildModulesCommand(
        TimeSpan buildTimeout,
        Func<ProcessStartInfo, Process?> processStarter)
        : this(
            new RekallAgeModuleBuildPolicy(),
            new RekallAgeModuleSdkIntegrityVerifier(),
            new RekallAgeModuleBuildReceiptService(),
            buildTimeout,
            processStarter)
    {
    }

    private BuildModulesCommand(
        RekallAgeModuleBuildPolicy buildPolicy,
        RekallAgeModuleSdkIntegrityVerifier sdkIntegrityVerifier,
        RekallAgeModuleBuildReceiptService receiptService,
        TimeSpan buildTimeout,
        Func<ProcessStartInfo, Process?> processStarter)
    {
        _buildPolicy = buildPolicy;
        _sdkIntegrityVerifier = sdkIntegrityVerifier;
        _receiptService = receiptService;
        if (buildTimeout <= TimeSpan.Zero || buildTimeout > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(buildTimeout),
                "Module build timeout must be greater than zero and no more than 30 minutes.");
        }
        _buildTimeout = buildTimeout;
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    public string Name => "rekall.build.modules";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Builds C# module projects under a Rekall AGE project.",
        typeof(BuildModulesRequest).FullName!,
        typeof(BuildModulesResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<BuildModulesResult>> ExecuteAsync(
        BuildModulesRequest request,
        RekallAgeCommandContext context)
    {
        var policy = _buildPolicy.Inspect(request.ProjectRoot);
        var results = new List<BuildModuleResult>();
        if (!policy.Ready)
        {
            return RekallAgeCommandResult<BuildModulesResult>.Failure(
                new BuildModulesResult(results),
                "Module build policy rejected one or more projects before compilation.",
                policy.Issues
                    .Select(issue => new RekallAgeCommandError(
                        "REKALL_MODULE_BUILD_POLICY_REJECTED",
                        issue.Message,
                        issue.Target))
                    .ToArray());
        }

        if (policy.Candidates.Count > 0)
        {
            var sdkIntegrity = _sdkIntegrityVerifier.Verify(request.ProjectRoot);
            if (!sdkIntegrity.Ready)
            {
                return RekallAgeCommandResult<BuildModulesResult>.Failure(
                    new BuildModulesResult(results),
                    "Project-local module SDK integrity verification failed before compilation.",
                    sdkIntegrity.Issues
                        .Select(issue => new RekallAgeCommandError(
                            "REKALL_MODULE_SDK_INTEGRITY_FAILED",
                            issue.Message,
                            issue.Target))
                        .ToArray());
            }
        }

        foreach (var candidate in policy.Candidates)
        {
            string sourceFingerprint;
            try
            {
                sourceFingerprint = _receiptService.CaptureSourceFingerprint(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return RekallAgeCommandResult<BuildModulesResult>.Failure(
                    new BuildModulesResult(results),
                    "Module source could not be fingerprinted safely before compilation.",
                    [new RekallAgeCommandError(
                        "REKALL_MODULE_SOURCE_FINGERPRINT_FAILED",
                        ex.Message,
                        candidate.ModuleDirectory)]);
            }
            ResetVerifiedGeneratedDirectory(candidate, candidate.OutputDirectory);
            ResetVerifiedGeneratedDirectory(candidate, candidate.IntermediateDirectory);
            var result = await BuildProjectAsync(candidate, context.CancellationToken);
            if (result.Succeeded)
            {
                string receiptPath;
                try
                {
                    receiptPath = await _receiptService.WriteAsync(
                        request.ProjectRoot,
                        candidate,
                        sourceFingerprint,
                        context.CancellationToken);
                }
                catch (RekallAgeModuleReceiptException ex)
                {
                    results.Add(result with { Succeeded = false, Output = ex.Message });
                    return RekallAgeCommandResult<BuildModulesResult>.Failure(
                        new BuildModulesResult(results),
                        "A bounded module build receipt could not be issued.",
                        [new RekallAgeCommandError(ex.Code, ex.Message, ex.Target)]);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    results.Add(result with { Succeeded = false, Output = ex.Message });
                    return RekallAgeCommandResult<BuildModulesResult>.Failure(
                        new BuildModulesResult(results),
                        "A bounded module build receipt could not be written safely.",
                        [new RekallAgeCommandError(
                            "REKALL_MODULE_RECEIPT_WRITE_FAILED",
                            ex.Message,
                            candidate.OutputDirectory)]);
                }
                result = result with
                {
                    ReceiptPath = receiptPath,
                    TrustPosture = RekallAgeModuleTrustPostures.WindowsAppContainerRestricted
                };
            }
            results.Add(result);
            if (result.Succeeded)
            {
                context.Transaction.RecordChangedResource(result.AssemblyPath);
                context.Transaction.RecordChangedResource(result.ReceiptPath);
            }
        }

        var value = new BuildModulesResult(results);
        if (results.Count == 0)
        {
            var scaffold = new RekallAgeSuggestedCommand(
                "rekall.module.scaffold_playable",
                new Dictionary<string, object?>
                {
                    ["projectRoot"] = request.ProjectRoot,
                    ["moduleId"] = "game.playable",
                    ["displayName"] = "Agent Authored Playable",
                    ["moduleName"] = "Playable"
                });
            return RekallAgeCommandResult<BuildModulesResult>.Failure(
                value,
                "No module projects were found.",
                [new RekallAgeCommandError(
                    "REKALL_MODULE_PROJECTS_MISSING",
                    "No module projects were found. Execute the suggested playable scaffold, author behavior if needed, then rebuild modules.",
                    request.ProjectRoot,
                    [scaffold])]);
        }

        if (results.Any(result => !result.Succeeded))
        {
            return RekallAgeCommandResult<BuildModulesResult>.Failure(
                value,
                "One or more module projects failed to build.",
                results
                    .Where(result => !result.Succeeded)
                    .Select(result => new RekallAgeCommandError(
                        result.TimedOut ? "REKALL_MODULE_BUILD_TIMEOUT" : "REKALL_MODULE_BUILD_FAILED",
                        result.Output,
                        result.ProjectPath))
                    .ToArray());
        }

        return RekallAgeCommandResult<BuildModulesResult>.Success(
            value,
            $"Built {results.Count} module project(s).");
    }

    private static void ResetVerifiedGeneratedDirectory(
        RekallAgeModuleBuildCandidate candidate,
        string directory)
    {
        var output = Path.GetFullPath(directory);
        var module = Path.GetFullPath(candidate.ModuleDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!output.StartsWith(module + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"Verified module output '{output}' is outside '{module}'.");
        }

        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private async Task<BuildModuleResult> BuildProjectAsync(
        RekallAgeModuleBuildCandidate candidate,
        CancellationToken cancellationToken)
    {
        var projectPath = candidate.ProjectPath;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-v:minimal");
        startInfo.ArgumentList.Add("/nr:false");
        startInfo.ArgumentList.Add("-p:ImportDirectoryBuildProps=false");
        startInfo.ArgumentList.Add("-p:ImportDirectoryBuildTargets=false");
        var portableSdkProject = IsPortableSdkProject(projectPath);
        if (portableSdkProject)
        {
            startInfo.ArgumentList.Add("-p:BaseIntermediateOutputPath=obj/rekall/");
            startInfo.ArgumentList.Add("-p:OutputPath=bin/rekall/net10.0/");
        }

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = _processStarter(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet build.");
        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(_buildTimeout);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(wait.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            await TerminateProcessTreeAsync(process);
        }
        catch (OperationCanceledException)
        {
            await TerminateProcessTreeAsync(process);
            throw;
        }

        var output = await ReadCompilerOutputAsync(outputTask, errorTask);
        if (timedOut)
        {
            output = string.Concat(
                output,
                output.Length == 0 || output.EndsWith('\n') ? string.Empty : Environment.NewLine,
                $"Module compilation exceeded the {_buildTimeout} deadline and its process tree was terminated.");
        }

        var moduleName = candidate.ModuleName;
        var assemblyPath = portableSdkProject
            ? Path.Combine(candidate.OutputDirectory, $"{moduleName}.dll")
            : Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", "Debug", "net10.0", $"{moduleName}.dll");
        var sdkVersion = ReadSdkVersion(projectPath);

        return new BuildModuleResult(
            moduleName,
            projectPath,
            assemblyPath,
            !timedOut && process.ExitCode == 0 && File.Exists(assemblyPath),
            output,
            timedOut ? -1 : process.ExitCode,
            sdkVersion)
        {
            TimedOut = timedOut
        };
    }

    private static async Task<string> ReadCompilerOutputAsync(
        Task<string> outputTask,
        Task<string> errorTask)
    {
        try
        {
            await Task.WhenAll(outputTask, errorTask).WaitAsync(ProcessCleanupTimeout);
            return await outputTask + await errorTask;
        }
        catch (TimeoutException)
        {
            return "Compiler output streams did not close within the bounded cleanup deadline.";
        }
    }

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(ProcessCleanupTimeout);
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException)
        {
            // The command still returns bounded timeout/cancellation evidence.
        }
    }

    private static bool IsPortableSdkProject(string projectPath)
    {
        return File.ReadAllText(projectPath).Contains(
            "Rekall.Age.Sdk.props",
            StringComparison.Ordinal);
    }

    private static string ReadSdkVersion(string projectPath)
    {
        var moduleDirectory = new DirectoryInfo(Path.GetDirectoryName(projectPath)!);
        var projectRoot = moduleDirectory.Parent?.Parent;
        if (projectRoot is null)
        {
            return "unknown";
        }

        var sdkRoot = Path.Combine(projectRoot.FullName, ".rekall", "sdk");
        if (!Directory.Exists(sdkRoot))
        {
            return "unknown";
        }

        foreach (var manifestPath in Directory.EnumerateFiles(sdkRoot, "rekall.sdk.json", SearchOption.AllDirectories))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (document.RootElement.TryGetProperty("compatibilityVersion", out var version))
                {
                    return version.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (JsonException)
            {
                return "invalid";
            }
        }

        return "unknown";
    }
}
