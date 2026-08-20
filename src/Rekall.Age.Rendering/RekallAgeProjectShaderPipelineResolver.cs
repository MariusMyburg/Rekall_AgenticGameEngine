using System.Security.Cryptography;
using System.Text;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Rendering;

public readonly record struct RekallAgeShaderPipelineKey(string ContentHash);

public sealed record RekallAgeShaderVertexElement(
    int Location,
    string Name,
    string Format);

public sealed record RekallAgeShaderResourceElement(
    int Set,
    int Binding,
    string Name,
    string Kind,
    string Stages);

public sealed record RekallAgeResolvedShaderPipeline(
    RekallAgeShaderPipelineKey Key,
    int AbiVersion,
    string VertexName,
    string FragmentName,
    string VertexSource,
    string FragmentSource,
    byte[] VertexSpirv,
    byte[] FragmentSpirv,
    IReadOnlyList<RekallAgeShaderVertexElement> VertexElements,
    IReadOnlyList<RekallAgeShaderResourceElement> Resources,
    bool Valid,
    IReadOnlyList<string> Errors);

public sealed class RekallAgeProjectShaderPipelineResolver
{
    public async ValueTask<RekallAgeResolvedShaderPipeline> ResolveAsync(
        string projectRoot,
        RekallAgeRuntimeViewportShaderPipeline pipeline,
        CancellationToken cancellationToken)
    {
        if (!ShaderSourcePaths.TryResolveReadPath(
                projectRoot, pipeline.VertexShader, "vertex", "project", out var vertexPath, out var vertexErrors))
        {
            return Invalid(pipeline, vertexErrors.Select(error => $"{error.Code}: {error.Message}"));
        }

        if (!ShaderSourcePaths.TryResolveReadPath(
                projectRoot, pipeline.FragmentShader, "fragment", "project", out var fragmentPath, out var fragmentErrors))
        {
            return Invalid(pipeline, fragmentErrors.Select(error => $"{error.Code}: {error.Message}"));
        }

        var vertexSource = await File.ReadAllTextAsync(vertexPath.Path, cancellationToken).ConfigureAwait(false);
        var fragmentSource = await File.ReadAllTextAsync(fragmentPath.Path, cancellationToken).ConfigureAwait(false);
        try
        {
            var compiler = new RekallAgeVulkanShaderCompiler();
            var vertex = compiler.CompileSource(vertexSource, vertexPath.Path, RekallAgeVulkanShaderStage.Vertex);
            var fragment = compiler.CompileSource(fragmentSource, fragmentPath.Path, RekallAgeVulkanShaderStage.Fragment);
            var reflection = RekallAgeSpirvReflector.Reflect(vertex.Spirv, fragment.Spirv);
            var errors = RekallAgeSceneMaterialShaderAbi.ValidateVertexElements(reflection.VertexElements)
                .Concat(RekallAgeSceneMaterialShaderAbi.ValidateResources(reflection.Resources))
                .Take(32)
                .ToArray();
            return new RekallAgeResolvedShaderPipeline(
                CreateKey(pipeline, vertexSource, fragmentSource, vertex.Spirv, fragment.Spirv),
                RekallAgeSceneMaterialShaderAbi.Version,
                pipeline.VertexShader,
                pipeline.FragmentShader,
                vertexSource,
                fragmentSource,
                vertex.Spirv,
                fragment.Spirv,
                reflection.VertexElements,
                reflection.Resources,
                errors.Length == 0,
                errors);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Invalid(
                pipeline,
                [$"REKALL_SHADER_COMPILE_FAILED: {exception.Message}"],
                vertexSource,
                fragmentSource);
        }
    }

    private static RekallAgeShaderPipelineKey CreateKey(
        RekallAgeRuntimeViewportShaderPipeline pipeline,
        string vertexSource,
        string fragmentSource,
        byte[] vertexSpirv,
        byte[] fragmentSpirv)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, RekallAgeSceneMaterialShaderAbi.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, pipeline.VertexShader.Trim());
        Append(hash, pipeline.FragmentShader.Trim());
        Append(hash, vertexSource.ReplaceLineEndings("\n"));
        Append(hash, fragmentSource.ReplaceLineEndings("\n"));
        hash.AppendData(vertexSpirv);
        hash.AppendData(fragmentSpirv);
        return new RekallAgeShaderPipelineKey(Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static RekallAgeResolvedShaderPipeline Invalid(
        RekallAgeRuntimeViewportShaderPipeline pipeline,
        IEnumerable<string> errors,
        string vertexSource = "",
        string fragmentSource = "") =>
        new(
            new RekallAgeShaderPipelineKey(string.Empty),
            RekallAgeSceneMaterialShaderAbi.Version,
            pipeline.VertexShader,
            pipeline.FragmentShader,
            vertexSource,
            fragmentSource,
            [],
            [],
            [],
            [],
            false,
            errors.Take(32).ToArray());
}
