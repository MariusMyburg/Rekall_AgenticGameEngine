using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.ProgrammableGeometryProbe;

[RekallAgeModule("example.programmable_geometry_probe", "Programmable Geometry Probe")]
[RekallAgeRequiresCapability("world")]
[RekallAgeRequiresCapability("rendering3d")]
public sealed class ProgrammableGeometryProbeModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder) =>
        builder.RegisterRuntimeSystem<ProgrammableGeometryProbeSystem>();
}

public sealed class ProgrammableGeometryProbeSystem : IRekallAgeRuntimeModuleSystem
{
    public string Id => nameof(ProgrammableGeometryProbeSystem);
    public int Priority => 100;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context) =>
        ValueTask.FromResult(world.WithGpuWorkload(Workload));

    private static RekallAgeRuntimeGpuWorkload Workload { get; } = new("probe.asset-backed-geometry")
    {
        Buffers =
        [
            new("vertices", 24, RekallAgeRuntimeGpuBufferUsage.Vertex)
            {
                InitialDataAsset = "asset_gpu_vertices"
            }
        ],
        Shaders =
        [
            new("geometry.vertex", RekallAgeRuntimeGpuShaderStage.Vertex, RekallAgeRuntimeGpuShaderLanguage.Glsl,
                """
                #version 450
                layout(location = 0) in uvec2 Code;
                layout(location = 0) out vec3 color;
                void main()
                {
                    uint xCode = Code.x & 255u;
                    uint yCode = Code.y & 255u;
                    float x = xCode == 76u ? -0.72 : (xCode == 82u ? 0.72 : 0.0);
                    float y = yCode == 68u ? -0.68 : 0.72;
                    gl_Position = vec4(x, y, 0.0, 1.0);
                    color = vec3((x + 1.0) * 0.5, (y + 1.0) * 0.5, 0.95);
                }
                """),
            new("geometry.fragment", RekallAgeRuntimeGpuShaderStage.Fragment, RekallAgeRuntimeGpuShaderLanguage.Glsl,
                """
                #version 450
                layout(location = 0) in vec3 color;
                layout(location = 0) out vec4 outputColor;
                void main()
                {
                    outputColor = vec4(color, 1.0);
                }
                """)
        ],
        Pipelines =
        [
            new("geometry.pipeline", RekallAgeRuntimeGpuPipelineKind.Render)
            {
                VertexShader = "geometry.vertex",
                FragmentShader = "geometry.fragment",
                ColorFormats = ["bgra8-unorm"],
                DepthStencilFormat = "depth24-stencil8",
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
            new(RekallAgeRuntimeGpuCommandKind.BeginRenderPass) { Resource = "engine.output" },
            new(RekallAgeRuntimeGpuCommandKind.SetRenderPipeline) { Resource = "geometry.pipeline" },
            new(RekallAgeRuntimeGpuCommandKind.SetVertexBuffer) { Slot = 0, Resource = "vertices", SizeBytes = 24 },
            new(RekallAgeRuntimeGpuCommandKind.Draw) { VertexCount = 3 },
            new(RekallAgeRuntimeGpuCommandKind.EndRenderPass)
        ]
    };
}
