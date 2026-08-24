using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.WebGpu;

namespace Rekall.Age.Player.Web;

public static class WebGpuProofExecution
{
    public static async ValueTask<WebGpuProofEvidence> ExecuteAsync(
        RekallAgeWebGpuRenderingDevice device,
        RekallAgeGraphicsResourceHandle output,
        RekallAgeTextureFormat canvasFormat,
        Func<CancellationToken, ValueTask<WebGpuProofReadbackResult>> readPixels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(readPixels);
        var workload = WebGpuProofWorkload.Create(canvasFormat);
        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(
            workload,
            device,
            new Dictionary<string, RekallAgeGraphicsResourceHandle>(StringComparer.Ordinal)
            {
                ["engine.output"] = output
            });
        if (!compiled.Valid)
        {
            return Evidence(0, compiled.Diagnostics, null);
        }

        var submission = device.SubmitWithPixelReadback(compiled.CommandBuffer!);
        if (!submission.Valid)
        {
            return Evidence(0, submission.Diagnostics, null);
        }

        var flush = await device.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (!flush.Valid)
        {
            return Evidence(1, flush.Diagnostics, null);
        }

        var readback = WebGpuProofEvidenceJson.NormalizeReadback(
            await readPixels(cancellationToken).ConfigureAwait(false));
        return Evidence(1, readback.Diagnostics, readback.PixelProof);
    }

    private static WebGpuProofEvidence Evidence(
        int submittedFrames,
        IReadOnlyList<RekallAgeGraphicsDiagnostic> diagnostics,
        WebGpuPixelProof? pixelProof) => new(
            "WebGPU",
            RekallAgeWebGpuProtocol.Version,
            WebGpuProofWorkload.WorkloadId,
            submittedFrames,
            diagnostics,
            pixelProof);
}
