using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

/// <summary>
/// A ringed planet projects two meshes for one entity - "rekall.planet.surface" and
/// "rekall.planet.ring" - while the cloud and atmosphere shells derive their renderable ids
/// from the entity id alone. Emitting those shells for every projection produced duplicate
/// ids, which the Vulkan capture path rejects with a duplicate-key failure.
/// </summary>
public sealed class RingedPlanetProjectionTests
{
    [Fact]
    public void RingedPlanetWithCloudsAndAtmosphereProducesUniqueRenderableIds()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Meridian", ["planet"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PlanetRenderer",
                    new JsonObject { ["radius"] = 42 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CloudLayerRenderer",
                    new JsonObject { ["height"] = 0.012, ["coverage"] = 0.85 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AtmosphereRenderer",
                    new JsonObject { ["height"] = 0.055 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.RingRenderer",
                    new JsonObject { ["innerRadius"] = 58, ["outerRadius"] = 104 })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 640, 480, debugOverlay: false);

        var ids = frame.Renderables.Select(renderable => renderable.EntityId).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(ids, id => id.EndsWith(":clouds", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.EndsWith(":atmosphere", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.EndsWith(":ring", StringComparison.Ordinal));
    }
}
