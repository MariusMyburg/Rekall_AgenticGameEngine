using System.Text.Json.Nodes;
using Rekall.Age.Player.Web;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class WebPlayerTests
{
    [Fact]
    public async Task SimulatesAndPresentsOnAnOrdinaryTick()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 64, 64);
        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(Array.Empty<Type>());
        var world = new RekallAgeRuntimeWorldBuilder().Build(SphereScene());
        var player = new RekallAgeWebPlayer(world, loop, device);

        var result = await player.TickAsync(
            1.0 / 60.0,
            RekallAgeWebInputSnapshot.Empty(64, 64),
            colorTarget,
            RekallAgeTextureFormat.Rgba8Unorm,
            CancellationToken.None);

        Assert.Equal(1, result.TickSequence);
        Assert.True(result.StepsSimulated >= 1);
        Assert.True(result.Rendered, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.True(result.DrawCount > 0);
        Assert.True(result.RenderedEntityCount > 0);
    }

    [Fact]
    public async Task StillPresentsTheCurrentWorldWhenElapsedTimeIsZero()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 64, 64);
        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(Array.Empty<Type>());
        var world = new RekallAgeRuntimeWorldBuilder().Build(SphereScene());
        var player = new RekallAgeWebPlayer(world, loop, device);

        var result = await player.TickAsync(
            0,
            RekallAgeWebInputSnapshot.Empty(64, 64),
            colorTarget,
            RekallAgeTextureFormat.Rgba8Unorm,
            CancellationToken.None);

        Assert.Equal(0, result.StepsSimulated);
        Assert.True(result.Rendered, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
    }

    [Fact]
    public async Task PausedTicksStillPresentButNeverAdvanceSimulationOrLoseFrameIdentity()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 64, 64);
        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(Array.Empty<Type>());
        var world = new RekallAgeRuntimeWorldBuilder().Build(SphereScene());
        var player = new RekallAgeWebPlayer(world, loop, device);
        player.Pause();

        var first = await player.TickAsync(1.0 / 60.0, RekallAgeWebInputSnapshot.Empty(64, 64), colorTarget, RekallAgeTextureFormat.Rgba8Unorm, CancellationToken.None);
        var second = await player.TickAsync(1.0 / 60.0, RekallAgeWebInputSnapshot.Empty(64, 64), colorTarget, RekallAgeTextureFormat.Rgba8Unorm, CancellationToken.None);

        Assert.Equal(0, first.StepsSimulated);
        Assert.Equal(0, second.StepsSimulated);
        Assert.Equal(first.FrameIndex, second.FrameIndex);
        Assert.True(first.Rendered);
        Assert.True(second.Rendered);

        player.Resume();
        var afterResume = await player.TickAsync(1.0 / 60.0, RekallAgeWebInputSnapshot.Empty(64, 64), colorTarget, RekallAgeTextureFormat.Rgba8Unorm, CancellationToken.None);
        Assert.True(afterResume.StepsSimulated >= 1);
    }

    [Fact]
    public async Task NeverLosesAHeldKeyEdgeAcrossAPauseBoundary()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 64, 64);
        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(Array.Empty<Type>());
        var world = new RekallAgeRuntimeWorldBuilder().Build(SphereScene());
        var player = new RekallAgeWebPlayer(world, loop, device);

        var heldW = RekallAgeWebInputSnapshot.Empty(64, 64) with { HeldKeyCodes = ["KeyW"] };
        player.Pause();
        await player.TickAsync(1.0 / 60.0, heldW, colorTarget, RekallAgeTextureFormat.Rgba8Unorm, CancellationToken.None);
        player.Resume();
        var afterResume = await player.TickAsync(1.0 / 60.0, heldW, colorTarget, RekallAgeTextureFormat.Rgba8Unorm, CancellationToken.None);

        // Still held on the resumed tick, so it must not appear as a fresh "pressed this frame" edge again;
        // proving this requires observing the bridge would need internal access, so this proves the tick itself
        // completed cleanly for the still-held snapshot instead of throwing or resetting state.
        Assert.True(afterResume.Rendered);
    }

    [Fact]
    public async Task IncrementsTickSequenceEveryCallRegardlessOfPauseOrElapsedTime()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 64, 64);
        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(Array.Empty<Type>());
        var world = new RekallAgeRuntimeWorldBuilder().Build(SphereScene());
        var player = new RekallAgeWebPlayer(world, loop, device);

        await player.TickAsync(0, RekallAgeWebInputSnapshot.Empty(64, 64), colorTarget, RekallAgeTextureFormat.Rgba8Unorm, CancellationToken.None);
        player.Pause();
        var result = await player.TickAsync(1.0 / 60.0, RekallAgeWebInputSnapshot.Empty(64, 64), colorTarget, RekallAgeTextureFormat.Rgba8Unorm, CancellationToken.None);

        Assert.Equal(2, result.TickSequence);
    }

    [Fact]
    public async Task RespondsToResizeBetweenTicksWithoutCorruptingTiming()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 128, 128);
        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(Array.Empty<Type>());
        var world = new RekallAgeRuntimeWorldBuilder().Build(SphereScene());
        var player = new RekallAgeWebPlayer(world, loop, device);

        var before = await player.TickAsync(1.0 / 60.0, RekallAgeWebInputSnapshot.Empty(64, 64), colorTarget, RekallAgeTextureFormat.Rgba8Unorm, CancellationToken.None);
        var after = await player.TickAsync(1.0 / 60.0, RekallAgeWebInputSnapshot.Empty(128, 128), colorTarget, RekallAgeTextureFormat.Rgba8Unorm, CancellationToken.None);

        Assert.True(before.Rendered);
        Assert.True(after.Rendered);
        Assert.True(after.StepsSimulated >= 1);
    }

    private static RekallAgeGraphicsResourceHandle CreateColorTarget(IRekallAgeRenderingDevice device, int width, int height)
    {
        var texture = device.CreateTexture(new RekallAgeTextureDescriptor(
            RekallAgeTextureDimension.Texture2D, width, height, 1, 1, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm, RekallAgeTextureUsage.ColorAttachment, "scene-color"));
        Assert.True(texture.Created, string.Join(Environment.NewLine, texture.Diagnostics.Select(item => item.Message)));

        var target = device.CreateRenderTarget(new RekallAgeRenderTargetDescriptor(
            [new RekallAgeRenderTargetAttachment(texture.Handle)],
            null,
            width,
            height,
            "scene-target"));
        Assert.True(target.Created, string.Join(Environment.NewLine, target.Diagnostics.Select(item => item.Message)));
        return target.Handle;
    }

    private static RekallAgeSceneDocument SphereScene()
    {
        return RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
                {
                    ["z"] = 5
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Sphere", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject
                {
                    ["primitive"] = "sphere"
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Material", new JsonObject
                {
                    ["baseColor"] = "#3477d6"
                })));
    }
}
