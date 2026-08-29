using Rekall.Age.Editor.Contracts;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioInspectorBrowserTests
{
    private static readonly IReadOnlyList<RekallAgeInspectorComponentModel> Components =
    [
        new(
            "Game.Vehicle",
            [
                new("maxSpeed", "12", "number") { Description = "Maximum forward speed" },
                new("driveMode", "all-wheel", "string")
            ])
        {
            DisplayName = "Vehicle Controller",
            Description = "Controls the rover drivetrain.",
            SchemaKnown = true
        },
        new(
            "Rekall.Transform2D",
            [new("x", "4.5", "number"), new("y", "1.25", "number")])
        {
            DisplayName = "Transform 2D",
            Description = "World position and scale.",
            SchemaKnown = true
        }
    ];

    [Theory]
    [InlineData("vehicle")]
    [InlineData("GAME.VEHICLE")]
    [InlineData("drivetrain")]
    [InlineData("maxSpeed")]
    [InlineData("all-wheel")]
    public void SearchMatchesComponentAndPropertyMetadataCaseInsensitively(string query)
    {
        var result = RekallAgeStudioInspectorBrowser.Project(Components, query, "Game.Vehicle");

        var component = Assert.Single(result.Components);
        Assert.Equal("Game.Vehicle", component.Type);
        Assert.Equal("Game.Vehicle", result.SelectedComponent?.Type);
    }

    [Fact]
    public void ProjectionFallsBackToFirstVisibleComponentWithoutMutatingTheSource()
    {
        var result = RekallAgeStudioInspectorBrowser.Project(Components, string.Empty, "Missing.Component");

        Assert.Equal(2, result.Components.Count);
        Assert.Equal("Game.Vehicle", result.SelectedComponent?.Type);
        Assert.Equal(2, Components.Count);
    }

    [Fact]
    public void NoSearchMatchReturnsAnEmptyResultAndNoSelection()
    {
        var result = RekallAgeStudioInspectorBrowser.Project(Components, "not-present", "Game.Vehicle");

        Assert.Empty(result.Components);
        Assert.Null(result.SelectedComponent);
    }
}
