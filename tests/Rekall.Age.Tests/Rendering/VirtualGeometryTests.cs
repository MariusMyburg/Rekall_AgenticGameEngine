using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class VirtualGeometryTests
{
    [Fact]
    public void BuiltInModuleExposesVirtualGeometrySchema()
    {
        var index = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly);
        var module = Assert.Single(index.Modules, item => item.Id == "rekall.builtins");

        var virtualGeometry = Assert.Single(module.Components, component => component.DisplayName == "Virtual Geometry");

        Assert.Contains(virtualGeometry.Properties, property => property.Name == "Enabled");
        Assert.Contains(virtualGeometry.Properties, property => property.Name == "MaxSelectedTriangles");
        Assert.Contains(virtualGeometry.Properties, property => property.Name == "TargetPixelError");
    }

    [Fact]
    public void RuntimeFrameBuilderProjectsVirtualGeometrySettings()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Dense Prop", ["geometry"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.VirtualGeometry", new JsonObject
                {
                    ["enabled"] = true,
                    ["targetPixelError"] = 2.5,
                    ["clusterTriangleCount"] = 64,
                    ["maxSelectedTriangles"] = 2000,
                    ["maxLodLevel"] = 6,
                    ["debugMode"] = "clusters"
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        var renderable = Assert.Single(frame.Renderables, item => item.EntityName == "Dense Prop");
        Assert.NotNull(renderable.VirtualGeometry);
        Assert.True(renderable.VirtualGeometry.Enabled);
        Assert.Equal(2.5, renderable.VirtualGeometry.TargetPixelError);
        Assert.Equal(64, renderable.VirtualGeometry.ClusterTriangleCount);
        Assert.Equal(2000, renderable.VirtualGeometry.MaxSelectedTriangles);
        Assert.Equal(6, renderable.VirtualGeometry.MaxLodLevel);
        Assert.Equal("clusters", renderable.VirtualGeometry.DebugMode);
    }

    [Fact]
    public void RuntimeFrameBuilderProjectsPlanetVirtualGeometryToGeneratedShells()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "planet"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Gaia", ["planet"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                {
                    ["Radius"] = 6,
                    ["meshSlices"] = 192,
                    ["meshStacks"] = 96
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.AtmosphereRenderer", new JsonObject
                {
                    ["height"] = 0.2,
                    ["meshSlices"] = 384,
                    ["meshStacks"] = 192
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.VirtualGeometry", new JsonObject
                {
                    ["targetPixelError"] = 1.5,
                    ["maxSelectedTriangles"] = 12000,
                    ["clusterTriangleCount"] = 128,
                    ["maxLodLevel"] = 8
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 640, 360, debugOverlay: false);

        var surface = Assert.Single(frame.Renderables, item => item.Variant == "rekall.planet.surface");
        var atmosphere = Assert.Single(frame.Renderables, item => item.Variant == "rekall.planet.atmosphere");
        Assert.NotNull(surface.VirtualGeometry);
        Assert.NotNull(atmosphere.VirtualGeometry);
        Assert.Equal(surface.VirtualGeometry, atmosphere.VirtualGeometry);
    }

    [Fact]
    public void VulkanMeshBuilderReducesVirtualGeometryImportedMeshTriangles()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "entity-1",
            "Dense Imported Mesh",
            "mesh",
            "asset_dense",
            0,
            0,
            60,
            1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                Enabled: true,
                TargetPixelError: 1,
                ClusterTriangleCount: 4,
                MaxSelectedTriangles: 4,
                MaxLodLevel: 8,
                DebugMode: "off")));
        var assetMesh = CreateTriangleMesh("asset_dense", "Dense Asset", triangleCount: 12);
        var assets = new RekallAgeRuntimeViewportAssetSet(
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal)
            {
                ["asset_dense"] = [assetMesh]
            },
            Array.Empty<RekallAgeRuntimeViewportAssetIssue>());

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets));

        Assert.Equal("entity-1", mesh.EntityId);
        Assert.Equal("Dense Imported Mesh", mesh.EntityName);
        Assert.Equal(12, mesh.VirtualGeometrySourceTriangleCount);
        Assert.True(mesh.VirtualGeometryLodLevel > 0);
        Assert.True(mesh.Indices.Count / 3 <= 4);
    }

    [Fact]
    public async Task PerformanceBudgetReportsVirtualGeometryReduction()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Dense Authored Mesh", ["geometry"])
                .AddComponent(CreateAuthoredGeometryMeshComponent(triangleCount: 12))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.VirtualGeometry", new JsonObject
                {
                    ["maxSelectedTriangles"] = 3,
                    ["clusterTriangleCount"] = 3
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("virtual geometry budget"), CancellationToken.None);

        var result = await new InspectScenePerformanceBudgetCommand().ExecuteAsync(
            new InspectScenePerformanceBudgetRequest(root, "Main"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(1, result.Value.VirtualGeometryRenderableCount);
        Assert.Equal(12, result.Value.VirtualGeometrySourceTriangles);
        Assert.True(result.Value.VirtualGeometrySelectedTriangles <= 3);
        Assert.True(result.Value.VirtualGeometryReducedTriangles >= 9);
        Assert.Equal(result.Value.VirtualGeometrySelectedTriangles, result.Value.Triangles);
        Assert.Contains(result.Value.Recommendations, item => item.Contains("Virtual geometry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InspectVirtualGeometrySceneReportsPerRenderableReduction()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Dense Authored Mesh", ["geometry"])
                .AddComponent(CreateAuthoredGeometryMeshComponent(triangleCount: 10))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.VirtualGeometry", new JsonObject
                {
                    ["targetPixelError"] = 1.25,
                    ["maxSelectedTriangles"] = 4,
                    ["clusterTriangleCount"] = 4
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("virtual geometry inspect"), CancellationToken.None);

        var result = await new InspectVirtualGeometrySceneCommand().ExecuteAsync(
            new InspectVirtualGeometrySceneRequest(root, "Main"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("Main", result.Value.SceneName);
        Assert.Equal(1, result.Value.VirtualGeometryRenderableCount);
        Assert.Equal(10, result.Value.SourceTriangles);
        Assert.True(result.Value.SelectedTriangles <= 4);
        Assert.True(result.Value.ReducedTriangles >= 6);
        var item = Assert.Single(result.Value.Renderables);
        Assert.Equal("Dense Authored Mesh", item.EntityName);
        Assert.True(item.Enabled);
        Assert.Equal(1.25, item.TargetPixelError);
        Assert.Equal(4, item.ClusterTriangleCount);
        Assert.Equal(4, item.MaxSelectedTriangles);
        Assert.Equal(10, item.SourceTriangles);
        Assert.Equal(result.Value.SelectedTriangles, item.SelectedTriangles);
        Assert.Equal(result.Value.ReducedTriangles, item.ReducedTriangles);
        Assert.True(item.MaxLodLevel > 0);
        Assert.True(item.SelectedLodLevel > 0);
    }

    [Fact]
    public async Task ApplyVirtualGeometryToSceneAddsComponentToExistingDenseRenderable()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "planet"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Existing Detailed Planet", ["planet"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                {
                    ["radius"] = 6,
                    ["meshSlices"] = 192,
                    ["meshStacks"] = 96
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.AtmosphereRenderer", new JsonObject
                {
                    ["height"] = 0.2,
                    ["meshSlices"] = 384,
                    ["meshStacks"] = 192
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("apply virtual geometry"), CancellationToken.None);

        var result = await new ApplyVirtualGeometryToSceneCommand().ExecuteAsync(
            new ApplyVirtualGeometryToSceneRequest(
                root,
                "Main",
                MinSourceTriangles: 10000,
                MaxSelectedTriangles: 12000,
                ClusterTriangleCount: 128),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(1, result.Value.AppliedEntityCount);
        Assert.True(result.Value.CandidateEntityCount >= 1);
        var applied = Assert.Single(result.Value.AppliedEntities);
        Assert.Equal("Existing Detailed Planet", applied.EntityName);
        Assert.True(applied.SourceTriangles >= 10000);
        var updated = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var planet = updated.Entities.Single(entity => entity.Name == "Existing Detailed Planet");
        var virtualGeometry = Assert.Single(planet.Components, component => component.Type == "Rekall.VirtualGeometry");
        Assert.Equal(12000, virtualGeometry.Properties["maxSelectedTriangles"]!.GetValue<int>());
        Assert.Equal(128, virtualGeometry.Properties["clusterTriangleCount"]!.GetValue<int>());
        Assert.Contains(new RekallAgeSceneStore().GetScenePath(root, "Main"), context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task ApplyVirtualGeometryToSceneSkipsExistingComponentsUnlessOverwriteRequested()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Dense Authored Mesh", ["geometry"])
                .AddComponent(CreateAuthoredGeometryMeshComponent(triangleCount: 12))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.VirtualGeometry", new JsonObject
                {
                    ["maxSelectedTriangles"] = 3,
                    ["clusterTriangleCount"] = 3
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new ApplyVirtualGeometryToSceneCommand().ExecuteAsync(
            new ApplyVirtualGeometryToSceneRequest(
                root,
                "Main",
                MinSourceTriangles: 1,
                MaxSelectedTriangles: 10,
                ClusterTriangleCount: 5),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("skip existing virtual geometry"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(0, result.Value.AppliedEntityCount);
        Assert.Equal(1, result.Value.SkippedExistingEntityCount);
        var updated = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var virtualGeometry = updated.Entities.Single().Components.Single(component => component.Type == "Rekall.VirtualGeometry");
        Assert.Equal(3, virtualGeometry.Properties["maxSelectedTriangles"]!.GetValue<int>());
        Assert.Equal(3, virtualGeometry.Properties["clusterTriangleCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task ApplyVirtualGeometryToSceneDryRunReportsCandidatesWithoutSaving()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "planet"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Existing Detailed Planet", ["planet"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                {
                    ["radius"] = 6,
                    ["meshSlices"] = 192,
                    ["meshStacks"] = 96
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("dry run virtual geometry"), CancellationToken.None);

        var result = await new ApplyVirtualGeometryToSceneCommand().ExecuteAsync(
            new ApplyVirtualGeometryToSceneRequest(
                root,
                "Main",
                MinSourceTriangles: 10000,
                DryRun: true),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.DryRun);
        Assert.Equal(1, result.Value.AppliedEntityCount);
        Assert.Empty(context.Transaction.ChangedResources);
        var unchanged = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var planet = unchanged.Entities.Single(entity => entity.Name == "Existing Detailed Planet");
        Assert.DoesNotContain(planet.Components, component => component.Type == "Rekall.VirtualGeometry");
    }

    [Fact]
    public async Task ApplyVirtualGeometryToSceneCanTargetEntityName()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "planet"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Earth", ["planet"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                {
                    ["radius"] = 6,
                    ["meshSlices"] = 192,
                    ["meshStacks"] = 96
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Jupiter", ["planet"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                {
                    ["radius"] = 8,
                    ["meshSlices"] = 192,
                    ["meshStacks"] = 96
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new ApplyVirtualGeometryToSceneCommand().ExecuteAsync(
            new ApplyVirtualGeometryToSceneRequest(
                root,
                "Main",
                MinSourceTriangles: 30000,
                EntityName: "Earth"),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("apply earth virtual geometry"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        var applied = Assert.Single(result.Value.AppliedEntities);
        Assert.Equal("Earth", applied.EntityName);
        var updated = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var earth = updated.Entities.Single(entity => entity.Name == "Earth");
        var jupiter = updated.Entities.Single(entity => entity.Name == "Jupiter");
        Assert.Contains(earth.Components, component => component.Type == "Rekall.VirtualGeometry");
        Assert.DoesNotContain(jupiter.Components, component => component.Type == "Rekall.VirtualGeometry");
    }

    [Fact]
    public async Task InspectVirtualGeometrySceneRejectsNegativeFrames()
    {
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("virtual geometry invalid"), CancellationToken.None);

        var result = await new InspectVirtualGeometrySceneCommand().ExecuteAsync(
            new InspectVirtualGeometrySceneRequest("missing", "Main", Frames: -1),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VIRTUAL_GEOMETRY_INVALID_REQUEST");
    }

    private static RekallAgeRuntimeViewportFrame CreateFrame(params RekallAgeRuntimeViewportRenderable[] renderables)
    {
        return new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            640,
            360,
            new RekallAgeRuntimeViewportCamera("camera", "Camera", "3d", true),
            [],
            renderables,
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
    }

    private static RekallAgeVulkanSceneMesh CreateTriangleMesh(string entityId, string name, int triangleCount)
    {
        var vertices = new List<RekallAgeVulkanSceneVertex>(triangleCount * 3);
        var indices = new List<uint>(triangleCount * 3);
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var x = triangle * 2f;
            var start = (uint)vertices.Count;
            vertices.Add(new RekallAgeVulkanSceneVertex(x, 0, 0, 0, 1, 0, 1, 1, 1, 1, 0, 0));
            vertices.Add(new RekallAgeVulkanSceneVertex(x + 1, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 0));
            vertices.Add(new RekallAgeVulkanSceneVertex(x, 1, 0, 0, 1, 0, 1, 1, 1, 1, 0, 1));
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }

        return new RekallAgeVulkanSceneMesh(entityId, name, "glb", vertices, indices);
    }

    private static RekallAgeComponentDocument CreateAuthoredGeometryMeshComponent(int triangleCount)
    {
        var vertices = new JsonArray();
        var indices = new JsonArray();
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var start = triangle * 3;
            var x = triangle * 2;
            vertices.Add(new JsonObject { ["x"] = x, ["y"] = 0, ["z"] = 0 });
            vertices.Add(new JsonObject { ["x"] = x + 1, ["y"] = 0, ["z"] = 0 });
            vertices.Add(new JsonObject { ["x"] = x, ["y"] = 1, ["z"] = 0 });
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }

        return RekallAgeComponentDocument.Create("Rekall.GeometryMesh", new JsonObject
        {
            ["vertices"] = vertices,
            ["indices"] = indices
        });
    }
}
