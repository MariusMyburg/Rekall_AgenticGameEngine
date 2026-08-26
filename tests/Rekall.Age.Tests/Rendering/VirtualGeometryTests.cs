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
    public void VulkanMeshBuilderApportionsRenderableBudgetAcrossMaterialSurfaces()
    {
        var frame = CreateFrame(new RekallAgeRuntimeViewportRenderable(
            "entity-1",
            "Multi Surface Mesh",
            "mesh",
            "asset.multi",
            0,
            0,
            0,
            1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                Enabled: true,
                TargetPixelError: 0,
                ClusterTriangleCount: 32,
                MaxSelectedTriangles: 600,
                MaxLodLevel: 8)));
        var first = CreateSubdividedOctahedron("surface-a", "Surface A", subdivisions: 3);
        var second = CreateSubdividedOctahedron("surface-b", "Surface B", subdivisions: 3);
        var assets = new RekallAgeRuntimeViewportAssetSet(
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal)
            {
                ["asset.multi"] = [first, second]
            },
            Array.Empty<RekallAgeRuntimeViewportAssetIssue>());

        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets);

        Assert.Equal(2, meshes.Count);
        Assert.All(meshes, mesh => Assert.Equal(512, mesh.VirtualGeometrySourceTriangleCount));
        Assert.InRange(meshes.Sum(mesh => mesh.Indices.Count / 3), 1, 600);
    }

    [Fact]
    public void ReducerPreservesClosedSurfaceTopologyInsteadOfDroppingTriangles()
    {
        var source = CreateSubdividedOctahedron("closed", "Closed Surface", subdivisions: 3);
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "closed",
            "Closed Surface",
            "mesh",
            "asset.closed",
            0,
            0,
            60,
            1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                Enabled: true,
                TargetPixelError: 1,
                ClusterTriangleCount: 32,
                MaxSelectedTriangles: 128,
                MaxLodLevel: 8));

        var reduced = RekallAgeVirtualGeometryReducer.Reduce(source, renderable, camera: null);

        Assert.True(reduced.Indices.Count < source.Indices.Count);
        Assert.InRange(reduced.Indices.Count / 3, 1, 128);
        Assert.Equal(0, CountGeometricBoundaryEdges(reduced));
        Assert.All(reduced.Indices, index => Assert.True(index < reduced.Vertices.Count));
    }

    [Fact]
    public void ReducerDoesNotFuseCoincidentDisconnectedClosedComponents()
    {
        var component = CreateSubdividedOctahedron("component", "Component", subdivisions: 2);
        var vertexOffset = checked((uint)component.Vertices.Count);
        var secondComponentVertices = component.Vertices
            .Select(vertex => vertex with { R = 0, G = 0, B = 0 })
            .ToArray();
        var source = component with
        {
            Vertices = component.Vertices.Concat(secondComponentVertices).ToArray(),
            Indices = component.Indices
                .Concat(component.Indices.Select(index => index + vertexOffset))
                .ToArray()
        };
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "coincident", "Coincident Components", "mesh", "asset.coincident", 0, 0, 0, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 0,
                ClusterTriangleCount: 32,
                MaxSelectedTriangles: 80,
                MaxLodLevel: 8));

        var reduced = RekallAgeVirtualGeometryReducer.Reduce(source, renderable, camera: null);

        Assert.Contains(reduced.Vertices, vertex => vertex.R == 1);
        Assert.Contains(reduced.Vertices, vertex => vertex.R == 0);
        Assert.InRange(reduced.Indices.Count / 3, 1, 80);
    }

    [Fact]
    public void ReducerDoesNotFuseCoincidentDisconnectedOpenComponents()
    {
        var white = new List<RekallAgeVulkanSceneVertex>();
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                white.Add(new RekallAgeVulkanSceneVertex(
                    x / 3f, y / 3f, 0, 0, 0, 1, 1, 1, 1, 1, x / 3f, y / 3f));
            }
        }
        var componentIndices = new List<uint>();
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                var bottomLeft = checked((uint)(y * 4 + x));
                componentIndices.AddRange(
                [
                    bottomLeft, bottomLeft + 1, bottomLeft + 4,
                    bottomLeft + 1, bottomLeft + 5, bottomLeft + 4
                ]);
            }
        }
        var black = white.Select(vertex => vertex with { R = 0, G = 0, B = 0 }).ToArray();
        var vertexOffset = checked((uint)white.Count);
        var source = new RekallAgeVulkanSceneMesh(
            "open-components",
            "Open Components",
            "glb",
            white.Concat(black).ToArray(),
            componentIndices.Concat(componentIndices.Select(index => index + vertexOffset)).ToArray());
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "open-components", "Open Components", "mesh", "asset.open-components", 0, 0, 0, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 0,
                ClusterTriangleCount: 32,
                MaxSelectedTriangles: 16,
                MaxLodLevel: 8));

        var reduced = RekallAgeVirtualGeometryReducer.Reduce(source, renderable, camera: null);

        Assert.True(reduced.Indices.Count < source.Indices.Count);
        Assert.Contains(reduced.Vertices, vertex => vertex.R == 1);
        Assert.Contains(reduced.Vertices, vertex => vertex.R == 0);
    }

    [Fact]
    public void ClusterTriangleCountControlsDistanceLodGranularity()
    {
        var source = CreateSubdividedOctahedron("cluster-size", "Cluster Size", subdivisions: 4);
        var fine = new RekallAgeRuntimeViewportRenderable(
            "cluster-size", "Cluster Size", "mesh", "asset.cluster-size", 0, 0, 48, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 1,
                ClusterTriangleCount: 16,
                MaxSelectedTriangles: 0,
                MaxLodLevel: 8));
        var coarse = fine with
        {
            VirtualGeometry = fine.VirtualGeometry! with { ClusterTriangleCount = 256 }
        };
        var camera = new RekallAgeRuntimeViewportCamera("camera", "Camera", "3d", true);

        var fineResult = RekallAgeVirtualGeometryReducer.Reduce(source, fine, camera);
        var coarseResult = RekallAgeVirtualGeometryReducer.Reduce(source, coarse, camera);

        Assert.True(fineResult.Indices.Count > coarseResult.Indices.Count);
        Assert.InRange(fineResult.VirtualGeometryLodLevel, 1, 7);
        Assert.InRange(coarseResult.VirtualGeometryLodLevel, 1, 7);
        Assert.Equal(0, CountGeometricBoundaryEdges(fineResult));
        Assert.Equal(0, CountGeometricBoundaryEdges(coarseResult));
    }

    [Fact]
    public void ReducerRecognizesClosedTopologyAcrossSplitRenderVertices()
    {
        var shared = CreateSubdividedOctahedron("split", "Split Closed Surface", subdivisions: 3);
        var splitVertices = shared.Indices.Select(index => shared.Vertices[(int)index]).ToArray();
        var split = shared with
        {
            Vertices = splitVertices,
            Indices = Enumerable.Range(0, splitVertices.Length).Select(index => checked((uint)index)).ToArray()
        };
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "split", "Split Closed Surface", "mesh", "asset.split", 0, 0, 60, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                MaxSelectedTriangles: 128, MaxLodLevel: 8));

        var reduced = RekallAgeVirtualGeometryReducer.Reduce(split, renderable, camera: null);

        Assert.True(reduced.Indices.Count < split.Indices.Count);
        Assert.Equal(0, CountGeometricBoundaryEdges(reduced));
    }

    [Fact]
    public void ReducerSelectionIsMonotonicAsTriangleBudgetTightens()
    {
        var source = CreateSubdividedOctahedron("monotonic", "Monotonic Surface", subdivisions: 3);
        var moderate = new RekallAgeRuntimeViewportRenderable(
            "monotonic", "Monotonic Surface", "mesh", "asset.monotonic", 0, 0, 0, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 0, MaxSelectedTriangles: 300, MaxLodLevel: 8));
        var aggressive = moderate with
        {
            VirtualGeometry = moderate.VirtualGeometry! with { MaxSelectedTriangles = 100 }
        };

        var moderateResult = RekallAgeVirtualGeometryReducer.Reduce(source, moderate, camera: null);
        var aggressiveResult = RekallAgeVirtualGeometryReducer.Reduce(source, aggressive, camera: null);

        Assert.True(aggressiveResult.Indices.Count <= moderateResult.Indices.Count);
        Assert.Equal(0, CountGeometricBoundaryEdges(aggressiveResult));
    }

    [Fact]
    public void ReducerMarksAnImpossibleTopologySafeTriangleCapUnsatisfied()
    {
        var source = CreateSubdividedOctahedron("minimum-shell", "Minimum Shell", subdivisions: 0);
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "minimum-shell", "Minimum Shell", "mesh", "asset.minimum-shell", 0, 0, 0, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 0,
                ClusterTriangleCount: 128,
                MaxSelectedTriangles: 1,
                MaxLodLevel: 8));

        var reduced = RekallAgeVirtualGeometryReducer.Reduce(source, renderable, camera: null);

        Assert.False(reduced.VirtualGeometryBudgetSatisfied);
        Assert.Equal(8, reduced.VirtualGeometrySourceTriangleCount);
        Assert.True(reduced.Indices.Count / 3 > 1);
    }

    [Fact]
    public void ReducerReportsUnsatisfiedBudgetWhenLodReductionIsDisabled()
    {
        var source = CreateSubdividedOctahedron("lod-disabled", "LOD Disabled", subdivisions: 2);
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "lod-disabled", "LOD Disabled", "mesh", "asset.lod-disabled", 0, 0, 0, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 0,
                MaxSelectedTriangles: 1,
                MaxLodLevel: 0));

        var reduced = RekallAgeVirtualGeometryReducer.Reduce(source, renderable, camera: null);

        Assert.Same(source.Vertices, reduced.Vertices);
        Assert.Same(source.Indices, reduced.Indices);
        Assert.Equal(source.Indices.Count / 3, reduced.VirtualGeometrySourceTriangleCount);
        Assert.False(reduced.VirtualGeometryBudgetSatisfied);
    }

    [Theory]
    [InlineData("skin")]
    [InlineData("morph")]
    public void ReducerDefersMeshesWithVertexIndexedDeformationPayloads(string payload)
    {
        var source = CreateSubdividedOctahedron("deformed", "Deformed Surface", subdivisions: 2);
        source = payload switch
        {
            "skin" => source with
            {
                SkinBindings = Enumerable.Repeat(
                    new RekallAgeVulkanSceneSkinBinding(0, 0, 0, 0, 1, 0, 0, 0),
                    source.Vertices.Count).ToArray()
            },
            _ => source with
            {
                MorphTargets =
                [
                    new RekallAgeVulkanSceneMorphTarget(
                        "detail",
                        Enumerable.Repeat(System.Numerics.Vector3.Zero, source.Vertices.Count).ToArray(),
                        Enumerable.Repeat(System.Numerics.Vector3.Zero, source.Vertices.Count).ToArray())
                ]
            }
        };
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "deformed", "Deformed Surface", "mesh", "asset.deformed", 0, 0, 60, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                MaxSelectedTriangles: 16, MaxLodLevel: 8));

        var reduced = RekallAgeVirtualGeometryReducer.Reduce(source, renderable, camera: null);

        Assert.Same(source.Vertices, reduced.Vertices);
        Assert.Same(source.Indices, reduced.Indices);
        Assert.Equal(source.Indices.Count / 3, reduced.VirtualGeometrySourceTriangleCount);
        Assert.False(reduced.VirtualGeometryBudgetSatisfied);
    }

    [Fact]
    public void ReducerIgnoresVerticesOutsideTheSurfaceIndexSet()
    {
        var source = CreateSubdividedOctahedron("surface", "Surface", subdivisions: 3);
        var unused = Enumerable.Range(0, 512)
            .Select(index => new RekallAgeVulkanSceneVertex(
                1_000 + index, 1_000, 1_000,
                0, 1, 0, 1, 0, 0, 1, 0, 0));
        var withUnusedVertices = source with { Vertices = source.Vertices.Concat(unused).ToArray() };
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "surface", "Surface", "mesh", "asset.surface", 0, 0, 60, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                MaxSelectedTriangles: 128, MaxLodLevel: 8));

        var expected = RekallAgeVirtualGeometryReducer.Reduce(source, renderable, camera: null);
        var actual = RekallAgeVirtualGeometryReducer.Reduce(withUnusedVertices, renderable, camera: null);

        Assert.Equal(expected.Indices, actual.Indices);
        Assert.Equal(expected.Vertices, actual.Vertices);
    }

    [Fact]
    public void VulkanMeshBuilderReusesReducedAssetGeometryWithinTheSameLod()
    {
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "cached", "Cached Ruin", "mesh", "asset.cached", 0, 0, 48, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 1,
                ClusterTriangleCount: 32,
                MaxSelectedTriangles: 128,
                MaxLodLevel: 8));
        var frame = CreateFrame(renderable);
        var assets = new RekallAgeRuntimeViewportAssetSet(
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal)
            {
                ["asset.cached"] = [CreateSubdividedOctahedron("source", "Source", subdivisions: 3)]
            },
            Array.Empty<RekallAgeRuntimeViewportAssetIssue>());
        var builder = new RekallAgeVulkanSceneMeshBuilder();

        var first = Assert.Single(builder.BuildMeshes(frame, assets));
        var second = Assert.Single(builder.BuildMeshes(frame, assets));

        Assert.Equal(1, builder.VirtualGeometryCacheMisses);
        Assert.Equal(1, builder.VirtualGeometryCacheHits);
        Assert.Same(first.Vertices, second.Vertices);
        Assert.Same(first.Indices, second.Indices);
    }

    [Fact]
    public void VulkanMeshBuilderReusesReducedAssetGeometryWithNonWhiteMaterial()
    {
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "cached-material", "Cached Material Ruin", "mesh", "asset.cached-material", 0, 0, 48, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 1,
                ClusterTriangleCount: 32,
                MaxSelectedTriangles: 128,
                MaxLodLevel: 8));
        var frame = CreateFrame(renderable);
        var source = CreateSubdividedOctahedron("source", "Source", subdivisions: 3) with
        {
            MaterialAssetId = "material.stone"
        };
        var assets = new RekallAgeRuntimeViewportAssetSet(
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal)
            {
                ["asset.cached-material"] = [source]
            },
            Array.Empty<RekallAgeRuntimeViewportAssetIssue>())
        {
            Materials = new Dictionary<string, RekallAgeRuntimeMaterialAsset>(StringComparer.Ordinal)
            {
                ["material.stone"] = new("material.stone")
                {
                    BaseColorFactor = new System.Numerics.Vector4(0.76f, 0.78f, 0.75f, 1)
                }
            }
        };
        var builder = new RekallAgeVulkanSceneMeshBuilder();

        var first = Assert.Single(builder.BuildMeshes(frame, assets));
        var second = Assert.Single(builder.BuildMeshes(frame, assets));

        Assert.Equal(1, builder.VirtualGeometryCacheMisses);
        Assert.Equal(1, builder.VirtualGeometryCacheHits);
        Assert.Equal(first.Vertices, second.Vertices);
        Assert.Same(first.Indices, second.Indices);
    }

    [Fact]
    public void VulkanMeshBuilderReusesReducedWebAuthoredGeometry()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera3D",
                    new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Dense Web Geometry", ["geometry"])
                .AddComponent(CreateAuthoredGeometryMeshComponent(triangleCount: 12))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.VirtualGeometry",
                    new JsonObject
                    {
                        ["maxSelectedTriangles"] = 4,
                        ["clusterTriangleCount"] = 4
                    })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 640, 360, debugOverlay: false);
        var builder = new RekallAgeVulkanSceneMeshBuilder();

        var first = Assert.Single(builder.BuildMeshes(frame));
        var second = Assert.Single(builder.BuildMeshes(frame));

        Assert.Equal(1, builder.VirtualGeometryCacheMisses);
        Assert.Equal(1, builder.VirtualGeometryCacheHits);
        Assert.Equal(first.Vertices, second.Vertices);
        Assert.Same(first.Indices, second.Indices);
    }

    [Fact]
    public void VirtualGeometrySelectionSignatureChangesOnlyAcrossLodBoundariesOrSettings()
    {
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "signature", "Signature Ruin", "mesh", "asset.signature", 0, 0, 48, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 1,
                ClusterTriangleCount: 128,
                MaxSelectedTriangles: 256,
                MaxLodLevel: 8));
        var near = CreateFrameWithCamera(
            new RekallAgeRuntimeViewportCamera("camera", "Camera", "3d", true, Z: 0),
            renderable);
        var sameBucket = CreateFrameWithCamera(
            new RekallAgeRuntimeViewportCamera("camera", "Camera", "3d", true, Z: 4),
            renderable);
        var nextBucket = CreateFrameWithCamera(
            new RekallAgeRuntimeViewportCamera("camera", "Camera", "3d", true, Z: 20),
            renderable);
        var changedSettings = CreateFrameWithCamera(
            near.ActiveCamera!,
            renderable with
            {
                VirtualGeometry = renderable.VirtualGeometry! with { ClusterTriangleCount = 64 }
            });

        var nearSignature = RekallAgeVirtualGeometrySelectionSignature.Compute(near);

        Assert.Equal(nearSignature, RekallAgeVirtualGeometrySelectionSignature.Compute(sameBucket));
        Assert.NotEqual(nearSignature, RekallAgeVirtualGeometrySelectionSignature.Compute(nextBucket));
        Assert.NotEqual(nearSignature, RekallAgeVirtualGeometrySelectionSignature.Compute(changedSettings));
    }

    [Fact]
    public void HigherTargetPixelErrorSelectsMoreAggressiveDistanceLod()
    {
        var conservative = new RekallAgeRuntimeViewportRenderable(
            "pixel-error", "Pixel Error", "mesh", "asset.pixel-error", 0, 0, 64, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 0.5,
                MaxSelectedTriangles: 0,
                MaxLodLevel: 8));
        var aggressive = conservative with
        {
            VirtualGeometry = conservative.VirtualGeometry! with { TargetPixelError = 2 }
        };
        var camera = new RekallAgeRuntimeViewportCamera("camera", "Camera", "3d", true);

        var conservativeLevel = RekallAgeVirtualGeometryReducer.ResolveDistanceLodLevel(conservative, camera);
        var aggressiveLevel = RekallAgeVirtualGeometryReducer.ResolveDistanceLodLevel(aggressive, camera);

        Assert.True(aggressiveLevel > conservativeLevel);
    }

    [Fact]
    public void HigherTargetPixelErrorProducesNoMoreGeometryAtMaximumDistanceLod()
    {
        var source = CreateSubdividedOctahedron("pixel-error-output", "Pixel Error Output", subdivisions: 4);
        var conservative = new RekallAgeRuntimeViewportRenderable(
            "pixel-error-output", "Pixel Error Output", "mesh", "asset.pixel-error-output", 0, 0, 128, 1,
            VirtualGeometry: new RekallAgeRuntimeViewportVirtualGeometry(
                TargetPixelError: 0.5,
                MaxSelectedTriangles: 0,
                MaxLodLevel: 8));
        var aggressive = conservative with
        {
            VirtualGeometry = conservative.VirtualGeometry! with { TargetPixelError = 2 }
        };
        var camera = new RekallAgeRuntimeViewportCamera("camera", "Camera", "3d", true);

        var conservativeResult = RekallAgeVirtualGeometryReducer.Reduce(source, conservative, camera);
        var aggressiveResult = RekallAgeVirtualGeometryReducer.Reduce(source, aggressive, camera);

        Assert.True(conservativeResult.Indices.Count < source.Indices.Count);
        Assert.True(aggressiveResult.Indices.Count <= conservativeResult.Indices.Count);
        Assert.Equal(8, aggressiveResult.VirtualGeometryLodLevel);
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
        Assert.True(item.BudgetSatisfied);
        Assert.True(item.MaxLodLevel > 0);
        Assert.True(item.SelectedLodLevel > 0);
    }

    [Fact]
    public async Task InspectVirtualGeometrySceneReportsAnUnsatisfiedTopologySafeCap()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera3D",
                    new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Minimum Closed Cube", ["geometry"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GeometryPrimitive",
                    new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.VirtualGeometry",
                    new JsonObject
                    {
                        ["targetPixelError"] = 0,
                        ["maxSelectedTriangles"] = 1,
                        ["clusterTriangleCount"] = 128
                    })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new InspectVirtualGeometrySceneCommand().ExecuteAsync(
            new InspectVirtualGeometrySceneRequest(root, "Main"),
            new RekallAgeCommandContext(
                "test",
                RekallAgeTransaction.Begin("inspect impossible virtual geometry cap"),
                CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        var item = Assert.Single(result.Value.Renderables);
        Assert.False(item.BudgetSatisfied);
        Assert.True(item.SelectedTriangles > item.MaxSelectedTriangles);
        Assert.Contains(result.Value.Recommendations, recommendation =>
            recommendation.Contains("could not satisfy", StringComparison.OrdinalIgnoreCase));
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
        return CreateFrameWithCamera(
            new RekallAgeRuntimeViewportCamera("camera", "Camera", "3d", true),
            renderables);
    }

    private static RekallAgeRuntimeViewportFrame CreateFrameWithCamera(
        RekallAgeRuntimeViewportCamera camera,
        params RekallAgeRuntimeViewportRenderable[] renderables)
    {
        return new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            640,
            360,
            camera,
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

    private static RekallAgeVulkanSceneMesh CreateSubdividedOctahedron(string entityId, string name, int subdivisions)
    {
        var positions = new List<System.Numerics.Vector3>
        {
            new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0),
            new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)
        };
        var triangles = new List<(int A, int B, int C)>
        {
            (2, 0, 4), (2, 4, 1), (2, 1, 5), (2, 5, 0),
            (3, 4, 0), (3, 1, 4), (3, 5, 1), (3, 0, 5)
        };

        for (var level = 0; level < subdivisions; level++)
        {
            var midpointByEdge = new Dictionary<(int, int), int>();
            var refined = new List<(int A, int B, int C)>(triangles.Count * 4);
            foreach (var (a, b, c) in triangles)
            {
                var ab = Midpoint(a, b);
                var bc = Midpoint(b, c);
                var ca = Midpoint(c, a);
                refined.Add((a, ab, ca));
                refined.Add((ab, b, bc));
                refined.Add((ca, bc, c));
                refined.Add((ab, bc, ca));
            }

            triangles = refined;

            int Midpoint(int first, int second)
            {
                var key = first < second ? (first, second) : (second, first);
                if (midpointByEdge.TryGetValue(key, out var existing))
                    return existing;
                var index = positions.Count;
                positions.Add(System.Numerics.Vector3.Normalize((positions[first] + positions[second]) * 0.5f));
                midpointByEdge[key] = index;
                return index;
            }
        }

        var vertices = positions.Select(position => new RekallAgeVulkanSceneVertex(
            position.X, position.Y, position.Z,
            position.X, position.Y, position.Z,
            1, 1, 1, 1, 0, 0)).ToArray();
        var indices = triangles.SelectMany(triangle => new[]
        {
            checked((uint)triangle.A), checked((uint)triangle.B), checked((uint)triangle.C)
        }).ToArray();
        return new RekallAgeVulkanSceneMesh(entityId, name, "octahedron", vertices, indices);
    }

    private static int CountBoundaryEdges(IReadOnlyList<uint> indices)
    {
        var uses = new Dictionary<(uint, uint), int>();
        for (var offset = 0; offset < indices.Count; offset += 3)
        {
            Add(indices[offset], indices[offset + 1]);
            Add(indices[offset + 1], indices[offset + 2]);
            Add(indices[offset + 2], indices[offset]);
        }

        return uses.Count(item => item.Value == 1);

        void Add(uint first, uint second)
        {
            var edge = first < second ? (first, second) : (second, first);
            uses[edge] = uses.GetValueOrDefault(edge) + 1;
        }
    }

    private static int CountGeometricBoundaryEdges(RekallAgeVulkanSceneMesh mesh)
    {
        var weldedByPosition = new Dictionary<(float X, float Y, float Z), uint>();
        var next = 0u;
        var weldedIndices = mesh.Indices.Select(index =>
        {
            var vertex = mesh.Vertices[(int)index];
            var position = (vertex.X, vertex.Y, vertex.Z);
            if (!weldedByPosition.TryGetValue(position, out var welded))
            {
                welded = next++;
                weldedByPosition[position] = welded;
            }

            return welded;
        }).ToArray();
        return CountBoundaryEdges(weldedIndices);
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
