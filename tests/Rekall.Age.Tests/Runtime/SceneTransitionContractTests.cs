using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

/// <summary>
/// Rekall.SceneTransition lets an authored module ask the runtime to load a different scene.
/// Before it existed, scenes could only be changed from outside a running player over the
/// live-edit pipe, so a game could not move between its own menus, briefings and missions.
///
/// The player-side handling needs a window and is covered by running the player against a probe
/// scene. What is pinned here is the authoring contract: the type is a known reserved component,
/// it round-trips through a scene blueprint, and it survives into the runtime world where the
/// player looks for it.
/// </summary>
public sealed class SceneTransitionContractTests
{
    private const string TransitionType = "Rekall.SceneTransition";

    [Fact]
    public void SceneTransitionIsAKnownReservedComponentType()
    {
        Assert.Contains(TransitionType, RekallAgeBuiltInComponentTypeCatalog.Types);
    }

    [Fact]
    public void AuthoredSceneTransitionReachesTheRuntimeWorld()
    {
        var scene = RekallAgeSceneDocument.Create("Menu", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Flow", ["flow"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create(
                    TransitionType,
                    new JsonObject
                    {
                        ["requestedScene"] = "Mission01",
                        ["reason"] = "player pressed Deploy",
                    })));

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var entity = Assert.Single(world.Entities);
        var component = Assert.Single(
            entity.Components,
            item => item.Type.Equals(TransitionType, StringComparison.Ordinal));
        Assert.Equal("Mission01", component.Properties["requestedScene"]!.GetValue<string>());
        Assert.Equal("player pressed Deploy", component.Properties["reason"]!.GetValue<string>());
    }

    [Fact]
    public void AnEmptyRequestedSceneIsNotATransition()
    {
        // The player treats blank as "no request", so a scene can carry the component
        // permanently and fill it in only when it wants to move.
        var scene = RekallAgeSceneDocument.Create("Menu", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Flow", ["flow"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    TransitionType,
                    new JsonObject { ["requestedScene"] = "" })));

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var component = Assert.Single(
            world.Entities[0].Components,
            item => item.Type.Equals(TransitionType, StringComparison.Ordinal));
        Assert.True(string.IsNullOrWhiteSpace(component.Properties["requestedScene"]!.GetValue<string>()));
    }
}
