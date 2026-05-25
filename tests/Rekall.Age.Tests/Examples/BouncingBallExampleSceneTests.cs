using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime;
using System.Runtime.CompilerServices;

namespace Rekall.Age.Tests.Examples;

public sealed class BouncingBallExampleSceneTests
{
    [Fact]
    public async Task BouncingBallExampleBuildsRunsAndCapturesViewport()
    {
        var root = Path.Combine(FindRepositoryRoot(), "Examples", "BouncingBall");
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("bouncing ball example"),
            CancellationToken.None);

        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(root, "Main", 70, CancellationToken.None);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);
        var capture = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest(root, "Main", 70, TestPaths.CreateTempDirectory(), 320, 180, false),
            context);

        Assert.True(build.Ok, build.Summary);
        Assert.Contains(world.SystemsRun, system => system == "BouncingBallMotionSystem");
        var ball = world.Entities.Single(entity => entity.Name == "Bouncing Ball");
        Assert.InRange(ball.Transform.Position3D.Y, 0.92, 4.8);
        Assert.Contains(ball.Components, component => component.Type == "Rekall.PhysicsState3D");
        Assert.Contains(frame.Renderables, renderable =>
            renderable.EntityName == "Bouncing Ball"
            && renderable.Kind == "mesh"
            && renderable.Variant == "rekall.geometry.sphere");
        Assert.Contains(frame.Renderables, renderable =>
            renderable.EntityName == "Matte Floor"
            && renderable.Kind == "mesh"
            && renderable.Variant == "rekall.geometry.cube");
        Assert.True(capture.Ok, capture.Summary);
        Assert.True(capture.Value.Captured);
        Assert.Equal(3, capture.Value.RenderableCount);
        Assert.Equal(0, capture.Value.FallbackRenderableCount);
        Assert.True(File.Exists(capture.Value.ScreenshotPath));
    }

    [Fact]
    public async Task BouncingBallExampleEventuallyRestsOnTheFloor()
    {
        var root = Path.Combine(FindRepositoryRoot(), "Examples", "BouncingBall");
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("bouncing ball settle check"),
            CancellationToken.None);

        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(root, "Main", 1800, CancellationToken.None);

        Assert.True(build.Ok, build.Summary);
        var ball = world.Entities.Single(entity => entity.Name == "Bouncing Ball");
        var velocity = ball.Components.Single(component => component.Type == "Rekall.PhysicsState3D")
            .Properties["linearVelocity"]!.AsObject();
        Assert.InRange(ball.Transform.Position3D.Y, 0.89, 0.93);
        Assert.InRange(Math.Abs(velocity["y"]!.GetValue<float>()), 0, 0.05);
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Path.GetDirectoryName(sourceFilePath)
        };
        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var directory = new DirectoryInfo(candidate!);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
