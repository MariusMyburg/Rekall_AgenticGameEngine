using Rekall.Age.Agent;
using Rekall.Age.Agent.Commands;
using Rekall.Age.Assets.Commands;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Project;
using Rekall.Age.Project.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Validation;
using Rekall.Age.World;
using Rekall.Age.World.Commands;
using System.Text.Json.Nodes;

return await RekallAgeCli.RunAsync(args, CancellationToken.None);

internal static class RekallAgeCli
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: rekall-age <game|project|capability|scene|entity|component|asset|run|context|capture|module|build|templates|mcp> ...");
            return 2;
        }

        var registry = BuildRegistry();
        var transaction = RekallAgeTransaction.Begin(string.Join(' ', args));
        var context = new RekallAgeCommandContext("cli", transaction, cancellationToken);

        try
        {
            return args switch
            {
                ["templates", "list"] => ListTemplates(),
                ["mcp", "stdio"] => await RunMcpStdioAsync(registry, context),
                ["asset", "import", var root, var source, var kind, var displayName] =>
                    await ImportAssetAsync(registry, context, root, source, kind, displayName),
                ["asset", "list", var root] => await ListAssetsAsync(registry, context, root, null),
                ["asset", "list", var root, var kind] => await ListAssetsAsync(registry, context, root, kind),
                ["module", "schemas"] => await ListSchemasAsync(registry, context, null),
                ["module", "schemas", var moduleId] => await ListSchemasAsync(registry, context, moduleId),
                ["module", "schemas", "project", var root] => await ListProjectSchemasAsync(registry, context, root),
                ["module", "scaffold", var root, var moduleId, var displayName, var moduleName, var componentName] =>
                    await ScaffoldModuleAsync(registry, context, root, moduleId, displayName, moduleName, componentName),
                ["build", "modules", var root] => await BuildModulesAsync(registry, context, root),
                ["build", "player", var root, var scene] => await BuildPlayerAsync(registry, context, root, scene, graphics: false),
                ["build", "player", var root, var scene, "--graphics"] => await BuildPlayerAsync(registry, context, root, scene, graphics: true),
                ["game", "create", var root, var name, var template] => await CreateGameAsync(registry, context, root, name, template),
                ["project", "create", var root, var name, var capabilities] => await CreateProjectAsync(registry, context, root, name, capabilities),
                ["capability", "add", var root, var capability] => await AddCapabilityAsync(registry, context, root, capability),
                ["scene", "create", var root, var name, var capabilities] => await CreateSceneAsync(registry, context, root, name, capabilities),
                ["entity", "create", var root, var scene, var name, var tags] => await CreateEntityAsync(registry, context, root, scene, name, tags),
                ["entity", "inspect", var root, var scene, var entityId] => await InspectEntityAsync(registry, context, root, scene, entityId),
                ["component", "set", var root, var scene, var entityId, var componentType, var propertyName, var value] =>
                    await SetComponentPropertyAsync(registry, context, root, scene, entityId, componentType, propertyName, value),
                ["play", "scene", var root, var scene, var frames] => await PlaySceneAsync(registry, context, root, scene, frames),
                ["run", "scene", var root, var scene, var seconds] => await RunSceneAsync(registry, context, root, scene, seconds),
                ["context", "summary", var root] => await PrintSummaryAsync(registry, context, root),
                ["context", "scene", var root, var scene] => await PrintSceneSummaryAsync(registry, context, root, scene),
                ["capture", "screenshot", var root, var scene] => await CaptureAsync(registry, context, root, scene),
                _ => PrintUnknown(args)
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static RekallAgeCommandRegistry BuildRegistry()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        registry.Register(new AddCapabilityCommand());
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        registry.Register(new AddComponentCommand());
        registry.Register(new SetComponentPropertyCommand());
        registry.Register(new InspectEntityCommand());
        registry.Register(new CreateGameFromTemplateCommand());
        registry.Register(new ListGameTemplatesCommand());
        registry.Register(new GetProjectSummaryCommand());
        registry.Register(new GetSceneSummaryCommand());
        registry.Register(new ListComponentSchemasCommand());
        registry.Register(new ScaffoldModuleCommand());
        registry.Register(new BuildModulesCommand());
        registry.Register(new BuildPlayerCommand());
        registry.Register(new ImportAssetCommand());
        registry.Register(new ListAssetsCommand());
        registry.Register(new PlaySceneCommand());
        registry.Register(new RunSceneCommand());
        registry.Register(new CaptureScreenshotCommand());
        return registry;
    }

    private static int ListTemplates()
    {
        foreach (var template in RekallAgeGameTemplateCatalog.CreateDefault().Templates)
        {
            Console.WriteLine($"{template.Id}: {template.DisplayName} - {template.Description}");
        }

        return 0;
    }

    private static async Task<int> ImportAssetAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string source,
        string kind,
        string displayName)
    {
        var result = await registry.ExecuteAsync<ImportAssetRequest, ImportAssetResult>(
            "rekall.asset.import",
            new ImportAssetRequest(root, source, kind, displayName),
            context);
        Console.WriteLine($"{result.Value.Asset.Id}: {result.Value.Asset.ImportedPath}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> BuildPlayerAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        bool graphics)
    {
        var result = await registry.ExecuteAsync<BuildPlayerRequest, BuildPlayerResult>(
            "rekall.build.player",
            new BuildPlayerRequest(root, scene, Graphics: graphics),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"{result.Value.LaunchPath} {string.Join(' ', result.Value.Arguments)}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> PlaySceneAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames)
    {
        var count = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<PlaySceneRequest, PlaySceneResult>(
            "rekall.play.scene",
            new PlaySceneRequest(root, scene, count),
            context);
        Console.WriteLine(result.Summary);
        foreach (var frame in result.Value.Frames)
        {
            Console.WriteLine("FRAME");
            Console.Write(frame);
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ListAssetsAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string? kind)
    {
        var result = await registry.ExecuteAsync<ListAssetsRequest, ListAssetsResult>(
            "rekall.asset.list",
            new ListAssetsRequest(root, kind),
            context);
        Console.WriteLine(result.Summary);
        foreach (var asset in result.Value.Assets)
        {
            Console.WriteLine($"{asset.Id}: {asset.Kind}/{asset.Name} -> {asset.ImportedPath}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RunMcpStdioAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context)
    {
        var server = new RekallAgeMcpJsonRpcServer(registry);
        await server.RunStdioAsync(Console.In, Console.Out, context);
        return 0;
    }

    private static async Task<int> ListSchemasAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string? moduleId)
    {
        var result = await registry.ExecuteAsync<ListComponentSchemasRequest, ListComponentSchemasResult>(
            "rekall.module.component_schemas",
            new ListComponentSchemasRequest(moduleId),
            context);
        Console.WriteLine(result.Summary);
        foreach (var component in result.Value.Components)
        {
            Console.WriteLine($"{component.DisplayName}: {component.TypeName}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ListProjectSchemasAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<ListComponentSchemasRequest, ListComponentSchemasResult>(
            "rekall.module.component_schemas",
            new ListComponentSchemasRequest(ProjectRoot: root),
            context);
        Console.WriteLine(result.Summary);
        foreach (var component in result.Value.Components)
        {
            Console.WriteLine($"{component.DisplayName}: {component.TypeName}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ScaffoldModuleAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string moduleId,
        string displayName,
        string moduleName,
        string componentName)
    {
        var result = await registry.ExecuteAsync<ScaffoldModuleRequest, ScaffoldModuleResult>(
            "rekall.module.scaffold",
            new ScaffoldModuleRequest(root, moduleId, displayName, moduleName, componentName),
            context);
        Console.WriteLine(result.Value.SourcePath);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectEntityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId)
    {
        var result = await registry.ExecuteAsync<InspectEntityRequest, InspectEntityResult>(
            "rekall.entity.inspect",
            new InspectEntityRequest(root, scene, entityId),
            context);
        Console.WriteLine($"{result.Value.Entity.Id}: {result.Value.Entity.Name}");
        foreach (var component in result.Value.Entity.Components)
        {
            Console.WriteLine($"{component.Type}: {component.Properties}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> SetComponentPropertyAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string componentType,
        string propertyName,
        string value)
    {
        var result = await registry.ExecuteAsync<SetComponentPropertyRequest, SetComponentPropertyResult>(
            "rekall.component.set_property",
            new SetComponentPropertyRequest(root, scene, entityId, componentType, propertyName, ParseJsonValue(value)),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> BuildModulesAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<BuildModulesRequest, BuildModulesResult>(
            "rekall.build.modules",
            new BuildModulesRequest(root),
            context);
        Console.WriteLine(result.Summary);
        foreach (var module in result.Value.Modules)
        {
            Console.WriteLine($"{module.ModuleName}: {module.AssemblyPath}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateGameAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string name,
        string template)
    {
        var result = await registry.ExecuteAsync<CreateGameFromTemplateRequest, CreateGameFromTemplateResult>(
            "rekall.workflow.create_game_from_template",
            new CreateGameFromTemplateRequest(root, name, template),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateProjectAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string name,
        string capabilities)
    {
        var result = await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new CreateProjectRequest(root, name, SplitCsv(capabilities)),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> AddCapabilityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string capability)
    {
        var result = await registry.ExecuteAsync<AddCapabilityRequest, AddCapabilityResult>(
            "rekall.capability.add",
            new AddCapabilityRequest(root, capability),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateSceneAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string name,
        string capabilities)
    {
        var result = await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create",
            new CreateSceneRequest(root, name, SplitCsv(capabilities)),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateEntityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string name,
        string tags)
    {
        var result = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, scene, name, SplitCsv(tags)),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> PrintSummaryAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<GetProjectSummaryRequest, GetProjectSummaryResult>(
            "rekall.context.project_summary",
            new GetProjectSummaryRequest(root),
            context);
        var summary = result.Value.Summary;

        Console.WriteLine($"{summary.Project}: {summary.Health.Status}");
        foreach (var issue in summary.Health.BlockingIssues)
        {
            Console.WriteLine($"- {issue}");
        }

        return result.Ok && summary.Health.Status == "ok" ? 0 : 1;
    }

    private static async Task<int> PrintSceneSummaryAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene)
    {
        var result = await registry.ExecuteAsync<GetSceneSummaryRequest, GetSceneSummaryResult>(
            "rekall.context.scene_summary",
            new GetSceneSummaryRequest(root, scene),
            context);
        var summary = result.Value.Summary;
        Console.WriteLine($"Scene {summary.Scene}: {summary.EntityCount} entities");
        Console.WriteLine($"Components: {string.Join(", ", summary.ComponentTypes)}");
        foreach (var entity in summary.Entities)
        {
            Console.WriteLine($"- {entity.Name}: {string.Join(", ", entity.Components)}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RunSceneAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string seconds)
    {
        var duration = double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<RunSceneRequest, RunSceneResult>(
            "rekall.run.scene",
            new RunSceneRequest(root, scene, duration),
            context);

        Console.WriteLine($"Simulated {scene}: {result.Value.FramesSimulated} frames");
        Console.WriteLine($"Systems: {string.Join(", ", result.Value.ActiveSystems)}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CaptureAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene)
    {
        var result = await registry.ExecuteAsync<CaptureScreenshotRequest, CaptureScreenshotResult>(
            "rekall.capture.screenshot",
            new CaptureScreenshotRequest(root, scene, Path.Combine(root, "Artifacts", "Screenshots")),
            context);
        Console.WriteLine(result.Value.ScreenshotPath);
        return result.Ok && result.Value.NonBlank ? 0 : 1;
    }

    private static int PrintUnknown(string[] args)
    {
        Console.Error.WriteLine($"Unknown command: {string.Join(' ', args)}");
        return 2;
    }

    private static string[] SplitCsv(string value)
    {
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static JsonNode? ParseJsonValue(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (System.Text.Json.JsonException)
        {
            return JsonValue.Create(value);
        }
    }
}
