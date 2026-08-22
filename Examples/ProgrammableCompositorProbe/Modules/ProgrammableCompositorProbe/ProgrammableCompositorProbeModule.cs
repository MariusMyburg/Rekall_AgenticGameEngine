using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.ProgrammableCompositorProbe;

[RekallAgeModule("example.programmable_compositor_probe", "Programmable Compositor Probe")]
[RekallAgeRequiresCapability("world")]
[RekallAgeRequiresCapability("rendering3d")]
public sealed class ProgrammableCompositorProbeModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder) =>
        builder.RegisterRuntimeSystem<ProgrammableCompositorProbeSystem>();
}

public sealed class ProgrammableCompositorProbeSystem : IRekallAgeRuntimeModuleSystem
{
    public string Id => nameof(ProgrammableCompositorProbeSystem);
    public int Priority => 100;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context) =>
        ValueTask.FromResult(world.WithGpuWorkload(Workload));

    private static RekallAgeRuntimeGpuWorkload Workload { get; } = new("probe.invert-compositor")
    {
        Samplers = [new("linear") { AddressU = "clamp-to-edge", AddressV = "clamp-to-edge" }],
        Shaders =
        [
            new("fullscreen.vertex", RekallAgeRuntimeGpuShaderStage.Vertex, RekallAgeRuntimeGpuShaderLanguage.Glsl,
                """
                #version 450
                layout(location = 0) out vec2 uv;
                void main()
                {
                    vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
                    uv = p * 0.5;
                    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
                }
                """),
            new("invert.fragment", RekallAgeRuntimeGpuShaderStage.Fragment, RekallAgeRuntimeGpuShaderLanguage.Glsl,
                """
                #version 450
                layout(set = 0, binding = 0) uniform texture2D sceneColor;
                layout(set = 0, binding = 1) uniform sampler sceneSampler;
                layout(location = 0) in vec2 uv;
                layout(location = 0) out vec4 outputColor;
                void main()
                {
                    vec4 source = texture(sampler2D(sceneColor, sceneSampler), uv);
                    vec3 invertedScene = vec3(1.0) - source.rgb;
                    float band = step(0.5, fract((uv.x + uv.y) * 8.0));
                    vec3 diagnosticOverlay = mix(vec3(0.05, 0.24, 0.72), vec3(0.88, 0.10, 0.34), band);
                    outputColor = vec4(mix(invertedScene, diagnosticOverlay, 0.55), source.a);
                }
                """)
        ],
        BindingLayouts =
        [
            new("scene.layout",
            [
                new(0, RekallAgeRuntimeGpuBindingType.SampledTexture, [RekallAgeRuntimeGpuShaderStage.Fragment]),
                new(1, RekallAgeRuntimeGpuBindingType.Sampler, [RekallAgeRuntimeGpuShaderStage.Fragment])
            ])
        ],
        BindingSets =
        [
            new("scene.set", "scene.layout",
            [
                new(0, "engine.scene-color"),
                new(1, "linear")
            ])
        ],
        Pipelines =
        [
            new("invert.pipeline", RekallAgeRuntimeGpuPipelineKind.Render)
            {
                VertexShader = "fullscreen.vertex",
                FragmentShader = "invert.fragment",
                BindingLayouts = ["scene.layout"],
                ColorFormats = ["bgra8-unorm"],
                DepthStencilFormat = "depth24-stencil8",
                CullMode = "none"
            }
        ],
        Commands =
        [
            new(RekallAgeRuntimeGpuCommandKind.BeginRenderPass) { Resource = "engine.output" },
            new(RekallAgeRuntimeGpuCommandKind.SetRenderPipeline) { Resource = "invert.pipeline" },
            new(RekallAgeRuntimeGpuCommandKind.SetBindingSet) { BindingSetIndex = 0, Resource = "scene.set" },
            new(RekallAgeRuntimeGpuCommandKind.Draw) { VertexCount = 3 },
            new(RekallAgeRuntimeGpuCommandKind.EndRenderPass)
        ]
    };
}
