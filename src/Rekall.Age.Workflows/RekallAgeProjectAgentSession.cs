using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Mcp;

namespace Rekall.Age.Workflows;

public sealed record RekallAgeProjectAgentSessionRequest(
    string ProjectRoot,
    string SceneName,
    string Model,
    string Task)
{
    public int? MaxTurns { get; init; }
    public string? Think { get; init; }
    public double? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public TimeSpan? MaxTurnDuration { get; init; }
    public bool RequireCompletionAudit { get; init; } = true;
    public bool RequireCompletionAuditToolEvidence { get; init; }
    public bool TreatGauntletAsTerminalSuccess { get; init; }
}

public sealed record RekallAgeProjectAgentSessionResult(
    bool Succeeded,
    string Summary,
    RekallAgeLanguageModelAgentResult AgentResult);

public sealed class RekallAgeProjectAgentSession
{
    private readonly IRekallAgeLanguageModelClient _modelClient;
    private readonly RekallAgeCommandRegistry _registry;
    private readonly string _actorId;

    public RekallAgeProjectAgentSession(
        IRekallAgeLanguageModelClient modelClient,
        RekallAgeCommandRegistry registry,
        string actorId = "rekall-studio-agent")
    {
        _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        _actorId = actorId;
    }

    public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
        _modelClient.ListModelsAsync(cancellationToken);

    public async ValueTask<RekallAgeProjectAgentSessionResult> RunAsync(
        RekallAgeProjectAgentSessionRequest request,
        IProgress<RekallAgeLanguageModelAgentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SceneName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Task);

        var projectRoot = Path.GetFullPath(request.ProjectRoot);
        var hasExistingRuntimeSystem = HasExistingRuntimeSystemSource(projectRoot);
        var tools = new ProjectScopedToolExecutor(
            projectRoot,
            request.SceneName,
            _registry,
            new RekallAgeMcpAgentToolExecutor(_registry, _actorId, progressiveDiscovery: true));
        var agent = new RekallAgeLanguageModelAgent(_modelClient, tools);
        var scopedTask = RekallAgeAgentTaskComposer.Compose(projectRoot, request.SceneName, request.Task);
        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest(
                request.Model,
                RekallAgeEmbeddedAgentContract.SystemPrompt,
                scopedTask)
            {
                MaxTurns = request.MaxTurns,
                Think = request.Think,
                Temperature = request.Temperature,
                MaxOutputTokens = request.MaxOutputTokens,
                MaxTurnDuration = request.MaxTurnDuration,
                RequireCompletionAudit = request.RequireCompletionAudit,
                RequireCompletionAuditToolEvidence = request.RequireCompletionAuditToolEvidence,
                RequireTaskSpecificEvidence = request.RequireCompletionAuditToolEvidence,
                RequireRuntimeBehaviorAssertions = !request.TreatGauntletAsTerminalSuccess,
                RuntimeAuthoringCheckpointSatisfied = hasExistingRuntimeSystem,
                Progress = progress,
                CompletionAuditPrimingTools = new HashSet<string>(
                    ["rekall.workflow.audit_playable_package"],
                    StringComparer.Ordinal),
                TerminalSuccessTools = request.TreatGauntletAsTerminalSuccess
                    ? new HashSet<string>(["rekall.workflow.agent_authoring_gauntlet"], StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal)
            },
            cancellationToken);
        var failedTools = result.ToolExecutions.Count(execution => !execution.Succeeded);
        var securityBoundaryFailures = result.ToolExecutions.Count(execution =>
            !execution.Succeeded
            && execution.ResultPreview.Contains(
                "REKALL_AGENT_PROJECT_SCOPE_VIOLATION",
                StringComparison.Ordinal));
        var succeeded = result.Completed
            && securityBoundaryFailures == 0
            && (request.RequireCompletionAudit || failedTools == 0);
        var summary = succeeded
            ? failedTools == 0
                ? $"AI authoring completed in {result.Turns} turns with {result.ToolCallCount} tool calls."
                : $"AI authoring completed its evidence audit after recovering from {failedTools} failed tool call{(failedTools == 1 ? string.Empty : "s")}."
            : result.Completed
                ? $"AI authoring stopped with {failedTools} failed tool call{(failedTools == 1 ? string.Empty : "s")}."
                : $"AI authoring stopped: {result.StopReason}.";
        return new RekallAgeProjectAgentSessionResult(succeeded, summary, result);
    }

    private static bool HasExistingRuntimeSystemSource(string projectRoot)
    {
        var normalizedProjectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var modulesRoot = Path.GetFullPath(Path.Combine(normalizedProjectRoot, "Modules"));
        if (!Directory.Exists(modulesRoot))
        {
            return false;
        }

        const int maxModuleDirectories = 256;
        const int maxSourceFilesPerModule = 256;
        const long maxSourceBytes = 1_048_576;
        try
        {
            if (!IsContainedPath(modulesRoot, normalizedProjectRoot)
                || (File.GetAttributes(modulesRoot) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            foreach (var moduleDirectory in Directory.EnumerateDirectories(modulesRoot).Take(maxModuleDirectories))
            {
                var normalizedModuleDirectory = Path.GetFullPath(moduleDirectory);
                if (!IsContainedPath(normalizedModuleDirectory, modulesRoot)
                    || (File.GetAttributes(normalizedModuleDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                foreach (var sourcePath in Directory.EnumerateFiles(normalizedModuleDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                    .Take(maxSourceFilesPerModule))
                {
                    var normalizedSourcePath = Path.GetFullPath(sourcePath);
                    if (!IsContainedPath(normalizedSourcePath, normalizedModuleDirectory))
                    {
                        continue;
                    }

                    var sourceInfo = new FileInfo(normalizedSourcePath);
                    if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0
                        || sourceInfo.Length <= 0
                        || sourceInfo.Length > maxSourceBytes)
                    {
                        continue;
                    }

                    if (DeclaresRuntimeSystem(File.ReadAllText(normalizedSourcePath)))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException)
        {
            return false;
        }

        return false;
    }

    private static bool DeclaresRuntimeSystem(string source)
    {
        var code = MaskCommentsAndLiterals(source);
        var searchFrom = 0;
        while (searchFrom < code.Length)
        {
            var classIndex = code.IndexOf("class", searchFrom, StringComparison.Ordinal);
            if (classIndex < 0)
            {
                return false;
            }
            searchFrom = classIndex + "class".Length;
            if (!IsWordAt(code, classIndex, "class"))
            {
                continue;
            }

            var angleDepth = 0;
            var baseListStart = -1;
            var declarationEnd = code.Length;
            for (var index = searchFrom; index < code.Length; index++)
            {
                switch (code[index])
                {
                    case '<':
                        angleDepth++;
                        break;
                    case '>' when angleDepth > 0:
                        angleDepth--;
                        break;
                    case ':' when angleDepth == 0 && baseListStart < 0:
                        baseListStart = index + 1;
                        break;
                    case '{' or ';' when angleDepth == 0:
                        declarationEnd = index;
                        index = code.Length;
                        break;
                    default:
                        if (angleDepth == 0 && IsWordAt(code, index, "where"))
                        {
                            declarationEnd = index;
                            index = code.Length;
                        }
                        break;
                }
            }

            if (baseListStart >= 0
                && baseListStart < declarationEnd
                && HasDirectRuntimeSystemBase(code.AsSpan(baseListStart, declarationEnd - baseListStart)))
            {
                return true;
            }
            searchFrom = Math.Max(searchFrom, declarationEnd + 1);
        }

        return false;
    }

    private static bool HasDirectRuntimeSystemBase(ReadOnlySpan<char> baseList)
    {
        var angleDepth = 0;
        var itemStart = 0;
        for (var index = 0; index <= baseList.Length; index++)
        {
            if (index < baseList.Length)
            {
                if (baseList[index] == '<') angleDepth++;
                else if (baseList[index] == '>' && angleDepth > 0) angleDepth--;
            }

            if (index != baseList.Length && (baseList[index] != ',' || angleDepth != 0))
            {
                continue;
            }

            var candidate = baseList[itemStart..index].Trim();
            if (!candidate.Contains('<')
                && !candidate.Contains('>')
                && DirectBaseTerminalName(candidate).SequenceEqual("IRekallAgeRuntimeModuleSystem"))
            {
                return true;
            }
            itemStart = index + 1;
        }

        return false;
    }

    private static ReadOnlySpan<char> DirectBaseTerminalName(ReadOnlySpan<char> candidate)
    {
        candidate = candidate.Trim();
        var lastDot = candidate.LastIndexOf('.');
        var lastAlias = candidate.LastIndexOf("::".AsSpan());
        var separator = Math.Max(lastDot, lastAlias < 0 ? -1 : lastAlias + 1);
        return candidate[(separator + 1)..].Trim();
    }

    private static bool IsWordAt(string source, int index, string word)
    {
        if (index < 0
            || index + word.Length > source.Length
            || !source.AsSpan(index, word.Length).SequenceEqual(word))
        {
            return false;
        }

        return (index == 0 || !IsIdentifierCharacter(source[index - 1]))
            && (index + word.Length == source.Length || !IsIdentifierCharacter(source[index + word.Length]));
    }

    private static bool IsIdentifierCharacter(char value) =>
        value == '_' || char.IsLetterOrDigit(value);

    private static string MaskCommentsAndLiterals(string source)
    {
        var masked = source.ToCharArray();
        static void Blank(char[] value, int index)
        {
            if (value[index] is not '\r' and not '\n') value[index] = ' ';
        }

        for (var index = 0; index < source.Length;)
        {
            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                Blank(masked, index++);
                Blank(masked, index++);
                while (index < source.Length && source[index] is not '\r' and not '\n') Blank(masked, index++);
                continue;
            }
            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                Blank(masked, index++);
                Blank(masked, index++);
                while (index < source.Length)
                {
                    if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        Blank(masked, index++);
                        Blank(masked, index++);
                        break;
                    }
                    Blank(masked, index++);
                }
                continue;
            }
            if (source[index] == '"')
            {
                var quoteCount = 1;
                while (index + quoteCount < source.Length && source[index + quoteCount] == '"') quoteCount++;
                if (quoteCount >= 3)
                {
                    for (var count = 0; count < quoteCount; count++) Blank(masked, index++);
                    while (index < source.Length)
                    {
                        var closingQuotes = 0;
                        while (index + closingQuotes < source.Length && source[index + closingQuotes] == '"') closingQuotes++;
                        if (closingQuotes >= quoteCount)
                        {
                            for (var count = 0; count < quoteCount; count++) Blank(masked, index++);
                            break;
                        }
                        Blank(masked, index++);
                    }
                    continue;
                }

                var verbatim = index > 0 && source[index - 1] == '@';
                Blank(masked, index++);
                while (index < source.Length)
                {
                    if (!verbatim && source[index] == '\\' && index + 1 < source.Length)
                    {
                        Blank(masked, index++);
                        Blank(masked, index++);
                        continue;
                    }
                    if (source[index] == '"')
                    {
                        Blank(masked, index++);
                        if (verbatim && index < source.Length && source[index] == '"')
                        {
                            Blank(masked, index++);
                            continue;
                        }
                        break;
                    }
                    Blank(masked, index++);
                }
                continue;
            }
            if (source[index] == '\'')
            {
                Blank(masked, index++);
                while (index < source.Length)
                {
                    if (source[index] == '\\' && index + 1 < source.Length)
                    {
                        Blank(masked, index++);
                        Blank(masked, index++);
                        continue;
                    }
                    var closes = source[index] == '\'';
                    Blank(masked, index++);
                    if (closes) break;
                }
                continue;
            }
            index++;
        }

        return new string(masked);
    }

    private static bool IsContainedPath(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (candidate.Equals(root, comparison))
        {
            return true;
        }

        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, comparison);
    }

    private sealed class ProjectScopedToolExecutor(
        string projectRoot,
        string sceneName,
        RekallAgeCommandRegistry registry,
        IRekallAgeAgentToolExecutor inner) : IRekallAgeAgentToolExecutor
    {
        private readonly RekallAgeProjectCommandScope _scope = new(projectRoot, registry, sceneName);

        public IReadOnlyList<RekallAgeLanguageModelTool> Tools => inner.Tools;

        public ValueTask<JsonNode> ExecuteAsync(
            string name,
            JsonObject arguments,
            CancellationToken cancellationToken)
        {
            var scoped = _scope.Apply(name, arguments);
            if (!scoped.Succeeded)
            {
                var errors = new JsonArray();
                foreach (var error in scoped.Errors)
                {
                    errors.Add(new JsonObject
                    {
                        ["code"] = error.Code,
                        ["message"] = error.Message,
                        ["target"] = error.Target
                    });
                }

                return ValueTask.FromResult<JsonNode>(new JsonObject
                {
                    ["ok"] = false,
                    ["summary"] = scoped.ErrorSummary,
                    ["errors"] = errors
                });
            }

            return inner.ExecuteAsync(name, scoped.Arguments, cancellationToken);
        }
    }
}
