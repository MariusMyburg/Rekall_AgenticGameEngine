using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Rendering.Commands;

public sealed record ListShaderSourcesRequest(string ProjectRoot, bool IncludeEngineShaders = true);

public sealed record ShaderSourceSummary(
    string Name,
    string Stage,
    string Path,
    string RelativePath,
    string Scope,
    long ByteLength);

public sealed record ListShaderSourcesResult(IReadOnlyList<ShaderSourceSummary> Shaders);

public sealed record ReadShaderSourceRequest(string ProjectRoot, string Name, string Stage, string Scope = "project");

public sealed record ReadShaderSourceResult(
    string Name,
    string Stage,
    string Path,
    string RelativePath,
    string Scope,
    string Source);

public sealed record WriteShaderSourceRequest(
    string ProjectRoot,
    string Name,
    string Stage,
    string Source,
    bool ValidateBeforeWrite = true,
    bool Overwrite = true);

public sealed record WriteShaderSourceResult(
    string Name,
    string Stage,
    string Path,
    string RelativePath,
    bool Written,
    bool Validated,
    bool Compiled,
    IReadOnlyList<string> Errors);

public sealed record ValidateShaderSourceRequest(
    string ProjectRoot,
    string Name,
    string Stage,
    string? Source = null,
    string Scope = "project");

public sealed record ValidateShaderSourceResult(
    string Name,
    string Stage,
    string? Path,
    bool Compiled,
    int SpirvByteLength,
    IReadOnlyList<string> Errors);

public sealed record AssignShaderPipelineRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string VertexShader,
    string FragmentShader,
    bool ValidateBeforeAssign = true);

public sealed record AssignShaderPipelineResult(
    string EntityId,
    string VertexShader,
    string FragmentShader,
    bool Validated,
    IReadOnlyList<string> Errors,
    RekallAgeSceneDocument? Scene);

public sealed class ListShaderSourcesCommand : IRekallAgeCommand<ListShaderSourcesRequest, ListShaderSourcesResult>
{
    public string Name => "rekall.shader.list";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Lists project shader sources and bundled engine shader sources visible to agents.",
        typeof(ListShaderSourcesRequest).FullName!,
        typeof(ListShaderSourcesResult).FullName!);

    public ValueTask<RekallAgeCommandResult<ListShaderSourcesResult>> ExecuteAsync(
        ListShaderSourcesRequest request,
        RekallAgeCommandContext context)
    {
        var errors = ShaderSourcePaths.ValidateProjectRoot(request.ProjectRoot);
        if (errors.Count > 0)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<ListShaderSourcesResult>.Failure(
                new ListShaderSourcesResult([]),
                "Shader listing requires a valid project root.",
                errors));
        }

        var shaders = new List<ShaderSourceSummary>();
        shaders.AddRange(ShaderSourcePaths.ListProjectShaders(request.ProjectRoot));
        if (request.IncludeEngineShaders)
        {
            shaders.AddRange(ShaderSourcePaths.ListEngineShaders());
        }

        return ValueTask.FromResult(RekallAgeCommandResult<ListShaderSourcesResult>.Success(
            new ListShaderSourcesResult(shaders
                .OrderBy(shader => shader.Scope, StringComparer.Ordinal)
                .ThenBy(shader => shader.Name, StringComparer.Ordinal)
                .ThenBy(shader => shader.Stage, StringComparer.Ordinal)
                .ToArray()),
            $"Listed {shaders.Count} shader source(s)."));
    }
}

public sealed class ReadShaderSourceCommand : IRekallAgeCommand<ReadShaderSourceRequest, ReadShaderSourceResult>
{
    public string Name => "rekall.shader.read";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Reads a project or bundled engine shader source file.",
        typeof(ReadShaderSourceRequest).FullName!,
        typeof(ReadShaderSourceResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ReadShaderSourceResult>> ExecuteAsync(
        ReadShaderSourceRequest request,
        RekallAgeCommandContext context)
    {
        if (!ShaderSourcePaths.TryResolveReadPath(request.ProjectRoot, request.Name, request.Stage, request.Scope, out var resolved, out var errors))
        {
            return RekallAgeCommandResult<ReadShaderSourceResult>.Failure(
                EmptyRead(request),
                "Shader source could not be resolved.",
                errors);
        }

        var source = await File.ReadAllTextAsync(resolved.Path, context.CancellationToken);
        return RekallAgeCommandResult<ReadShaderSourceResult>.Success(
            new ReadShaderSourceResult(
                resolved.Name,
                resolved.Stage,
                resolved.Path,
                resolved.RelativePath,
                resolved.Scope,
                source),
            $"Read {resolved.Scope} shader '{resolved.Name}' ({resolved.Stage}).");
    }

    private static ReadShaderSourceResult EmptyRead(ReadShaderSourceRequest request)
    {
        return new ReadShaderSourceResult(request.Name, request.Stage, string.Empty, string.Empty, request.Scope, string.Empty);
    }
}

public sealed class WriteShaderSourceCommand : IRekallAgeCommand<WriteShaderSourceRequest, WriteShaderSourceResult>
{
    public string Name => "rekall.shader.write";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Writes a project shader source after optional GLSL validation.",
        typeof(WriteShaderSourceRequest).FullName!,
        typeof(WriteShaderSourceResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<WriteShaderSourceResult>> ExecuteAsync(
        WriteShaderSourceRequest request,
        RekallAgeCommandContext context)
    {
        if (request.Source is null)
        {
            var error = new RekallAgeCommandError("REKALL_SHADER_SOURCE_REQUIRED", "Shader source is required.", request.Name);
            return RekallAgeCommandResult<WriteShaderSourceResult>.Failure(
                EmptyWrite(request),
                error.Message,
                [error]);
        }

        if (!ShaderSourcePaths.TryResolveProjectPath(request.ProjectRoot, request.Name, request.Stage, out var resolved, out var errors))
        {
            return RekallAgeCommandResult<WriteShaderSourceResult>.Failure(
                EmptyWrite(request),
                "Shader source path is invalid.",
                errors);
        }

        if (!request.Overwrite && File.Exists(resolved.Path))
        {
            var error = new RekallAgeCommandError("REKALL_SHADER_EXISTS", "Shader source already exists and overwrite is false.", resolved.RelativePath);
            return RekallAgeCommandResult<WriteShaderSourceResult>.Failure(
                EmptyWrite(resolved, validated: false, compiled: false, []),
                error.Message,
                [error]);
        }

        var validationErrors = Array.Empty<string>();
        if (request.ValidateBeforeWrite)
        {
            var validation = ShaderSourceValidator.Validate(request.Source, resolved.Path, resolved.Stage);
            validationErrors = validation.Errors.ToArray();
            if (!validation.Compiled)
            {
                var error = new RekallAgeCommandError("REKALL_SHADER_COMPILE_FAILED", "Shader source did not compile.", resolved.RelativePath);
                return RekallAgeCommandResult<WriteShaderSourceResult>.Failure(
                    EmptyWrite(resolved, validated: true, compiled: false, validationErrors),
                    error.Message,
                    [error]);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolved.Path)!);
        await File.WriteAllTextAsync(resolved.Path, request.Source, context.CancellationToken);
        context.Transaction.RecordChangedResource(resolved.Path);
        return RekallAgeCommandResult<WriteShaderSourceResult>.Success(
            new WriteShaderSourceResult(
                resolved.Name,
                resolved.Stage,
                resolved.Path,
                resolved.RelativePath,
                true,
                request.ValidateBeforeWrite,
                request.ValidateBeforeWrite,
                validationErrors),
            $"Wrote project shader '{resolved.Name}' ({resolved.Stage}).");
    }

    private static WriteShaderSourceResult EmptyWrite(WriteShaderSourceRequest request)
    {
        return new WriteShaderSourceResult(request.Name, request.Stage, string.Empty, string.Empty, false, request.ValidateBeforeWrite, false, []);
    }

    private static WriteShaderSourceResult EmptyWrite(ResolvedShaderPath resolved, bool validated, bool compiled, IReadOnlyList<string> errors)
    {
        return new WriteShaderSourceResult(resolved.Name, resolved.Stage, resolved.Path, resolved.RelativePath, false, validated, compiled, errors);
    }
}

public sealed class ValidateShaderSourceCommand : IRekallAgeCommand<ValidateShaderSourceRequest, ValidateShaderSourceResult>
{
    public string Name => "rekall.shader.validate";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Compiles a shader source or shader file with Shaderc and reports diagnostics.",
        typeof(ValidateShaderSourceRequest).FullName!,
        typeof(ValidateShaderSourceResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ValidateShaderSourceResult>> ExecuteAsync(
        ValidateShaderSourceRequest request,
        RekallAgeCommandContext context)
    {
        string source;
        string sourceName;
        if (request.Source is not null)
        {
            if (!ShaderSourcePaths.IsSupportedStage(request.Stage))
            {
                var stageError = new RekallAgeCommandError("REKALL_SHADER_STAGE_INVALID", "Shader stage must be 'vertex' or 'fragment'.", request.Stage);
                return RekallAgeCommandResult<ValidateShaderSourceResult>.Failure(
                    EmptyValidate(request, null, [stageError.Message]),
                    stageError.Message,
                    [stageError]);
            }

            source = request.Source;
            sourceName = $"{request.Name}.{ShaderSourcePaths.ExtensionForStage(request.Stage)}";
        }
        else
        {
            if (!ShaderSourcePaths.TryResolveReadPath(request.ProjectRoot, request.Name, request.Stage, request.Scope, out var resolved, out var errors))
            {
                return RekallAgeCommandResult<ValidateShaderSourceResult>.Failure(
                    EmptyValidate(request, null, []),
                    "Shader source could not be resolved.",
                    errors);
            }

            source = await File.ReadAllTextAsync(resolved.Path, context.CancellationToken);
            sourceName = resolved.Path;
        }

        var validation = ShaderSourceValidator.Validate(source, sourceName, request.Stage);
        var result = new ValidateShaderSourceResult(
            request.Name,
            ShaderSourcePaths.NormalizeStage(request.Stage),
            request.Source is null ? sourceName : null,
            validation.Compiled,
            validation.SpirvByteLength,
            validation.Errors);
        if (validation.Compiled)
        {
            return RekallAgeCommandResult<ValidateShaderSourceResult>.Success(result, $"Shader '{request.Name}' compiled.");
        }

        var error = new RekallAgeCommandError("REKALL_SHADER_COMPILE_FAILED", "Shader source did not compile.", request.Name);
        return RekallAgeCommandResult<ValidateShaderSourceResult>.Failure(result, error.Message, [error]);
    }

    private static ValidateShaderSourceResult EmptyValidate(ValidateShaderSourceRequest request, string? path, IReadOnlyList<string> errors)
    {
        return new ValidateShaderSourceResult(request.Name, request.Stage, path, false, 0, errors);
    }
}

public sealed class AssignShaderPipelineCommand : IRekallAgeCommand<AssignShaderPipelineRequest, AssignShaderPipelineResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.shader.assign_pipeline";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Assigns validated project vertex and fragment shaders to a mesh renderer entity.",
        typeof(AssignShaderPipelineRequest).FullName!,
        typeof(AssignShaderPipelineResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AssignShaderPipelineResult>> ExecuteAsync(
        AssignShaderPipelineRequest request,
        RekallAgeCommandContext context)
    {
        if (!ShaderSourcePaths.TryResolveReadPath(request.ProjectRoot, request.VertexShader, "vertex", "project", out var vertex, out var vertexErrors))
        {
            return Failure(request, "Vertex shader source could not be resolved.", vertexErrors);
        }

        if (!ShaderSourcePaths.TryResolveReadPath(request.ProjectRoot, request.FragmentShader, "fragment", "project", out var fragment, out var fragmentErrors))
        {
            return Failure(request, "Fragment shader source could not be resolved.", fragmentErrors);
        }

        var diagnostics = new List<string>();
        if (request.ValidateBeforeAssign)
        {
            var vertexSource = await File.ReadAllTextAsync(vertex.Path, context.CancellationToken);
            var vertexValidation = ShaderSourceValidator.Validate(vertexSource, vertex.Path, vertex.Stage);
            diagnostics.AddRange(vertexValidation.Errors.Select(error => $"vertex: {error}"));
            var fragmentSource = await File.ReadAllTextAsync(fragment.Path, context.CancellationToken);
            var fragmentValidation = ShaderSourceValidator.Validate(fragmentSource, fragment.Path, fragment.Stage);
            diagnostics.AddRange(fragmentValidation.Errors.Select(error => $"fragment: {error}"));

            if (!vertexValidation.Compiled || !fragmentValidation.Compiled)
            {
                var error = new RekallAgeCommandError("REKALL_SHADER_COMPILE_FAILED", "Shader pipeline did not compile.", request.EntityId);
                return RekallAgeCommandResult<AssignShaderPipelineResult>.Failure(
                    Empty(request, request.ValidateBeforeAssign, diagnostics),
                    error.Message,
                    [error]);
            }
        }

        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var entity = scene.Entities.FirstOrDefault(item => item.Id.Equals(request.EntityId, StringComparison.Ordinal));
        if (entity is null)
        {
            var error = new RekallAgeCommandError("REKALL_SHADER_ENTITY_NOT_FOUND", "Entity was not found in the scene.", request.EntityId);
            return RekallAgeCommandResult<AssignShaderPipelineResult>.Failure(
                Empty(request, request.ValidateBeforeAssign, diagnostics),
                error.Message,
                [error]);
        }

        var rendererType = entity.Components.FirstOrDefault(component =>
            component.Type is "Rekall.MeshRenderer" or "Rekall.MeshSet")?.Type;
        if (rendererType is null)
        {
            var error = new RekallAgeCommandError("REKALL_SHADER_RENDERER_MISSING", "Entity must have a Rekall.MeshRenderer or Rekall.MeshSet component.", request.EntityId);
            return RekallAgeCommandResult<AssignShaderPipelineResult>.Failure(
                Empty(request, request.ValidateBeforeAssign, diagnostics),
                error.Message,
                [error]);
        }

        var updated = scene.UpdateEntity(
            request.EntityId,
            item => item.UpdateComponent(
                rendererType,
                component => component
                    .SetProperty("vertexShader", vertex.Name)
                    .SetProperty("fragmentShader", fragment.Name)));
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);

        return RekallAgeCommandResult<AssignShaderPipelineResult>.Success(
            new AssignShaderPipelineResult(
                request.EntityId,
                vertex.Name,
                fragment.Name,
                request.ValidateBeforeAssign,
                diagnostics,
                updated),
            $"Assigned shader pipeline '{vertex.Name}' + '{fragment.Name}' to entity '{entity.Name}'.");
    }

    private static RekallAgeCommandResult<AssignShaderPipelineResult> Failure(
        AssignShaderPipelineRequest request,
        string summary,
        IReadOnlyList<RekallAgeCommandError> errors)
    {
        return RekallAgeCommandResult<AssignShaderPipelineResult>.Failure(
            Empty(request, request.ValidateBeforeAssign, []),
            summary,
            errors);
    }

    private static AssignShaderPipelineResult Empty(
        AssignShaderPipelineRequest request,
        bool validated,
        IReadOnlyList<string> errors)
    {
        return new AssignShaderPipelineResult(
            request.EntityId,
            request.VertexShader,
            request.FragmentShader,
            validated,
            errors,
            null);
    }
}

internal static class ShaderSourceValidator
{
    public static (bool Compiled, int SpirvByteLength, IReadOnlyList<string> Errors) Validate(
        string source,
        string sourceName,
        string stage)
    {
        try
        {
            var compiled = new RekallAgeVulkanShaderCompiler().CompileSource(
                source,
                sourceName,
                ShaderSourcePaths.ToVulkanStage(stage));
            return (compiled.Spirv.Length > 0, compiled.Spirv.Length, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, 0, [ex.Message]);
        }
    }
}

internal sealed record ResolvedShaderPath(string Name, string Stage, string Path, string RelativePath, string Scope);

internal static class ShaderSourcePaths
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars()
        .Concat(Path.GetInvalidPathChars())
        .Distinct()
        .ToArray();

    public static IReadOnlyList<RekallAgeCommandError> ValidateProjectRoot(string projectRoot)
    {
        return string.IsNullOrWhiteSpace(projectRoot)
            ? [new RekallAgeCommandError("REKALL_SHADER_PROJECT_ROOT_REQUIRED", "Project root is required.")]
            : [];
    }

    public static bool TryResolveProjectPath(
        string projectRoot,
        string name,
        string stage,
        out ResolvedShaderPath resolved,
        out IReadOnlyList<RekallAgeCommandError> errors)
    {
        resolved = default!;
        var validation = Validate(projectRoot, name, stage);
        if (validation.Count > 0)
        {
            errors = validation;
            return false;
        }

        var root = Path.GetFullPath(projectRoot);
        var shaderRoot = Path.Combine(root, "Shaders");
        var relativeName = NormalizeName(name);
        var relativePath = Path.Combine("Shaders", $"{relativeName}.{ExtensionForStage(stage)}");
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var shaderRootFull = Path.GetFullPath(shaderRoot);
        if (!path.StartsWith(shaderRootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            errors = [new RekallAgeCommandError("REKALL_SHADER_PATH_INVALID", "Shader path must stay inside the project Shaders directory.", name)];
            return false;
        }

        resolved = new ResolvedShaderPath(relativeName.Replace('\\', '/'), NormalizeStage(stage), path, relativePath, "project");
        errors = [];
        return true;
    }

    public static bool TryResolveReadPath(
        string projectRoot,
        string name,
        string stage,
        string scope,
        out ResolvedShaderPath resolved,
        out IReadOnlyList<RekallAgeCommandError> errors)
    {
        if (NormalizeScope(scope).Equals("engine", StringComparison.Ordinal))
        {
            return TryResolveEnginePath(name, stage, out resolved, out errors);
        }

        if (!TryResolveProjectPath(projectRoot, name, stage, out resolved, out errors))
        {
            return false;
        }

        if (!File.Exists(resolved.Path))
        {
            errors = [new RekallAgeCommandError("REKALL_SHADER_NOT_FOUND", "Shader source was not found.", resolved.RelativePath)];
            return false;
        }

        return true;
    }

    public static IReadOnlyList<ShaderSourceSummary> ListProjectShaders(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return [];
        }

        var root = Path.GetFullPath(projectRoot);
        var shaderRoot = Path.Combine(root, "Shaders");
        if (!Directory.Exists(shaderRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(shaderRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => TryStageFromExtension(Path.GetExtension(path), out _))
            .Select(path => ToSummary(path, root, "project"))
            .ToArray();
    }

    public static IReadOnlyList<ShaderSourceSummary> ListEngineShaders()
    {
        var shaderRoot = FindEngineShaderRoot();
        if (shaderRoot is null)
        {
            return [];
        }

        return Directory.EnumerateFiles(shaderRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => TryStageFromExtension(Path.GetExtension(path), out _))
            .Select(path => ToSummary(path, Directory.GetParent(shaderRoot)!.FullName, "engine"))
            .ToArray();
    }

    public static string NormalizeStage(string stage)
    {
        return stage.Trim().ToLowerInvariant() switch
        {
            "vert" or "vertex" => "vertex",
            "frag" or "fragment" => "fragment",
            _ => stage.Trim().ToLowerInvariant()
        };
    }

    public static string ExtensionForStage(string stage)
    {
        return NormalizeStage(stage) switch
        {
            "vertex" => "vert",
            "fragment" => "frag",
            _ => throw new ArgumentException("Shader stage must be 'vertex' or 'fragment'.", nameof(stage))
        };
    }

    public static RekallAgeVulkanShaderStage ToVulkanStage(string stage)
    {
        return NormalizeStage(stage) switch
        {
            "vertex" => RekallAgeVulkanShaderStage.Vertex,
            "fragment" => RekallAgeVulkanShaderStage.Fragment,
            _ => throw new ArgumentException("Shader stage must be 'vertex' or 'fragment'.", nameof(stage))
        };
    }

    public static bool IsSupportedStage(string stage)
    {
        return NormalizeStage(stage) is "vertex" or "fragment";
    }

    private static bool TryResolveEnginePath(
        string name,
        string stage,
        out ResolvedShaderPath resolved,
        out IReadOnlyList<RekallAgeCommandError> errors)
    {
        resolved = default!;
        var validation = Validate("engine", name, stage).Where(error => error.Code != "REKALL_SHADER_PROJECT_ROOT_REQUIRED").ToArray();
        if (validation.Length > 0)
        {
            errors = validation;
            return false;
        }

        var shaderRoot = FindEngineShaderRoot();
        if (shaderRoot is null)
        {
            errors = [new RekallAgeCommandError("REKALL_SHADER_ENGINE_ROOT_MISSING", "Bundled engine shader directory was not found.")];
            return false;
        }

        var relativeName = NormalizeName(name);
        var relativePath = Path.Combine("Shaders", $"{relativeName}.{ExtensionForStage(stage)}");
        var path = Path.GetFullPath(Path.Combine(Directory.GetParent(shaderRoot)!.FullName, relativePath));
        var shaderRootFull = Path.GetFullPath(shaderRoot);
        if (!path.StartsWith(shaderRootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            errors = [new RekallAgeCommandError("REKALL_SHADER_NOT_FOUND", "Engine shader source was not found.", relativePath)];
            return false;
        }

        resolved = new ResolvedShaderPath(relativeName.Replace('\\', '/'), NormalizeStage(stage), path, relativePath, "engine");
        errors = [];
        return true;
    }

    private static IReadOnlyList<RekallAgeCommandError> Validate(string projectRoot, string name, string stage)
    {
        var errors = new List<RekallAgeCommandError>();
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            errors.Add(new RekallAgeCommandError("REKALL_SHADER_PROJECT_ROOT_REQUIRED", "Project root is required."));
        }

        if (string.IsNullOrWhiteSpace(name)
            || name.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(name)
            || name.Split('/', '\\').Any(segment => segment.Length == 0 || segment.IndexOfAny(InvalidNameChars) >= 0))
        {
            errors.Add(new RekallAgeCommandError("REKALL_SHADER_PATH_INVALID", "Shader name must be a relative path without traversal.", name));
        }

        if (NormalizeStage(stage) is not "vertex" and not "fragment")
        {
            errors.Add(new RekallAgeCommandError("REKALL_SHADER_STAGE_INVALID", "Shader stage must be 'vertex' or 'fragment'.", stage));
        }

        return errors;
    }

    private static ShaderSourceSummary ToSummary(string path, string root, string scope)
    {
        TryStageFromExtension(Path.GetExtension(path), out var stage);
        var relativePath = Path.GetRelativePath(root, path);
        var name = Path.Combine(
                Path.GetDirectoryName(relativePath) is { Length: > 0 } directory
                    ? directory.StartsWith("Shaders", StringComparison.OrdinalIgnoreCase)
                        ? directory["Shaders".Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        : directory
                    : string.Empty,
                Path.GetFileNameWithoutExtension(path))
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');
        return new ShaderSourceSummary(name, stage, path, relativePath, scope, new FileInfo(path).Length);
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string NormalizeScope(string scope)
    {
        return string.IsNullOrWhiteSpace(scope) ? "project" : scope.Trim().ToLowerInvariant();
    }

    private static bool TryStageFromExtension(string extension, out string stage)
    {
        stage = extension.TrimStart('.').ToLowerInvariant() switch
        {
            "vert" => "vertex",
            "frag" => "fragment",
            _ => string.Empty
        };
        return stage.Length > 0;
    }

    private static string? FindEngineShaderRoot()
    {
        var outputCandidate = Path.Combine(AppContext.BaseDirectory, "Shaders");
        if (Directory.Exists(outputCandidate))
        {
            return outputCandidate;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var sourceCandidate = Path.Combine(directory.FullName, "src", "Rekall.Age.Rendering", "Shaders");
            if (Directory.Exists(sourceCandidate))
            {
                return sourceCandidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
