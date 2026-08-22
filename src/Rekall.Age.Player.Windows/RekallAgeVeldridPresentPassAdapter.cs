using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Veldrid;

namespace Rekall.Age.Player.Windows;

/// <summary>Executes AGE's backend-neutral present-pass command stream on Veldrid.</summary>
internal sealed class RekallAgeVeldridPresentPassAdapter : IDisposable
{
    private readonly RekallAgePresentPassCommandPlanner _planner = new("veldrid-vulkan");

    public void Record(
        CommandList nativeCommands,
        Framebuffer framebuffer,
        Pipeline pipeline,
        ResourceSet sceneTextureSet,
        ResourceSet postProcessSet,
        int width,
        int height,
        RgbaFloat clearColor)
    {
        ArgumentNullException.ThrowIfNull(nativeCommands);
        ArgumentNullException.ThrowIfNull(framebuffer);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(sceneTextureSet);
        ArgumentNullException.ThrowIfNull(postProcessSet);

        var commands = _planner.Plan(
            Math.Max(1, width),
            Math.Max(1, height),
            new RekallAgeColorClearValue(clearColor.R, clearColor.G, clearColor.B, clearColor.A));
        foreach (var command in commands.Commands)
        {
            switch (command)
            {
                case RekallAgeBeginRenderPassCommand begin:
                    nativeCommands.SetFramebuffer(framebuffer);
                    nativeCommands.SetFullViewports();
                    nativeCommands.SetFullScissorRects();
                    var clear = begin.Descriptor.ColorClearValues[0];
                    nativeCommands.ClearColorTarget(0, new RgbaFloat(clear.Red, clear.Green, clear.Blue, clear.Alpha));
                    break;
                case RekallAgeSetRenderPipelineCommand:
                    nativeCommands.SetPipeline(pipeline);
                    break;
                case RekallAgeSetBindingSetCommand { Index: 0 }:
                    nativeCommands.SetGraphicsResourceSet(0, sceneTextureSet);
                    break;
                case RekallAgeSetBindingSetCommand { Index: 1 }:
                    nativeCommands.SetGraphicsResourceSet(1, postProcessSet);
                    break;
                case RekallAgeDrawCommand draw:
                    nativeCommands.Draw(draw.VertexCount, draw.InstanceCount, draw.FirstVertex, draw.FirstInstance);
                    break;
                case RekallAgeEndRenderPassCommand:
                    break;
                default:
                    throw new NotSupportedException($"Veldrid present adapter cannot execute {command.GetType().Name}.");
            }
        }
    }

    public void Dispose() => _planner.Dispose();
}
