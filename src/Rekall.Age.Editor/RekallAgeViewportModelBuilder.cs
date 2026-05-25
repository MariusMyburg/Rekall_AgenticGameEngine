using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Editor;

public sealed class RekallAgeViewportModelBuilder
{
    private readonly RekallAgeSceneStore _sceneStore;

    public RekallAgeViewportModelBuilder()
        : this(new RekallAgeSceneStore())
    {
    }

    public RekallAgeViewportModelBuilder(RekallAgeSceneStore sceneStore)
    {
        _sceneStore = sceneStore;
    }

    public async ValueTask<RekallAgeViewportModel> BuildAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var cameras = scene.Entities
            .SelectMany(entity => entity.Components
                .Where(component => component.Type is "Rekall.Camera2D" or "Rekall.Camera3D")
                .Select(component => new RekallAgeRenderCamera(
                    entity.Id,
                    entity.Name,
                    component.Type,
                    component.Properties.TryGetPropertyValue("active", out var activeNode)
                        && activeNode is not null
                        && activeNode.GetValue<bool>())))
            .ToArray();
        var sprites = scene.Entities
            .Where(entity => entity.Components.Any(component => component.Type == "Rekall.SpriteRenderer"))
            .Select(entity =>
            {
                var component = entity.Components.First(item => item.Type == "Rekall.SpriteRenderer");
                var assetId = component.Properties.TryGetPropertyValue("sprite", out var spriteNode)
                    ? spriteNode?.GetValue<string>()
                    : null;
                return new RekallAgeRenderSprite(entity.Id, entity.Name, assetId);
            })
            .ToArray();
        var meshes = scene.Entities
            .Where(entity => entity.Components.Any(component => component.Type is "Rekall.MeshRenderer" or "Rekall.MeshSet"))
            .Select(entity => new RekallAgeRenderMesh(entity.Id, entity.Name, null))
            .ToArray();
        var lights = scene.Entities
            .Where(entity => entity.Components.Any(component => component.Type.Contains("Light", StringComparison.Ordinal)))
            .Select(entity => new RekallAgeRenderLight(entity.Id, entity.Name, "light"))
            .ToArray();
        var activeCamera = cameras.FirstOrDefault(camera => camera.Active)?.EntityName
            ?? cameras.FirstOrDefault()?.EntityName;
        return new RekallAgeViewportModel(scene.Name, activeCamera, new RekallAgeRenderWorld(cameras, sprites, meshes, lights));
    }
}
