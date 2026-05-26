using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderLodSelectionTests
{
    [Fact]
    public void RuntimeRenderFrameSelectsLodPrimitiveByActiveCameraDistance()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Prop", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = 60 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.LodGroup", new JsonObject
                {
                    ["levels"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["minDistance"] = 0,
                            ["maxDistance"] = 25,
                            ["primitive"] = "sphere",
                            ["materialColor"] = "#ff0000"
                        },
                        new JsonObject
                        {
                            ["minDistance"] = 25,
                            ["primitive"] = "plane",
                            ["materialColor"] = "#00ff00"
                        }
                    }
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 800, 600, debugOverlay: false);

        var prop = Assert.Single(frame.Renderables, renderable => renderable.EntityName == "Prop");
        Assert.Equal("rekall.geometry.plane", prop.Variant);
        Assert.Equal("#00ff00", prop.MaterialColor);
    }

    [Fact]
    public void BuiltInModuleExposesLodGroupSchema()
    {
        var index = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly);
        var module = Assert.Single(index.Modules, item => item.Id == "rekall.builtins");

        var lod = Assert.Single(module.Components, component => component.DisplayName == "LOD Group");

        Assert.Contains(lod.Properties, property => property.Name == "Levels" && property.Kind == "lodLevels");
    }
}
