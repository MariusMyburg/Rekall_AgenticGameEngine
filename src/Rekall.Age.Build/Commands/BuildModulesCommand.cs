using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly Regex DiscardedWorldMutation = new(
        @"^\s*(?<receiver>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<method>AddEntity|RemoveEntity|ReplaceEntity|UpdateEntity|UpdateEntitiesWithTag|UpdateEntitiesWithComponent|UpdateEntitiesWithTagAndComponent)\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AssignedWorldMutation = new(
        @"^[\t ]*(?:(?<declaration>var|RekallAgeRuntimeWorld)\s+)?(?<target>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<receiver>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<method>AddEntity|RemoveEntity|ReplaceEntity|UpdateEntity|UpdateEntitiesWithTag|UpdateEntitiesWithComponent|UpdateEntitiesWithTagAndComponent)\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

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

        foreach (var candidate in policy.Candidates)
        {
            foreach (var sourcePath in candidate.SourcePaths)
            {
                var source = await File.ReadAllTextAsync(sourcePath, context.CancellationToken);
                var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                for (var index = 0; index < lines.Length; index++)
                {
                    var match = DiscardedWorldMutation.Match(lines[index]);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var receiver = match.Groups["receiver"].Value;
                    var method = match.Groups["method"].Value;
                    var message =
                        $"Immutable runtime world mutation '{receiver}.{method}(...)' is discarded at line {index + 1}. "
                        + $"Persist the returned world, for example: {receiver} = {receiver}.{method}(...). "
                        + "All RekallAgeRuntimeWorld mutation helpers return replacements; calling one without assignment, return, or another consuming expression is a gameplay no-op.";
                    return RekallAgeCommandResult<BuildModulesResult>.Failure(
                        new BuildModulesResult(results),
                        "A runtime module discards an immutable world mutation.",
                        [new RekallAgeCommandError(
                            "REKALL_MODULE_IMMUTABLE_MUTATION_DISCARDED",
                            message,
                            $"{sourcePath}:{index + 1}",
                            [new RekallAgeSuggestedCommand(
                                "rekall.module.read_source",
                                new Dictionary<string, object?>
                                {
                                    ["projectRoot"] = request.ProjectRoot,
                                    ["moduleName"] = candidate.ModuleName,
                                    ["fileName"] = Path.GetFileName(sourcePath)
                                })])]);
                }

                var lineageIssue = InspectImmutableWorldLineage(source);
                if (lineageIssue is not null)
                {
                    return RekallAgeCommandResult<BuildModulesResult>.Failure(
                        new BuildModulesResult(results),
                        lineageIssue.Summary,
                        [new RekallAgeCommandError(
                            lineageIssue.Code,
                            lineageIssue.Message,
                            $"{sourcePath}:{lineageIssue.LineNumber}",
                            [new RekallAgeSuggestedCommand(
                                "rekall.module.read_source",
                                new Dictionary<string, object?>
                                {
                                    ["projectRoot"] = request.ProjectRoot,
                                    ["moduleName"] = candidate.ModuleName,
                                    ["fileName"] = Path.GetFileName(sourcePath)
                                })])]);
                }
            }
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
                            issue.Target,
                            [new RekallAgeSuggestedCommand(
                                "rekall.module.install_sdk",
                                new Dictionary<string, object?>
                                {
                                    ["projectRoot"] = request.ProjectRoot
                                })]))
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
                    .Select(result => CreateBuildFailureError(request.ProjectRoot, result))
                    .ToArray());
        }

        return RekallAgeCommandResult<BuildModulesResult>.Success(
            value,
            $"Built {results.Count} module project(s).");
    }

    private static ImmutableWorldLineageIssue? InspectImmutableWorldLineage(string source)
    {
        var maskedSource = MaskNonCode(source);
        var assignments = AssignedWorldMutation.Matches(maskedSource)
            .Cast<Match>()
            .Select(match => new WorldMutationAssignment(
                match,
                match.Groups["target"].Value,
                match.Groups["receiver"].Value,
                match.Groups["method"].Value,
                match.Groups["declaration"].Success,
                GetLineNumber(maskedSource, match.Index)))
            .ToArray();
        var establishedWorlds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assignment in assignments)
        {
            if (!assignment.IsDeclaration
                && establishedWorlds.Contains(assignment.Target)
                && !string.Equals(assignment.Target, assignment.Receiver, StringComparison.Ordinal))
            {
                return new ImmutableWorldLineageIssue(
                    "REKALL_MODULE_IMMUTABLE_WORLD_STALE_BASE",
                    "A runtime module rebuilds an already-mutated immutable world from a stale base.",
                    $"Immutable world '{assignment.Target}' is reassigned from stale base "
                    + $"'{assignment.Receiver}' at line {assignment.LineNumber}, which discards an earlier mutation. "
                    + $"Continue the current lineage instead: {assignment.Target} = {assignment.Target}.{assignment.Method}(...).",
                    assignment.LineNumber);
            }

            establishedWorlds.Add(assignment.Target);
        }

        foreach (var outer in assignments)
        {
            var openParenthesis = outer.Match.Index + outer.Match.Length - 1;
            var closeParenthesis = FindMatchingParenthesis(maskedSource, openParenthesis);
            if (closeParenthesis < 0)
            {
                continue;
            }

            var callbackArrow = maskedSource.IndexOf("=>", openParenthesis, StringComparison.Ordinal);
            if (callbackArrow < 0 || callbackArrow > closeParenthesis)
            {
                continue;
            }

            var worldsEstablishedBeforeCallback = assignments
                .Where(candidate => candidate.Match.Index < outer.Match.Index)
                .Select(candidate => candidate.Target)
                .ToHashSet(StringComparer.Ordinal);
            var nested = assignments.FirstOrDefault(candidate =>
                candidate.Match.Index > callbackArrow
                && candidate.Match.Index < closeParenthesis
                && !candidate.IsDeclaration
                && worldsEstablishedBeforeCallback.Contains(candidate.Target));
            if (nested is null)
            {
                continue;
            }

            return new ImmutableWorldLineageIssue(
                "REKALL_MODULE_IMMUTABLE_WORLD_NESTED_MUTATION",
                "A runtime module mutates an outer immutable world from inside an entity-update callback.",
                $"Immutable world '{nested.Target}' is reassigned inside the {outer.Method} callback at line "
                + $"{nested.LineNumber}. The enclosing immutable update can overwrite that nested result. "
                + "Return only the entity from the callback, then perform world mutations sequentially outside it.",
                nested.LineNumber);
        }

        return null;
    }

    private static int FindMatchingParenthesis(string source, int openParenthesis)
    {
        var depth = 0;
        for (var index = openParenthesis; index < source.Length; index++)
        {
            if (source[index] == '(')
            {
                depth++;
            }
            else if (source[index] == ')' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static int GetLineNumber(string source, int offset)
    {
        var line = 1;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string MaskNonCode(string source)
    {
        var masked = source.ToCharArray();
        var state = SourceMaskState.Code;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (state == SourceMaskState.Code)
            {
                if (current == '/' && next == '/')
                {
                    masked[index] = masked[index + 1] = ' ';
                    index++;
                    state = SourceMaskState.LineComment;
                }
                else if (current == '/' && next == '*')
                {
                    masked[index] = masked[index + 1] = ' ';
                    index++;
                    state = SourceMaskState.BlockComment;
                }
                else if (current == '"')
                {
                    masked[index] = ' ';
                    state = index > 0 && source[index - 1] == '@'
                        ? SourceMaskState.VerbatimString
                        : SourceMaskState.String;
                }
                else if (current == '\'')
                {
                    masked[index] = ' ';
                    state = SourceMaskState.Character;
                }

                continue;
            }

            if (current is not ('\r' or '\n'))
            {
                masked[index] = ' ';
            }

            switch (state)
            {
                case SourceMaskState.LineComment when current == '\n':
                    state = SourceMaskState.Code;
                    break;
                case SourceMaskState.BlockComment when current == '*' && next == '/':
                    masked[index + 1] = ' ';
                    index++;
                    state = SourceMaskState.Code;
                    break;
                case SourceMaskState.String when current == '\\':
                case SourceMaskState.Character when current == '\\':
                    if (index + 1 < source.Length)
                    {
                        masked[index + 1] = source[index + 1] is '\r' or '\n' ? source[index + 1] : ' ';
                        index++;
                    }
                    break;
                case SourceMaskState.String when current == '"':
                    state = SourceMaskState.Code;
                    break;
                case SourceMaskState.Character when current == '\'':
                    state = SourceMaskState.Code;
                    break;
                case SourceMaskState.VerbatimString when current == '"' && next == '"':
                    masked[index + 1] = ' ';
                    index++;
                    break;
                case SourceMaskState.VerbatimString when current == '"':
                    state = SourceMaskState.Code;
                    break;
            }
        }

        return new string(masked);
    }

    private sealed record WorldMutationAssignment(
        Match Match,
        string Target,
        string Receiver,
        string Method,
        bool IsDeclaration,
        int LineNumber);

    private sealed record ImmutableWorldLineageIssue(
        string Code,
        string Summary,
        string Message,
        int LineNumber);

    private enum SourceMaskState
    {
        Code,
        LineComment,
        BlockComment,
        String,
        VerbatimString,
        Character
    }

    private static RekallAgeCommandError CreateBuildFailureError(
        string projectRoot,
        BuildModuleResult result)
    {
        if (result.TimedOut || !IsRuntimeSdkCompilerFailure(result.Output))
        {
            return new RekallAgeCommandError(
                result.TimedOut ? "REKALL_MODULE_BUILD_TIMEOUT" : "REKALL_MODULE_BUILD_FAILED",
                result.Output,
                result.ProjectPath);
        }

        const string query =
            "entity query entity transform Position3D immutable vector ComponentNumber ComponentBoolean ComponentString WithComponentNumber WithComponentBoolean WithComponentString UpdateEntity WithPosition3D runtime world";
        var recovery =
            "REKALL_RUNTIME_SDK_COMPILER_RECOVERY: Preserve the scaffolded SDK topology and repair against these exact immutable patterns:\n"
            + "- Select one entity: var entity = world.FindEntity(\"Player\"); then null-check it; EntitiesNamed returns a list of case-insensitive exact-name matches, never prefix matches. Query numbered/grouped objects with EntitiesWithComponent or EntitiesWithTag.\n"
            + "- Read transform: var position = entity.Transform.Position3D;\n"
            + "- Read authored state: var value = entity.ComponentNumber(componentType, \"value\", 0); use ComponentBoolean/ComponentString for those kinds.\n"
            + "- Read authored booleans: var enabled = entity.ComponentBoolean(componentType, \"enabled\", false); Boolean helpers use bool values directly, never compare them with 0 or 1.\n"
            + "- Write authored booleans: entity = entity.WithComponentBoolean(componentType, \"enabled\", true); pass true/false, never 1/0.\n"
            + "- Read a pressed semantic action: var resetPressed = world.WasInputActionPressed(\"reset\"); alternatively use world.InputActionValue(\"reset\", 0) > 0 because InputActionValue and its fallback are double.\n"
            + "- Replace position: var updated = entity.WithPosition3D(new RekallAgeRuntimeVector3(x, y, z));\n"
            + "- Persist replacement: world = world.UpdateEntity(entity.Id, current => current.WithPosition3D(new RekallAgeRuntimeVector3(x, y, z)));\n"
            + "These are extension-call shapes on the entity/world. Do not invent RekallAgeRuntimeModuleSdk.GetTransform3D, ReadTransform3D, Transform3D, GetComponentNumber, or a two-argument WithPosition3D. Inspect the compiled SDK with the populated suggested command, read the existing source, make one targeted repair, and rebuild.\n\n"
            + "Compiler diagnostics:\n"
            + result.Output;
        return new RekallAgeCommandError(
            "REKALL_MODULE_BUILD_FAILED",
            recovery,
            result.ProjectPath,
            [
                new RekallAgeSuggestedCommand(
                    "rekall.module.inspect_runtime_sdk",
                    new Dictionary<string, object?>
                    {
                        ["query"] = query,
                        ["limit"] = 24
                    }),
                new RekallAgeSuggestedCommand(
                    "rekall.module.list_sources",
                    new Dictionary<string, object?>
                    {
                        ["projectRoot"] = projectRoot
                    })
            ]);
    }

    private static bool IsRuntimeSdkCompilerFailure(string output) =>
        output.Contains("RekallAgeRuntime", StringComparison.Ordinal)
        || output.Contains("ComponentNumber", StringComparison.Ordinal)
        || output.Contains("ComponentBoolean", StringComparison.Ordinal)
        || output.Contains("ComponentString", StringComparison.Ordinal)
        || output.Contains("WithPosition3D", StringComparison.Ordinal)
        || output.Contains("Position3D", StringComparison.Ordinal)
        || output.Contains("operands of type 'bool' and 'int'", StringComparison.Ordinal)
        || output.Contains("cannot convert from 'bool' to 'double'", StringComparison.Ordinal)
        || output.Contains("cannot convert from 'int' to 'bool'", StringComparison.Ordinal);

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
