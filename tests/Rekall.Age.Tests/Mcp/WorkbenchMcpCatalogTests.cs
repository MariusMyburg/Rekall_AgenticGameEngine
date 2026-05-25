using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime.Commands;

namespace Rekall.Age.Tests.Mcp;

public sealed class WorkbenchMcpCatalogTests
{
    [Fact]
    public void CatalogExposesWorkbenchWorkflowCommands()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ImportAssetWithReportCommand());
        registry.Register(new DuplicateEntityCommand());
        registry.Register(new CreateGeometryPrimitiveCommand());
        registry.Register(new CreateGeometryMeshCommand());
        registry.Register(new CreateGeometryRecipeCommand());
        registry.Register(new CreateGeometryExtrusionCommand());
        registry.Register(new ParentEntityCommand());
        registry.Register(new CreatePrefabFromEntityCommand());
        registry.Register(new InstantiatePrefabCommand());
        registry.Register(new SnapEntityToGridCommand());
        registry.Register(new InspectSceneRuntimeCommand());
        registry.Register(new CaptureRuntimeViewportCommand());
        registry.Register(new ExportSceneGlbCommand());

        var names = RekallAgeMcpCatalog.FromRegistry(registry).Tools.Select(tool => tool.Name).ToArray();

        Assert.Contains("rekall.asset.import_report", names);
        Assert.Contains("rekall.level.entity.duplicate", names);
        Assert.Contains("rekall.geometry.create_primitive", names);
        Assert.Contains("rekall.geometry.create_mesh", names);
        Assert.Contains("rekall.geometry.create_recipe", names);
        Assert.Contains("rekall.geometry.create_extrusion", names);
        Assert.Contains("rekall.level.prefab.instantiate", names);
        Assert.Contains("rekall.runtime.inspect_scene", names);
        Assert.Contains("rekall.render.capture_runtime_viewport", names);
        Assert.Contains("rekall.render.export_scene_glb", names);
    }
}
