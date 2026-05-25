using System.Runtime.InteropServices;
using Silk.NET.Shaderc;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeVulkanShaderCompiler
{
    public RekallAgeVulkanSceneShaderCompilationResult CompileScenePipeline(
        RekallAgeVulkanScenePipelineDescription pipeline)
    {
        var errors = new List<string>();
        var vertex = CompileShader(pipeline.VertexShaderPath, RekallAgeVulkanShaderStage.Vertex, errors);
        var fragment = CompileShader(pipeline.FragmentShaderPath, RekallAgeVulkanShaderStage.Fragment, errors);
        return new RekallAgeVulkanSceneShaderCompilationResult(
            errors.Count == 0 && vertex.Spirv.Length > 0 && fragment.Spirv.Length > 0,
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
        List<string> errors)
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
                Compile(File.ReadAllText(sourcePath), sourcePath, ToShaderKind(stage)));
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
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported Vulkan shader stage.")
        };
    }
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

public enum RekallAgeVulkanShaderStage
{
    Vertex,
    Fragment
}
