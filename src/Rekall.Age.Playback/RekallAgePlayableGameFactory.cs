using Rekall.Age.World;

namespace Rekall.Age.Playback;

public static class RekallAgePlayableGameFactory
{
    public static IRekallAgePlayableGame Create(string projectRoot, RekallAgeSceneDocument scene)
    {
        return RekallAgeModulePlayableGame.Create(projectRoot, scene);
    }
}
