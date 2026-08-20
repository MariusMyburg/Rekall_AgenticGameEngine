namespace Rekall.Age.Validation;

public sealed record RekallAgeShaderPipelineValidationResult(
    bool Valid,
    int AbiVersion,
    string ContentHash,
    IReadOnlyList<string> Diagnostics);

public interface IRekallAgeShaderPipelineValidationService
{
    ValueTask<RekallAgeShaderPipelineValidationResult> ValidateAsync(
        string projectRoot,
        string vertexShader,
        string fragmentShader,
        CancellationToken cancellationToken);
}
