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
            return Valid(result) ? NormalizeReadback(result!) : InvalidReadback();
        }
        catch (JsonException)
        {
            return InvalidReadback();
        }
    }

    public static WebGpuProofReadbackResult NormalizeReadback(WebGpuProofReadbackResult? result)
    {
        if (!Valid(result))
        {
            return InvalidReadback();
        }

        var proof = result!.PixelProof!;
        var recomputed = proof with { Passed = SamplesPass(proof.Samples) };
        var diagnostics = result.Diagnostics.Take(64).ToList();
        if (!result.Succeeded && diagnostics.Count == 0)
        {
            diagnostics.Add(new("REKALL_WEBGPU_READBACK_FAILED", "The browser did not confirm WebGPU canvas readback success."));
        }
        if (!recomputed.Passed && diagnostics.Count < 64
            && diagnostics.All(item => item.Code != "REKALL_WEBGPU_PIXEL_PROOF_FAILED"))
        {
            diagnostics.Add(new("REKALL_WEBGPU_PIXEL_PROOF_FAILED", "Canvas pixels did not contain the expected dark background and distinct cyan, blue, and magenta regions."));
        }

        return new(result.Succeeded && diagnostics.Count == 0 && recomputed.Passed, diagnostics, recomputed);
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

    private static bool SamplesPass(WebGpuPixelSamples samples)
    {
        var background = samples.Background;
        var cyan = samples.Cyan;
        var blue = samples.Blue;
        var magenta = samples.Magenta;
        var dark = background.R < 40 && background.G < 40 && background.B < 40 && background.A >= 240;
        var cyanLike = cyan.R < 110 && cyan.G >= 150 && cyan.B >= 170 && cyan.A >= 240;
        var blueLike = blue.R < 110 && blue.G < 140 && blue.B >= 190 && blue.A >= 240;
        var magentaLike = magenta.R >= 150 && magenta.G < 120 && magenta.B >= 160 && magenta.A >= 240;
        var allZero = new[] { cyan, blue, magenta }.All(pixel => pixel.R == 0 && pixel.G == 0 && pixel.B == 0 && pixel.A == 0);
        var distinct = Distance(cyan, blue) >= 80 && Distance(cyan, magenta) >= 80 && Distance(blue, magenta) >= 80;
        return dark && cyanLike && blueLike && magentaLike && distinct && !allZero;
    }

    private static int Distance(WebGpuPixelSample left, WebGpuPixelSample right) =>
        Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);

    private static WebGpuProofReadbackResult InvalidReadback() => new(
        false,
        [new("REKALL_WEBGPU_READBACK_RESULT_INVALID", "The browser returned an invalid or oversized WebGPU readback result.")],
        null);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WebGpuProofEvidence))]
[JsonSerializable(typeof(WebGpuProofReadbackResult))]
internal sealed partial class WebGpuProofJsonContext : JsonSerializerContext;
