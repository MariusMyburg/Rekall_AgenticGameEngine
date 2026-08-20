using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Validation;

namespace Rekall.Age.Workflows;

public sealed class RekallAgeWorkflowShaderPipelineValidationService
    : IRekallAgeShaderPipelineValidationService
{
    public async ValueTask<RekallAgeShaderPipelineValidationResult> ValidateAsync(
        string projectRoot,
        string vertexShader,
        string fragmentShader,
        CancellationToken cancellationToken)
    {
        var resolved = await new RekallAgeProjectShaderPipelineResolver().ResolveAsync(
            projectRoot,
            new RekallAgeRuntimeViewportShaderPipeline(vertexShader, fragmentShader),
            cancellationToken);
        return new RekallAgeShaderPipelineValidationResult(
            resolved.Valid,
            resolved.AbiVersion,
            resolved.Key.ContentHash,
            resolved.Errors.Take(32).ToArray());
    }
}
