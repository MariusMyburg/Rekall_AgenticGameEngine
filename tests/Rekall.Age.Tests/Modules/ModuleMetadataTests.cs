using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleMetadataTests
{
    [Fact]
    public void IndexerDiscoversModuleComponentsPropertiesAndCapabilities()
    {
        var index = RekallAgeModuleIndexer.IndexAssembly(typeof(TestPlatformerModule).Assembly);
        var module = Assert.Single(index.Modules, item => item.Id == "test.platformer");
        var component = Assert.Single(module.Components, item => item.TypeName.EndsWith(nameof(TestPlayerController), StringComparison.Ordinal));

        Assert.Equal("Test Platformer", module.DisplayName);
        Assert.Equal(["physics2d", "rendering2d", "world"], module.RequiredCapabilities);
        Assert.Equal("Player Controller", component.DisplayName);
        Assert.Contains(component.Properties, property => property.Name == nameof(TestPlayerController.MoveSpeed) && property.Minimum == 0 && property.Maximum == 30);
        Assert.Contains(component.Properties, property => property.Name == nameof(TestPlayerController.JumpSound) && property.Kind == "assetRef");
    }

    [Fact]
    public async Task ListComponentSchemasCommandReturnsAgentReadableSchemas()
    {
        var command = new ListComponentSchemasCommand(typeof(TestPlatformerModule).Assembly);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("schemas"), CancellationToken.None);

        var result = await command.ExecuteAsync(new ListComponentSchemasRequest("test.platformer"), context);

        Assert.True(result.Ok);
        var schema = Assert.Single(result.Value.Components, item => item.DisplayName == "Player Controller");
        Assert.Contains(schema.Properties, property => property.Name == nameof(TestPlayerController.MoveSpeed));
    }

    [Fact]
    public void BuiltInModuleProvidesCoreSchemas()
    {
        var index = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly);
        var module = Assert.Single(index.Modules, item => item.Id == "rekall.builtins");

        Assert.Contains(module.Components, component => component.DisplayName == "Transform");
        Assert.Contains(module.Components, component => component.DisplayName == "Playable Loop");
        Assert.Contains(module.Components, component => component.DisplayName == "Camera 2D");
        Assert.Contains(module.Components, component => component.DisplayName == "Camera 3D");
    }

    [RekallAgeModule("test.platformer", "Test Platformer")]
    [RekallAgeRequiresCapability("world")]
    [RekallAgeRequiresCapability("rendering2d")]
    [RekallAgeRequiresCapability("physics2d")]
    private sealed class TestPlatformerModule : RekallAgeModule
    {
        public override void Configure(RekallAgeModuleBuilder builder)
        {
            builder.RegisterComponent<TestPlayerController>();
        }
    }

    [RekallAgeComponent("Player Controller")]
    private sealed class TestPlayerController : RekallAgeComponent
    {
        [RekallAgeProperty(Minimum = 0, Maximum = 30)]
        public float MoveSpeed { get; init; } = 8f;

        [RekallAgeProperty(Kind = "assetRef", AssetKind = "audio")]
        public string? JumpSound { get; init; }
    }
}
