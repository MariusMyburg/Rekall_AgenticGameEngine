using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Player.Web;

public static class WebGpuProofWorkload
{
    public const string WorkloadId = "proof.webgpu.asset-independent";

    public static RekallAgeRuntimeGpuWorkload Create(RekallAgeTextureFormat canvasFormat) => new(WorkloadId)
    {
        Buffers =
        [
            new("vertices", 24, RekallAgeRuntimeGpuBufferUsage.Vertex)
            {
                InitialDataUInt32 =
                [
                    0x7878784C, 0x78787844,
                    0x78787852, 0x78787844,
                    0x78787843, 0x0A787855
                ]
            },
            new("draw.arguments", 16, RekallAgeRuntimeGpuBufferUsage.Indirect)
            {
                InitialDataUInt32 = [3, 1, 0, 0]
            }
        ],
        Shaders =
        [
            new("proof.vertex", RekallAgeRuntimeGpuShaderStage.Vertex, RekallAgeRuntimeGpuShaderLanguage.Wgsl,
                """
                struct VertexOutput {
                    @builtin(position) position: vec4<f32>,
                    @location(0) color: vec3<f32>,
                };

                @vertex
                fn main(@location(0) code: vec2<u32>) -> VertexOutput {
                    let xCode = code.x & 255u;
                    let yCode = code.y & 255u;
                    var x = 0.0;
                    if (xCode == 76u) {
                        x = -0.72;
                    } else if (xCode == 82u) {
                        x = 0.72;
                    }
                    var y = 0.72;
                    if (yCode == 68u) {
                        y = -0.68;
                    }
                    var color = vec3<f32>(0.10, 0.25, 1.00);
                    if (xCode == 76u) {
                        color = vec3<f32>(0.10, 0.95, 0.90);
                    } else if (xCode == 82u) {
                        color = vec3<f32>(0.95, 0.10, 0.85);
                    }
                    var output: VertexOutput;
                    output.position = vec4<f32>(x, y, 0.0, 1.0);
                    output.color = color;
                    return output;
                }
                """),
            new("proof.fragment", RekallAgeRuntimeGpuShaderStage.Fragment, RekallAgeRuntimeGpuShaderLanguage.Wgsl,
                """
                @fragment
                fn main(@location(0) color: vec3<f32>) -> @location(0) vec4<f32> {
                    return vec4<f32>(color, 1.0);
                }
                """)
        ],
        Pipelines =
        [
            new("proof.pipeline", RekallAgeRuntimeGpuPipelineKind.Render)
            {
                VertexShader = "proof.vertex",
                FragmentShader = "proof.fragment",
                ColorFormats = [FormatName(canvasFormat)],
                CullMode = "none",
                VertexBuffers =
                [
                    new(8, RekallAgeRuntimeGpuVertexStepMode.Vertex,
                    [
                        new("Code", 0, RekallAgeRuntimeGpuVertexFormat.Uint32x2, 0)
                    ])
                ]
            }
        ],
        Commands =
        [
            new(RekallAgeRuntimeGpuCommandKind.BeginRenderPass)
            {
                Resource = "engine.output",
                ClearColors = [new(0.015f, 0.025f, 0.04f, 1f)],
                Label = "proof.webgpu.render"
            },
            new(RekallAgeRuntimeGpuCommandKind.SetRenderPipeline) { Resource = "proof.pipeline" },
            new(RekallAgeRuntimeGpuCommandKind.SetVertexBuffer)
            {
                Slot = 0,
                Resource = "vertices",
                SizeBytes = 24
            },
            new(RekallAgeRuntimeGpuCommandKind.DrawIndirect)
            {
                Resource = "draw.arguments",
                IndirectCount = 1,
                IndirectStrideBytes = 16
            },
            new(RekallAgeRuntimeGpuCommandKind.EndRenderPass)
        ]
    };

    private static string FormatName(RekallAgeTextureFormat format) => format switch
    {
        RekallAgeTextureFormat.Rgba8Unorm => "rgba8-unorm",
        RekallAgeTextureFormat.Rgba8UnormSrgb => "rgba8-unorm-srgb",
        RekallAgeTextureFormat.Bgra8Unorm => "bgra8-unorm",
        RekallAgeTextureFormat.Bgra8UnormSrgb => "bgra8-unorm-srgb",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The browser canvas must use an RGBA8 or BGRA8 format.")
    };
}
