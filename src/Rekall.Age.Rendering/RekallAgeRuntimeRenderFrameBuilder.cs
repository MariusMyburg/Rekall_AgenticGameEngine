using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeRuntimeRenderFrameBuilder
{
    public RekallAgeRuntimeViewportFrame Build(
        RekallAgeRuntimeWorld world,
        int width,
        int height,
        bool debugOverlay)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Viewport width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Viewport height must be greater than zero.");
        }

        var cameras = world.Subsystems.Rendering.Cameras
            .Select(camera => new RekallAgeRuntimeViewportCamera(
                camera.EntityId,
                camera.EntityName,
                camera.Kind,
                camera.Active))
            .OrderByDescending(camera => camera.Active)
            .ThenBy(camera => camera.EntityName, StringComparer.Ordinal)
            .ThenBy(camera => camera.EntityId, StringComparer.Ordinal)
            .ToArray();
        var activeCamera = cameras.FirstOrDefault(camera => camera.Active) ?? cameras.FirstOrDefault();
        var renderables = BuildRenderables(world)
            .OrderBy(renderable => renderable.SortKey)
            .ThenBy(renderable => renderable.EntityName, StringComparer.Ordinal)
            .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeRuntimeViewportFrame(
            world.SceneName,
            world.FrameIndex,
            world.ElapsedTime.TotalSeconds,
            width,
            height,
            activeCamera,
            cameras,
            renderables,
            world.Subsystems.Rendering.UiLayers.Count,
            new RekallAgeRuntimeViewportOverlay(debugOverlay, world.Observations.Count),
            world.Observations
                .Select(observation => new RekallAgeRuntimeViewportObservation(
                    observation.Code,
                    observation.Severity,
                    observation.Subsystem,
                    observation.TargetName.Length > 0 ? observation.TargetName : observation.TargetId,
                    observation.Message))
                .ToArray());
    }

    private static IEnumerable<RekallAgeRuntimeViewportRenderable> BuildRenderables(RekallAgeRuntimeWorld world)
    {
        foreach (var sprite in world.Subsystems.Rendering.Sprites)
        {
            var transform = FindTransform(world, sprite.EntityId);
            yield return new RekallAgeRuntimeViewportRenderable(
                sprite.EntityId,
                sprite.EntityName,
                "sprite",
                sprite.AssetId,
                transform.Position2D.X,
                transform.Position2D.Y,
                transform.Position3D.Z,
                100);
        }

        foreach (var mesh in world.Subsystems.Rendering.Meshes)
        {
            var transform = FindTransform(world, mesh.EntityId);
            yield return new RekallAgeRuntimeViewportRenderable(
                mesh.EntityId,
                mesh.EntityName,
                "mesh",
                mesh.AssetId,
                transform.Position3D.X,
                transform.Position3D.Y,
                transform.Position3D.Z,
                200);
        }

        foreach (var light in world.Subsystems.Rendering.Lights)
        {
            var transform = FindTransform(world, light.EntityId);
            yield return new RekallAgeRuntimeViewportRenderable(
                light.EntityId,
                light.EntityName,
                "light",
                null,
                transform.Position3D.X,
                transform.Position3D.Y,
                transform.Position3D.Z,
                300);
        }

        foreach (var uiLayer in world.Subsystems.Rendering.UiLayers)
        {
            yield return new RekallAgeRuntimeViewportRenderable(
                uiLayer.EntityId,
                uiLayer.EntityName,
                "ui",
                null,
                0,
                uiLayer.Layer,
                0,
                400 + uiLayer.Layer);
        }
    }

    private static RekallAgeRuntimeTransform FindTransform(RekallAgeRuntimeWorld world, string entityId)
    {
        return world.Entities.FirstOrDefault(entity => entity.Id.Equals(entityId, StringComparison.Ordinal))?.Transform
            ?? RekallAgeRuntimeTransform.Identity;
    }
}
