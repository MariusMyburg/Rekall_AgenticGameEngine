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
