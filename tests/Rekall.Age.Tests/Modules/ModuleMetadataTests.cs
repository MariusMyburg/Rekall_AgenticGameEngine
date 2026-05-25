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
        Assert.DoesNotContain(module.Components, component => component.DisplayName == "Playable Loop");
        Assert.Contains(module.Components, component => component.DisplayName == "Camera 2D");
        Assert.Contains(module.Components, component => component.DisplayName == "Camera 3D");
        var geometry = Assert.Single(module.Components, component => component.DisplayName == "Geometry Primitive");
        Assert.Contains(geometry.Properties, property => property.Name == "Primitive" && property.Kind == "string");
        Assert.Contains(geometry.Properties, property => property.Name == "Color" && property.Kind == "color");
        var mesh = Assert.Single(module.Components, component => component.DisplayName == "Geometry Mesh");
        Assert.Contains(mesh.Properties, property => property.Name == "Vertices" && property.Kind == "geometryVertices");
        Assert.Contains(mesh.Properties, property => property.Name == "Indices" && property.Kind == "geometryIndices");
        Assert.Contains(mesh.Properties, property => property.Name == "Color" && property.Kind == "color");
        var extrusion = Assert.Single(module.Components, component => component.DisplayName == "Geometry Extrusion");
        Assert.Contains(extrusion.Properties, property => property.Name == "Profile" && property.Kind == "geometryProfile");
        Assert.Contains(extrusion.Properties, property => property.Name == "Depth" && property.Kind == "number");
        Assert.Contains(extrusion.Properties, property => property.Name == "Color" && property.Kind == "color");
        var physicsWorld = Assert.Single(module.Components, component => component.DisplayName == "Physics World 3D");
        Assert.Contains(physicsWorld.Properties, property => property.Name == "GravityY" && property.Kind == "number");
        var physicsMaterial = Assert.Single(module.Components, component => component.DisplayName == "Physics Material 3D");
        Assert.Contains(physicsMaterial.Properties, property => property.Name == "Friction" && property.Minimum == 0);
        Assert.Contains(physicsMaterial.Properties, property => property.Name == "Restitution" && property.Minimum == 0 && property.Maximum == 1);
        Assert.Contains(physicsMaterial.Properties, property => property.Name == "MinimumBounceSpeed" && property.Minimum == 0);
        Assert.Contains(physicsMaterial.Properties, property => property.Name == "MaximumRecoveryVelocity" && property.Minimum == 0);
        Assert.Contains(physicsMaterial.Properties, property => property.Name == "SpringFrequency" && property.Minimum == 0.0001);
        Assert.Contains(physicsMaterial.Properties, property => property.Name == "DampingRatio" && property.Minimum == 0);
        var rigidbody = Assert.Single(module.Components, component => component.DisplayName == "Rigidbody 3D");
        Assert.Contains(rigidbody.Properties, property => property.Name == "Mass" && property.Minimum == 0.0001);
        var boxCollider = Assert.Single(module.Components, component => component.DisplayName == "Box Collider 3D");
        Assert.Contains(boxCollider.Properties, property => property.Name == "Width" && property.Minimum == 0.0001);
        var sphereCollider = Assert.Single(module.Components, component => component.DisplayName == "Sphere Collider 3D");
        Assert.Contains(sphereCollider.Properties, property => property.Name == "Radius" && property.Minimum == 0.0001);
        var capsuleCollider = Assert.Single(module.Components, component => component.DisplayName == "Capsule Collider 3D");
        Assert.Contains(capsuleCollider.Properties, property => property.Name == "Length" && property.Minimum == 0.0001);
        var meshCollider = Assert.Single(module.Components, component => component.DisplayName == "Mesh Collider");
        Assert.Contains(meshCollider.Properties, property => property.Name == "Convex" && property.Kind == "boolean");
        var planetRenderer = Assert.Single(module.Components, component => component.DisplayName == "Planet Renderer");
        Assert.Contains(planetRenderer.Properties, property => property.Name == "Radius" && property.Minimum == 0.0001);
        Assert.Contains(planetRenderer.Properties, property => property.Name == "SurfaceTexture" && property.Kind == "assetRef");
        Assert.Contains(planetRenderer.Properties, property => property.Name == "HeightTexture" && property.Kind == "assetRef");
        Assert.Contains(planetRenderer.Properties, property => property.Name == "Color" && property.Kind == "color");
        var atmosphereRenderer = Assert.Single(module.Components, component => component.DisplayName == "Atmosphere Renderer");
        Assert.Contains(atmosphereRenderer.Properties, property => property.Name == "RayleighColor" && property.Kind == "color");
        Assert.Contains(atmosphereRenderer.Properties, property => property.Name == "Height" && property.Minimum == 0);
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
