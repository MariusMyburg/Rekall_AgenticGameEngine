using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Player.Web;

public sealed record WebGpuPixelSample(int X, int Y, int R, int G, int B, int A);

public sealed record WebGpuPixelSamples(
    WebGpuPixelSample Background,
    WebGpuPixelSample Cyan,
    WebGpuPixelSample Blue,
    WebGpuPixelSample Magenta);

public sealed record WebGpuPixelProof(
    bool Passed,
    int Width,
    int Height,
    int BytesPerRow,
    WebGpuPixelSamples Samples);

public sealed record WebGpuProofEvidence(
    string Backend,
    int ProtocolVersion,
    string WorkloadId,
    int SubmittedFrames,
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics,
    WebGpuPixelProof? PixelProof);

public sealed record WebGpuProofReadbackResult(
    bool Succeeded,
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics,
    WebGpuPixelProof? PixelProof);

public static class WebGpuProofEvidenceJson
{
    public const int MaximumJsonBytes = 256 * 1024;
    private const int MaximumReadbackBytes = 64 * 1024 * 1024;

    public static string Serialize(WebGpuProofEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var json = JsonSerializer.Serialize(evidence, WebGpuProofJsonContext.Default.WebGpuProofEvidence);
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
        {
            throw new InvalidOperationException("WebGPU evidence exceeds the bounded JSON size.");
        }
        return json;
    }

    public static WebGpuProofReadbackResult DeserializeReadback(string? json)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
        {
            return InvalidReadback();
        }
        try
        {
            var result = JsonSerializer.Deserialize(json, WebGpuProofJsonContext.Default.WebGpuProofReadbackResult);
            return Valid(result) ? result! : InvalidReadback();
        }
        catch (JsonException)
        {
            return InvalidReadback();
        }
    }

    private static bool Valid(WebGpuProofReadbackResult? result)
    {
        if (result?.Diagnostics is null || result.Diagnostics.Count > 64 || result.PixelProof is not { } proof
            || proof.Samples is null || proof.Width <= 0 || proof.Height <= 0 || proof.BytesPerRow <= 0
            || proof.BytesPerRow % 256 != 0 || proof.BytesPerRow < (long)proof.Width * 4
            || (long)proof.BytesPerRow * proof.Height > MaximumReadbackBytes)
        {
            return false;
        }
        if (result.Diagnostics.Any(item => string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Message)
            || Encoding.UTF8.GetByteCount(item.Code) > 128 || Encoding.UTF8.GetByteCount(item.Message) > 2048
            || item.Target is not null && Encoding.UTF8.GetByteCount(item.Target) > 1024))
        {
            return false;
        }
        return Valid(proof.Samples.Background, proof) && Valid(proof.Samples.Cyan, proof)
            && Valid(proof.Samples.Blue, proof) && Valid(proof.Samples.Magenta, proof);
    }

    private static bool Valid(WebGpuPixelSample sample, WebGpuPixelProof proof) =>
        sample is not null && sample.X >= 0 && sample.X < proof.Width && sample.Y >= 0 && sample.Y < proof.Height
        && sample.R is >= 0 and <= 255 && sample.G is >= 0 and <= 255
        && sample.B is >= 0 and <= 255 && sample.A is >= 0 and <= 255;

    private static WebGpuProofReadbackResult InvalidReadback() => new(
        false,
        [new("REKALL_WEBGPU_READBACK_RESULT_INVALID", "The browser returned an invalid or oversized WebGPU readback result.")],
        null);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WebGpuProofEvidence))]
[JsonSerializable(typeof(WebGpuProofReadbackResult))]
internal sealed partial class WebGpuProofJsonContext : JsonSerializerContext;
