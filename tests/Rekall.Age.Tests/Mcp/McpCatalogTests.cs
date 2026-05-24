using Rekall.Age.Core.Commands;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime.Commands;

namespace Rekall.Age.Tests.Mcp;

public sealed class McpCatalogTests
{
    [Fact]
    public void CatalogExposesRegisteredCommandSchemasAsTools()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        registry.Register(new CreateGameFromTemplateCommand());
        registry.Register(new RunSceneCommand());
        registry.Register(new CaptureScreenshotCommand());
        registry.Register(new ListComponentSchemasCommand(GetType().Assembly));
        registry.Register(new ScaffoldModuleCommand());

        var catalog = RekallAgeMcpCatalog.FromRegistry(registry);

        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.project.create");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.create_game_from_template");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.run.scene");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.capture.screenshot");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.component_schemas");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.scaffold");
    }
}
