using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class ShapeRenderer2DTests
{
    [Fact]
    public void ShapeRenderer2DIsRegisteredWithInspectableDefaults()
    {
        var modules = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly);
        var builtIns = Assert.Single(modules.Modules, module => module.Id == "rekall.builtins");
        var schema = Assert.Single(
            builtIns.Components,
            component => component.TypeName == "Rekall.ShapeRenderer2D");

        Assert.Equal("Shape Renderer 2D", schema.DisplayName);
        Assert.Contains("XY plane", schema.Description, StringComparison.Ordinal);
        Assert.Contains(schema.Properties, property =>
            property.Name == "Shape"
            && property.Kind == "string"
            && property.AllowedValues.SequenceEqual(["rectangle", "circle"]));
        Assert.Contains(schema.Properties, property => property.Name == "Width" && property.Minimum == 0.0001);
        Assert.Contains(schema.Properties, property => property.Name == "Height" && property.Minimum == 0.0001);
        Assert.Contains(schema.Properties, property => property.Name == "Radius" && property.Minimum == 0.0001);
        Assert.Contains(schema.Properties, property => property.Name == "Color" && property.Kind == "color");
        Assert.Contains(schema.Properties, property => property.Name == "Active" && property.Kind == "boolean");
        Assert.True(RekallAgeBuiltInComponentTypeCatalog.IsKnown("Rekall.ShapeRenderer2D"));

        var defaults = new RekallAgeShapeRenderer2DComponent();
        Assert.Equal("rectangle", defaults.Shape);
        Assert.Equal(1, defaults.Width);
        Assert.Equal(1, defaults.Height);
        Assert.Equal(0.5, defaults.Radius);
        Assert.Equal("#ffffff", defaults.Color);
        Assert.True(defaults.Active);
    }
}
