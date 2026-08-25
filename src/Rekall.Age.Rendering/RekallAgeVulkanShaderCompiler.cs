using System.Runtime.InteropServices;
using Silk.NET.Shaderc;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeVulkanShaderCompiler
{
    public RekallAgeVulkanSceneShaderCompilationResult CompileScenePipeline(
        RekallAgeVulkanScenePipelineDescription pipeline)
        => CompileScenePipeline(pipeline, highDynamicRangeOutput: false);

    public RekallAgeVulkanSceneShaderCompilationResult CompileScenePipeline(
        RekallAgeVulkanScenePipelineDescription pipeline,
        bool highDynamicRangeOutput,
        bool directionalShadows = false)
    {
        var errors = new List<string>();
        var vertex = CompileShader(pipeline.VertexShaderPath, RekallAgeVulkanShaderStage.Vertex, errors);
        var fragment = CompileShader(
            pipeline.FragmentShaderPath,
            RekallAgeVulkanShaderStage.Fragment,
            errors,
            (highDynamicRangeOutput, directionalShadows) switch
            {
                (true, true) => ["REKALL_HDR_SCENE_OUTPUT", "REKALL_DIRECTIONAL_SHADOWS"],
                (true, false) => ["REKALL_HDR_SCENE_OUTPUT"],
                (false, true) => ["REKALL_DIRECTIONAL_SHADOWS"],
                _ => []
            });
        return new RekallAgeVulkanSceneShaderCompilationResult(
            errors.Count == 0 && vertex.Spirv.Length > 0 && fragment.Spirv.Length > 0,
            vertex,
            fragment,
            errors);
    }

    public RekallAgeVulkanHighFidelityShaderCompilationResult CompileHighFidelityPostPipeline(
        bool directionalShadows = false)
    {
        var errors = new List<string>();
        var fog = CompileShader(
            Path.Combine("Shaders", "rekall_fog.comp"),
            RekallAgeVulkanShaderStage.Compute,
            errors,
            directionalShadows ? ["REKALL_FOG_DIRECTIONAL_SHADOWS"] : []);
        var analyticFog = CompileShader(
            Path.Combine("Shaders", "rekall_fog.frag"),
            RekallAgeVulkanShaderStage.Fragment,
            errors);
        var bloom = CompileShader(
            Path.Combine("Shaders", "rekall_bloom.comp"),
            RekallAgeVulkanShaderStage.Compute,
            errors);
        var toneMap = CompileShader(
            Path.Combine("Shaders", "rekall_tonemap.frag"),
            RekallAgeVulkanShaderStage.Fragment,
            errors);
        RekallAgeVulkanCompiledShader fullscreenVertex;
        try
        {
            fullscreenVertex = CompileSource(FullscreenVertexSource, "rekall_fullscreen.vert", RekallAgeVulkanShaderStage.Vertex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"Vulkan fullscreen vertex shader failed to compile: {ex.Message}");
            fullscreenVertex = new RekallAgeVulkanCompiledShader(RekallAgeVulkanShaderStage.Vertex, "rekall_fullscreen.vert", []);
        }

        return new RekallAgeVulkanHighFidelityShaderCompilationResult(
            errors.Count == 0 && fog.Spirv.Length > 0 && analyticFog.Spirv.Length > 0 && bloom.Spirv.Length > 0 && toneMap.Spirv.Length > 0 && fullscreenVertex.Spirv.Length > 0,
            fog,
            analyticFog,
            bloom,
            fullscreenVertex,
            toneMap,
            errors);
    }

    public RekallAgeVulkanParticleShaderCompilationResult CompileParticlePipeline()
    {
        var errors = new List<string>();
        var compute = CompileShader(Path.Combine("Shaders", "rekall_particles.comp"), RekallAgeVulkanShaderStage.Compute, errors);
        var vertex = CompileShader(Path.Combine("Shaders", "rekall_particles.vert"), RekallAgeVulkanShaderStage.Vertex, errors);
        var fragment = CompileShader(Path.Combine("Shaders", "rekall_particles.frag"), RekallAgeVulkanShaderStage.Fragment, errors);
        return new RekallAgeVulkanParticleShaderCompilationResult(
            errors.Count == 0 && compute.Spirv.Length > 0 && vertex.Spirv.Length > 0 && fragment.Spirv.Length > 0,
            compute,
            vertex,
            fragment,
            errors);
    }

    public string ResolveShaderPath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        var outputCandidate = Path.Combine(AppContext.BaseDirectory, path);
        if (File.Exists(outputCandidate))
        {
            return outputCandidate;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var sourceCandidate = Path.Combine(directory.FullName, "src", "Rekall.Age.Rendering", path);
            if (File.Exists(sourceCandidate))
            {
                return sourceCandidate;
            }

            directory = directory.Parent;
        }

        return outputCandidate;
    }

    public RekallAgeVulkanCompiledShader CompileSource(
        string source,
        string sourceName,
        RekallAgeVulkanShaderStage stage)
    {
        return new RekallAgeVulkanCompiledShader(
            stage,
            sourceName,
            Compile(source, sourceName, ToShaderKind(stage)));
    }

    private RekallAgeVulkanCompiledShader CompileShader(
        string shaderPath,
        RekallAgeVulkanShaderStage stage,
        List<string> errors,
        IReadOnlyList<string>? defines = null)
    {
        var sourcePath = ResolveShaderPath(shaderPath);
        if (!File.Exists(sourcePath))
        {
            errors.Add($"Vulkan shader '{shaderPath}' was not found.");
            return new RekallAgeVulkanCompiledShader(stage, sourcePath, []);
        }

        try
        {
            return new RekallAgeVulkanCompiledShader(
                stage,
                sourcePath,
                Compile(ApplyDefines(File.ReadAllText(sourcePath), defines), sourcePath, ToShaderKind(stage)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"Vulkan shader '{sourcePath}' failed to compile: {ex.Message}");
            return new RekallAgeVulkanCompiledShader(stage, sourcePath, []);
        }
    }

    private static unsafe byte[] Compile(string source, string sourcePath, ShaderKind kind)
    {
        var shaderc = Shaderc.GetApi();
        var compiler = shaderc.CompilerInitialize();
        var options = shaderc.CompileOptionsInitialize();
        try
        {
            shaderc.CompileOptionsSetSourceLanguage(options, SourceLanguage.Glsl);
            shaderc.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, 0);
            shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);
            var result = shaderc.CompileIntoSpv(compiler, source, (nuint)source.Length, kind, sourcePath, "main", options);
            try
            {
                var status = shaderc.ResultGetCompilationStatus(result);
                if (status != CompilationStatus.Success)
                {
                    throw new InvalidOperationException(shaderc.ResultGetErrorMessageS(result));
                }

                var length = checked((int)shaderc.ResultGetLength(result));
                var bytes = new byte[length];
                Marshal.Copy((nint)shaderc.ResultGetBytes(result), bytes, 0, length);
                return bytes;
            }
            finally
            {
                shaderc.ResultRelease(result);
            }
        }
        finally
        {
            shaderc.CompileOptionsRelease(options);
            shaderc.CompilerRelease(compiler);
        }
    }

    private static ShaderKind ToShaderKind(RekallAgeVulkanShaderStage stage)
    {
        return stage switch
        {
            RekallAgeVulkanShaderStage.Vertex => ShaderKind.VertexShader,
            RekallAgeVulkanShaderStage.Fragment => ShaderKind.FragmentShader,
            RekallAgeVulkanShaderStage.Compute => ShaderKind.ComputeShader,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported Vulkan shader stage.")
        };
    }

    private static string ApplyDefines(string source, IReadOnlyList<string>? defines)
    {
        if (defines is null || defines.Count == 0)
        {
            return source;
        }

        var firstNewLine = source.IndexOf('\n');
        if (!source.StartsWith("#version", StringComparison.Ordinal) || firstNewLine < 0)
        {
            throw new InvalidOperationException("Vulkan GLSL sources that use compile definitions must begin with #version.");
        }

        var definitions = string.Join("\n", defines.Select(define => $"#define {define} 1"));
        return string.Concat(source.AsSpan(0, firstNewLine + 1), definitions, "\n", source.AsSpan(firstNewLine + 1));
    }

    private const string FullscreenVertexSource = """
        #version 450
        layout(location = 0) out vec2 fragUv;
        void main()
        {
            vec2 position = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
            fragUv = position;
            gl_Position = vec4(position * 2.0 - 1.0, 0.0, 1.0);
        }
        """;
}

public sealed record RekallAgeVulkanSceneShaderCompilationResult(
    bool Compiled,
    RekallAgeVulkanCompiledShader Vertex,
    RekallAgeVulkanCompiledShader Fragment,
    IReadOnlyList<string> Errors);

public sealed record RekallAgeVulkanCompiledShader(
    RekallAgeVulkanShaderStage Stage,
    string SourcePath,
    byte[] Spirv);

public sealed record RekallAgeVulkanParticleShaderCompilationResult(
    bool Compiled,
    RekallAgeVulkanCompiledShader Compute,
    RekallAgeVulkanCompiledShader Vertex,
    RekallAgeVulkanCompiledShader Fragment,
    IReadOnlyList<string> Errors);

public enum RekallAgeVulkanShaderStage
{
    Vertex,
    Fragment,
    Compute
}

public sealed record RekallAgeVulkanHighFidelityShaderCompilationResult(
    bool Compiled,
    RekallAgeVulkanCompiledShader Fog,
    RekallAgeVulkanCompiledShader AnalyticFog,
    RekallAgeVulkanCompiledShader Bloom,
    RekallAgeVulkanCompiledShader FullscreenVertex,
    RekallAgeVulkanCompiledShader ToneMap,
    IReadOnlyList<string> Errors);
