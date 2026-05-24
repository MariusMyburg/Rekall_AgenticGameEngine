using Rekall.Age.World;

namespace Rekall.Age.Playback;

public static class RekallAgePlayableGameFactory
{
    public static IRekallAgePlayableGame Create(RekallAgeSceneDocument scene)
    {
        var loopKind = scene.Entities
            .SelectMany(entity => entity.Components)
            .Where(component => component.Type.Equals("Rekall.PlayableLoop", StringComparison.Ordinal))
            .Select(component => component.Properties.TryGetPropertyValue("kind", out var value) ? value?.GetValue<string>() : null)
            .FirstOrDefault(kind => kind is not null);

        if (loopKind == "arcade" && HasComponent(scene, "Rekall.BrickGrid"))
        {
            return RekallAgeBreakoutGame.FromEntities(scene.Entities.Select(entity => entity.Name).ToArray());
        }

        if (loopKind == "arcade" && HasComponent(scene, "Rekall.PaddleController") && HasComponent(scene, "Rekall.Ball2D"))
        {
            return RekallAgePongGame.FromEntities(scene.Entities.Select(entity => entity.Name).ToArray());
        }

        throw new InvalidOperationException($"Scene '{scene.Name}' does not contain a playable runtime supported by this MVP player.");
    }

    private static bool HasComponent(RekallAgeSceneDocument scene, string componentType)
    {
        return scene.Entities.Any(entity => entity.Components.Any(component => component.Type.Equals(componentType, StringComparison.Ordinal)));
    }
}
