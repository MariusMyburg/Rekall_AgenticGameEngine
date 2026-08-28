using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

/// <summary>
/// BuildRenderables used to resolve each of ~20 optional renderer components with its own
/// <c>Components.FirstOrDefault(...)</c> scan. That is now a single switch pass over the
/// entity's components, which was the largest single cost in frame build on large scenes.
///
/// The risk in that rewrite is a mistyped case label silently dropping one component type, so
/// these tests attach several different optional renderer components to one entity and assert
/// each still reaches the renderable.
/// </summary>
public sealed class RenderableComponentResolutionTests
{
    private static RekallAgeRuntimeWorld Build(params (string Type, JsonObject Properties)[] components)
    {
        var entity = RekallAgeEntityDocument.Create("Prop", ["prop"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.GeometryPrimitive",
                new JsonObject { ["primitive"] = "cube" }));
        foreach (var (type, properties) in components)
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(type, properties));
        }

        return new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity));
    }

    private static RekallAgeRuntimeViewportFrame Frame(RekallAgeRuntimeWorld world) =>
        new RekallAgeRuntimeRenderFrameBuilder().Build(world, 640, 480, debugOverlay: false);

    [Fact]
    public void MaterialComponentReachesTheRenderable()
    {
        var frame = Frame(Build(("Rekall.Material", new JsonObject { ["baseColor"] = "#ff0000" })));

        Assert.Equal("#ff0000", Assert.Single(frame.Renderables).MaterialColor);
    }

    // Rekall.GeometryMesh resolution is covered by RuntimeGeometryMeshReuseTests, which builds
    // an entity without the Rekall.GeometryPrimitive this fixture attaches as its base mesh.

    [Fact]
    public void SeveralOptionalRendererComponentsAreAllResolvedInOnePass()
    {
        // Material, procedural material and virtual geometry are resolved by three separate
        // case labels; attaching them together catches a label that was dropped or misspelled.
        var frame = Frame(Build(
            ("Rekall.Material", new JsonObject { ["baseColor"] = "#123456" }),
            ("Rekall.ProceduralMaterial", new JsonObject { ["pattern"] = "checker" }),
            ("Rekall.VirtualGeometry", new JsonObject { ["enabled"] = true })));

        var renderable = Assert.Single(frame.Renderables);
        Assert.Equal("#123456", renderable.MaterialColor);
        Assert.NotNull(renderable.ProceduralMaterial);
        Assert.NotNull(renderable.VirtualGeometry);
    }

    [Fact]
    public void EntityWithNoOptionalRendererComponentsStillProducesARenderable()
    {
        Assert.Single(Frame(Build()).Renderables);
    }
}
