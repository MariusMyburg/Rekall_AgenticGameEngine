using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record WriteShaderIncludeRequest(
    string ProjectRoot,
    string Name,
    string Source,
    bool Overwrite = true);

public sealed record WriteShaderIncludeResult(
    string Name,
    string Path,
    string RelativePath,
    bool Written);

public sealed class WriteShaderIncludeCommand
    : IRekallAgeCommand<WriteShaderIncludeRequest, WriteShaderIncludeResult>
{
    public string Name => "rekall.shader.write_include";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Writes a reusable project GLSL include under Shaders as .glslinc. Entry shaders consume it with a full-line #include \"relative/path.glslinc\" directive.",
        typeof(WriteShaderIncludeRequest).FullName!,
        typeof(WriteShaderIncludeResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<WriteShaderIncludeResult>> ExecuteAsync(
        WriteShaderIncludeRequest request,
        RekallAgeCommandContext context)
    {
        if (request.Source is null)
        {
            return Failure(request, "REKALL_SHADER_SOURCE_REQUIRED", "Shader include source is required.");
        }

        if (!ShaderSourcePaths.TryResolveProjectPath(request.ProjectRoot, request.Name, "include", out var resolved, out var errors))
        {
            return RekallAgeCommandResult<WriteShaderIncludeResult>.Failure(
                new(request.Name, string.Empty, string.Empty, false),
                "Shader include path is invalid.",
                errors);
        }

        if (!request.Overwrite && File.Exists(resolved.Path))
        {
            return Failure(request, "REKALL_SHADER_EXISTS", "Shader include already exists and overwrite is false.", resolved);
        }

        if (System.Text.Encoding.UTF8.GetByteCount(request.Source) > 1024 * 1024)
        {
            return Failure(request, "REKALL_SHADER_INCLUDE_SIZE_LIMIT", "Shader include exceeds 1 MiB.", resolved);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolved.Path)!);
        await File.WriteAllTextAsync(resolved.Path, request.Source, context.CancellationToken).ConfigureAwait(false);
        context.Transaction.RecordChangedResource(resolved.Path);
        return RekallAgeCommandResult<WriteShaderIncludeResult>.Success(
            new(resolved.Name, resolved.Path, resolved.RelativePath, true),
            $"Wrote shader include '{resolved.Name}'.");
    }

    private static RekallAgeCommandResult<WriteShaderIncludeResult> Failure(
        WriteShaderIncludeRequest request,
        string code,
        string message,
        ResolvedShaderPath? resolved = null) =>
        RekallAgeCommandResult<WriteShaderIncludeResult>.Failure(
            new(request.Name, resolved?.Path ?? string.Empty, resolved?.RelativePath ?? string.Empty, false),
            message,
            [new RekallAgeCommandError(code, message, request.Name)]);
}

public sealed record PreprocessShaderSourceRequest(
    string ProjectRoot,
    string Name,
    string Stage,
    string Scope = "project");

public sealed record PreprocessShaderSourceResult(
    string Name,
    string Stage,
    string Path,
    bool Success,
    string ExpandedSource,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<RekallAgeShaderPreprocessDiagnostic> Diagnostics);

public sealed class PreprocessShaderSourceCommand
    : IRekallAgeCommand<PreprocessShaderSourceRequest, PreprocessShaderSourceResult>
{
    public string Name => "rekall.shader.preprocess";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Expands a vertex or fragment shader's bounded .glslinc dependency graph and returns exact source plus stable diagnostics without assigning or running it.",
        typeof(PreprocessShaderSourceRequest).FullName!,
        typeof(PreprocessShaderSourceResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<PreprocessShaderSourceResult>> ExecuteAsync(
        PreprocessShaderSourceRequest request,
        RekallAgeCommandContext context)
    {
        if (!request.Scope.Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            return RekallAgeCommandResult<PreprocessShaderSourceResult>.Failure(
                new(request.Name, request.Stage, string.Empty, false, string.Empty, [], []),
                "Only project shaders can be preprocessed through the authoring API.",
                [new RekallAgeCommandError(
                    "REKALL_SHADER_SCOPE_INVALID",
                    "Shader preprocessing accepts scope 'project'; bundled engine shaders are immutable implementation resources.",
                    request.Scope)]);
        }

        IReadOnlyList<RekallAgeCommandError> pathErrors = [];
        if (!ShaderSourcePaths.IsSupportedStage(request.Stage)
            || !ShaderSourcePaths.TryResolveReadPath(
                request.ProjectRoot, request.Name, request.Stage, request.Scope, out var resolved, out pathErrors))
        {
            var errors = ShaderSourcePaths.IsSupportedStage(request.Stage)
                ? pathErrors
                : [new RekallAgeCommandError("REKALL_SHADER_STAGE_INVALID", "Shader stage must be 'vertex' or 'fragment'.", request.Stage)];
            return RekallAgeCommandResult<PreprocessShaderSourceResult>.Failure(
                new(request.Name, request.Stage, string.Empty, false, string.Empty, [], []),
                "Shader source could not be preprocessed.",
                errors);
        }

        var expansion = await new RekallAgeShaderPreprocessor()
            .ExpandFileAsync(request.ProjectRoot, resolved.Path, context.CancellationToken)
            .ConfigureAwait(false);
        var result = new PreprocessShaderSourceResult(
            resolved.Name,
            resolved.Stage,
            resolved.Path,
            expansion.Success,
            expansion.ExpandedSource,
            expansion.Dependencies,
            expansion.Diagnostics);
        return expansion.Success
            ? RekallAgeCommandResult<PreprocessShaderSourceResult>.Success(
                result,
                $"Preprocessed shader '{resolved.Name}' with {expansion.Dependencies.Count} include(s).")
            : RekallAgeCommandResult<PreprocessShaderSourceResult>.Failure(
                result,
                $"Shader preprocessing failed with {expansion.Diagnostics.Count} diagnostic(s).",
                expansion.Diagnostics.Select(diagnostic => new RekallAgeCommandError(
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Line > 0 ? $"{diagnostic.Path}:{diagnostic.Line}" : diagnostic.Path)).ToArray());
    }
}
