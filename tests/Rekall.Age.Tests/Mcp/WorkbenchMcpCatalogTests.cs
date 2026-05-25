using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Mcp;
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
        registry.Register(new ParentEntityCommand());
        registry.Register(new CreatePrefabFromEntityCommand());
        registry.Register(new InstantiatePrefabCommand());
        registry.Register(new SnapEntityToGridCommand());
        registry.Register(new InspectSceneRuntimeCommand());

        var names = RekallAgeMcpCatalog.FromRegistry(registry).Tools.Select(tool => tool.Name).ToArray();

        Assert.Contains("rekall.asset.import_report", names);
        Assert.Contains("rekall.level.entity.duplicate", names);
        Assert.Contains("rekall.level.prefab.instantiate", names);
        Assert.Contains("rekall.runtime.inspect_scene", names);
    }
}
