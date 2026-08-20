using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectShaderPipelineRequest(
    string ProjectRoot,
    string VertexShader,
    string FragmentShader);

public sealed record InspectShaderPipelineResult(
    string VertexShader,
    string FragmentShader,
    int AbiVersion,
    string ContentHash,
    int VertexSpirvBytes,
    int FragmentSpirvBytes,
    bool Valid,
    IReadOnlyList<RekallAgeShaderVertexElement> VertexElements,
    IReadOnlyList<RekallAgeShaderResourceElement> Resources,
    IReadOnlyList<string> Diagnostics);

public sealed class InspectShaderPipelineCommand
    : IRekallAgeCommand<InspectShaderPipelineRequest, InspectShaderPipelineResult>
{
    public string Name => "rekall.shader.inspect_pipeline";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Compiles and reflects a project shader pair without returning source text, reporting its bounded scene-material ABI contract and stable content identity.",
        typeof(InspectShaderPipelineRequest).FullName!,
        typeof(InspectShaderPipelineResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectShaderPipelineResult>> ExecuteAsync(
        InspectShaderPipelineRequest request,
        RekallAgeCommandContext context)
    {
        var resolved = await new RekallAgeProjectShaderPipelineResolver().ResolveAsync(
            request.ProjectRoot,
            new(request.VertexShader, request.FragmentShader),
            context.CancellationToken);
        var result = new InspectShaderPipelineResult(
            request.VertexShader,
            request.FragmentShader,
            resolved.AbiVersion,
            resolved.Key.ContentHash,
            resolved.VertexSpirv.Length,
            resolved.FragmentSpirv.Length,
            resolved.Valid,
            resolved.VertexElements.Take(32).ToArray(),
            resolved.Resources.Take(64).ToArray(),
            resolved.Errors.Take(32).ToArray());
        return RekallAgeCommandResult<InspectShaderPipelineResult>.Success(
            result,
            resolved.Valid
                ? $"Shader pipeline '{request.VertexShader}' + '{request.FragmentShader}' is compatible with ABI {resolved.AbiVersion}."
                : $"Shader pipeline '{request.VertexShader}' + '{request.FragmentShader}' is invalid; inspect Diagnostics before assignment.");
    }
}
