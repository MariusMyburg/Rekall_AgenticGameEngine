using System.Globalization;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Renders a <see cref="RekallAgeRuntimeViewportFrame"/> directly through the generic
/// <see cref="IRekallAgeRenderingDevice"/> contract: camera projection, transforms, geometry primitives, vertex
/// color/material factors, and one directional light reach a real render pass, vertex/index buffers, a uniform
/// binding per draw, and indexed draw commands. It reuses the same backend-neutral frame/draw projection built for
/// the native Vulkan scene path (<see cref="RekallAgeVulkanSceneBatchBuilder"/>,
/// <see cref="RekallAgeVulkanSceneDrawPlanBuilder"/>, <see cref="RekallAgeVulkanSceneGeometryUploadBuilder"/>) so
/// both backends stay behaviorally identical instead of diverging into parallel scene renderers.
/// A depth attachment (<see cref="RekallAgeRenderingDeviceSceneResources.ResolveRenderTarget"/>) is composed onto
/// the caller's color target automatically so overlapping 3D geometry occludes correctly. Texture sampling,
/// skinning/morphing, and UI canvas draws are not yet covered by this renderer; they are deferred, still-
/// unimplemented slices of the same Task 5 tranche.
/// </summary>
public sealed class RekallAgeRenderingDeviceSceneRenderer
{
    private readonly IRekallAgeRenderingDevice _device;
    private readonly RekallAgeRenderingDeviceSceneResources _resources;
    private readonly RekallAgeVulkanSceneBatchBuilder _batchBuilder = new();

    public RekallAgeRenderingDeviceSceneRenderer(IRekallAgeRenderingDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _resources = new RekallAgeRenderingDeviceSceneResources(device);
    }

    public RekallAgeRenderingDeviceSceneFrameResult RenderFrame(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        RekallAgeGraphicsResourceHandle colorTarget,
        RekallAgeTextureFormat colorFormat)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(meshes);

        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        var batch = _batchBuilder.Build(frame, meshes);
        var drawPlan = RekallAgeVulkanSceneDrawPlanBuilder.Build(batch);
        var geometry = RekallAgeVulkanSceneGeometryUploadBuilder.Build(batch);

        if (!geometry.HasGeometry || drawPlan.Draws.Count == 0)
        {
            diagnostics.Add(new RekallAgeGraphicsDiagnostic(
                "REKALL_RENDERING_DEVICE_SCENE_EMPTY",
                "The viewport frame has no drawable geometry.",
                frame.SceneName));
            return new RekallAgeRenderingDeviceSceneFrameResult(false, 0, geometry.VertexCount, geometry.IndexCount, diagnostics);
        }

        var pipeline = _resources.ResolvePipeline(colorFormat, diagnostics);
        if (pipeline is not { } resolvedPipeline)
        {
            return Failed(geometry, diagnostics);
        }

        var renderTarget = _resources.ResolveRenderTarget(colorTarget, diagnostics);
        if (renderTarget is not { } resolvedRenderTarget)
        {
            return Failed(geometry, diagnostics);
        }

        var geometryBuffers = _resources.WriteGeometry(geometry, diagnostics);
        if (geometryBuffers is not { } buffers)
        {
            return Failed(geometry, diagnostics);
        }

        var frameUniformBuffer = _resources.WriteFrameUniform(batch.Frame, diagnostics);
        if (frameUniformBuffer is not { } frameUniform)
        {
            return Failed(geometry, diagnostics);
        }

        var drawBindingSets = _resources.WriteDrawUniformsAndBindingSets(
            drawPlan.Draws, frameUniform, resolvedPipeline.BindingLayout, diagnostics);
        if (drawBindingSets is null)
        {
            return Failed(geometry, diagnostics);
        }

        using var encoder = _device.BeginCommandEncoder("rekall-scene-frame");
        var clearColor = ParseClearColor(frame.ActiveCamera?.ClearColor);
        var begin = encoder.BeginRenderPass(new RekallAgeRenderPassDescriptor(
            resolvedRenderTarget,
            [clearColor],
            DepthClearValue: 1.0f,
            Label: "rekall-scene-pass"));
        if (!Validate(begin, diagnostics))
        {
            return Failed(geometry, diagnostics);
        }

        Validate(encoder.SetRenderPipeline(resolvedPipeline.Pipeline), diagnostics);
        Validate(encoder.SetVertexBuffer(0, buffers.VertexBuffer), diagnostics);
        Validate(encoder.SetIndexBuffer(buffers.IndexBuffer, RekallAgeIndexFormat.UInt32), diagnostics);

        var drawCount = 0;
        for (var i = 0; i < drawPlan.Draws.Count; i++)
        {
            var draw = drawPlan.Draws[i];
            Validate(encoder.SetBindingSet(0, drawBindingSets[i]), diagnostics);
            Validate(encoder.DrawIndexed(draw.IndexCount, 1, draw.FirstIndex, draw.VertexOffset), diagnostics);
            drawCount++;
        }

        Validate(encoder.EndRenderPass(), diagnostics);

        if (diagnostics.Count > 0)
        {
            return new RekallAgeRenderingDeviceSceneFrameResult(false, drawCount, geometry.VertexCount, geometry.IndexCount, diagnostics);
        }

        var commandBuffer = encoder.Finish();
        var submission = _device.Submit(commandBuffer);
        if (!submission.Valid)
        {
            diagnostics.AddRange(submission.Diagnostics);
            return new RekallAgeRenderingDeviceSceneFrameResult(false, drawCount, geometry.VertexCount, geometry.IndexCount, diagnostics);
        }

        return new RekallAgeRenderingDeviceSceneFrameResult(true, drawCount, geometry.VertexCount, geometry.IndexCount, diagnostics);
    }

    private static RekallAgeRenderingDeviceSceneFrameResult Failed(
        RekallAgeVulkanSceneGeometryUpload geometry,
        List<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        return new RekallAgeRenderingDeviceSceneFrameResult(false, 0, geometry.VertexCount, geometry.IndexCount, diagnostics);
    }

    private static bool Validate(RekallAgeGraphicsValidationResult result, List<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        if (!result.Valid)
        {
            diagnostics.AddRange(result.Diagnostics);
        }

        return result.Valid;
    }

    private static RekallAgeColorClearValue ParseClearColor(string? color)
    {
        if (color is { Length: 7 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new RekallAgeColorClearValue(r / 255f, g / 255f, b / 255f, 1f);
        }

        return new RekallAgeColorClearValue(0.0627f, 0.0941f, 0.1255f, 1f);
    }
}

public sealed record RekallAgeRenderingDeviceSceneFrameResult(
    bool Rendered,
    int DrawCount,
    int VertexCount,
    int IndexCount,
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics);

/// <summary>
/// The shared WGSL scene shader used by both the RenderingDevice-based renderer and, once the WebGPU adapter
/// executes this same renderer in-browser, the WebGPU device. It consumes the same frame/draw uniform layout
/// written by <see cref="RekallAgeRenderingDeviceSceneResources"/> and the same 48-byte vertex layout produced by
/// <see cref="RekallAgeVulkanSceneGeometryUploadBuilder"/>.
/// </summary>
public static class RekallAgeSceneWgslShaderSource
{
    public const string Source =
        """
        struct FrameUniform {
            viewProjection: mat4x4<f32>,
            lightDirection: vec4<f32>,
            lightColor: vec4<f32>,
            lightPosition: vec4<f32>,
            cameraPosition: vec4<f32>,
        };

        struct DrawUniform {
            model: mat4x4<f32>,
            materialFactors: vec4<f32>,
            emissiveFactors: vec4<f32>,
        };

        @group(0) @binding(0) var<uniform> frame: FrameUniform;
        @group(0) @binding(1) var<uniform> draw: DrawUniform;

        struct VertexInput {
            @location(0) position: vec3<f32>,
            @location(1) normal: vec3<f32>,
            @location(2) color: vec4<f32>,
            @location(3) uv: vec2<f32>,
        };

        struct VertexOutput {
            @builtin(position) clipPosition: vec4<f32>,
            @location(0) worldNormal: vec3<f32>,
            @location(1) color: vec4<f32>,
            @location(2) uv: vec2<f32>,
        };

        @vertex
        fn vs_main(input: VertexInput) -> VertexOutput {
            var output: VertexOutput;
            let worldPosition = draw.model * vec4<f32>(input.position, 1.0);
            output.clipPosition = frame.viewProjection * worldPosition;
            let normalMatrix = mat3x3<f32>(draw.model[0].xyz, draw.model[1].xyz, draw.model[2].xyz);
            output.worldNormal = normalize(normalMatrix * input.normal);
            output.color = input.color;
            output.uv = input.uv;
            return output;
        }

        @fragment
        fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
            let lightDir = normalize(-frame.lightDirection.xyz);
            let ndotl = max(dot(input.worldNormal, lightDir), 0.0);
            let ambient = 0.15;
            let lit = ambient + ndotl * (1.0 - ambient);
            let emissive = draw.emissiveFactors.rgb * draw.emissiveFactors.a;
            let baseColor = input.color.rgb * frame.lightColor.rgb * lit + emissive;
            return vec4<f32>(baseColor, input.color.a);
        }
        """;
}
