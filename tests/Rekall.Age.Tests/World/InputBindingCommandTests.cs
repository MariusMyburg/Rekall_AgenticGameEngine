using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.World;

public sealed class InputBindingCommandTests
{
    [Fact]
    public async Task InspectReturnsBoundedNativeBindingsAcrossActionMaps()
    {
        var root = await CreateSceneAsync();
        var result = await new InspectInputBindingsCommand().ExecuteAsync(
            new InspectInputBindingsRequest(root, "Main"),
            Context("inspect-input"));

        Assert.True(result.Ok, result.Summary);
        var binding = Assert.Single(result.Value.Bindings, item => item.ActionName == "move.horizontal");
        Assert.Equal("input", binding.EntityId);
        Assert.Equal("LeftX", binding.Binding["controllerAxis"]!.GetValue<string>());
    }

    [Fact]
    public async Task RebindReplacesOneActionTransactionallyWithoutStringEncoding()
    {
        var root = await CreateSceneAsync();
        var transaction = RekallAgeTransaction.Begin("rebind-input");
        var result = await new RebindInputActionCommand().ExecuteAsync(
            new RebindInputActionRequest(
                root,
                "Main",
                "input",
                "move.horizontal",
                new JsonObject
                {
                    ["positiveKey"] = "Right",
                    ["negativeKey"] = "Left",
                    ["controllerAxis"] = "RightX",
                    ["deadzone"] = 0.25
                }),
            new RekallAgeCommandContext("agent", transaction, CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        var actions = result.Value.Scene.GetRequiredEntity("input")
            .Components.Single(component => component.Type == "Rekall.InputActionMap")
            .Properties["actions"]!.AsArray();
        var rebound = Assert.IsType<JsonObject>(Assert.Single(actions));
        Assert.Equal("move.horizontal", rebound["name"]!.GetValue<string>());
        Assert.Equal("RightX", rebound["controllerAxis"]!.GetValue<string>());
        Assert.Single(transaction.ChangedResources);
    }

    [Fact]
    public async Task RebindCanRemoveActionAndRejectsMissingMap()
    {
        var root = await CreateSceneAsync();
        var removed = await new RebindInputActionCommand().ExecuteAsync(
            new RebindInputActionRequest(root, "Main", "input", "move.horizontal", Remove: true),
            Context("remove-input"));
        Assert.True(removed.Ok, removed.Summary);
        Assert.Empty(removed.Value.Scene.GetRequiredEntity("input").Components.Single().Properties["actions"]!.AsArray());

        var missing = await new RebindInputActionCommand().ExecuteAsync(
            new RebindInputActionRequest(root, "Main", "input", "missing", new JsonObject { ["key"] = "Q" }),
            Context("missing-input"));
        Assert.False(missing.Ok);
        Assert.Contains(missing.Errors, error => error.Code == "REKALL_INPUT_ACTION_NOT_FOUND");
    }

    private static async Task<string> CreateSceneAsync()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity((RekallAgeEntityDocument.Create("Input", ["input"]) with { Id = "input" })
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.InputActionMap",
                    new JsonObject
                    {
                        ["actions"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["name"] = "move.horizontal",
                                ["positiveKey"] = "D",
                                ["negativeKey"] = "A",
                                ["controllerAxis"] = "LeftX"
                            }
                        }
                    })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        return root;
    }

    private static RekallAgeCommandContext Context(string purpose) =>
        new("agent", RekallAgeTransaction.Begin(purpose), CancellationToken.None);
}
