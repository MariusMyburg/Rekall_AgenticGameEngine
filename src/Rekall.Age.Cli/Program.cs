using Rekall.Age.Agent;
using Rekall.Age.Agent.Commands;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets.Commands;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.GameTemplates;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Playback;
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
            Console.Error.WriteLine("Usage: rekall-age <game|project|capability|scene|entity|component|asset|level|studio|play|playtest|run|runtime|context|capture|render|module|build|templates|mcp> ...");
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
                ["templates", "inspect", var templateId] => await InspectTemplateAsync(registry, context, templateId),
                ["templates", "verify-mvp"] => await VerifyMvpTemplatesAsync(registry, context, null, cleanup: true),
                ["templates", "verify-mvp", var workRoot] => await VerifyMvpTemplatesAsync(registry, context, workRoot, cleanup: false),
                ["render", "backends"] => await ListRenderBackendsAsync(registry, context),
                ["render", "vulkan", "probe"] => await ProbeVulkanBackendAsync(registry, context),
                ["render", "vulkan", "device", "bootstrap"] =>
                    await BootstrapVulkanLogicalDeviceAsync(registry, context, null),
                ["render", "vulkan", "device", "bootstrap", var preferredDeviceType] =>
                    await BootstrapVulkanLogicalDeviceAsync(registry, context, preferredDeviceType),
                ["render", "vulkan", "command-buffer", "submit-empty"] =>
                    await SubmitEmptyVulkanCommandBufferAsync(registry, context, null),
                ["render", "vulkan", "command-buffer", "submit-empty", var preferredDeviceType] =>
                    await SubmitEmptyVulkanCommandBufferAsync(registry, context, preferredDeviceType),
                ["render", "vulkan", "buffer", "create-mapped"] =>
                    await CreateMappedVulkanBufferAsync(registry, context, "256", "vertex-buffer", null),
                ["render", "vulkan", "buffer", "create-mapped", var sizeBytes, var usage, var preferredDeviceType] =>
                    await CreateMappedVulkanBufferAsync(registry, context, sizeBytes, usage, preferredDeviceType),
                ["render", "vulkan", "image", "create-bound"] =>
                    await CreateBoundVulkanImageAsync(registry, context, "64", "64", "R8G8B8A8_UNorm", "color-attachment", null),
                ["render", "vulkan", "image", "create-bound", var width, var height, var format, var usage, var preferredDeviceType] =>
                    await CreateBoundVulkanImageAsync(registry, context, width, height, format, usage, preferredDeviceType),
                ["render", "vulkan", "render-target", "create"] =>
                    await CreateVulkanRenderTargetAsync(registry, context, "128", "72", "R8G8B8A8_UNorm", null),
                ["render", "vulkan", "render-target", "create", var width, var height, var format, var preferredDeviceType] =>
                    await CreateVulkanRenderTargetAsync(registry, context, width, height, format, preferredDeviceType),
                ["render", "vulkan", "render-pass", "submit-clear"] =>
                    await SubmitClearVulkanRenderPassAsync(registry, context, "128", "72", "R8G8B8A8_UNorm", null, null),
                ["render", "vulkan", "render-pass", "submit-clear", var width, var height, var format, var preferredDeviceType] =>
                    await SubmitClearVulkanRenderPassAsync(registry, context, width, height, format, preferredDeviceType, null),
                ["render", "vulkan", "render-pass", "submit-clear", var width, var height, var format, var preferredDeviceType, var r, var g, var b, var a] =>
                    await SubmitClearVulkanRenderPassAsync(registry, context, width, height, format, preferredDeviceType, ParseClearColor(r, g, b, a)),
                ["render", "vulkan", "render-pass", "read-clear"] =>
                    await ReadClearVulkanRenderPassAsync(registry, context, "64", "64", "R8G8B8A8_UNorm", null, null),
                ["render", "vulkan", "render-pass", "read-clear", var width, var height, var format, var preferredDeviceType] =>
                    await ReadClearVulkanRenderPassAsync(registry, context, width, height, format, preferredDeviceType, null),
                ["render", "vulkan", "render-pass", "read-clear", var width, var height, var format, var preferredDeviceType, var r, var g, var b, var a] =>
                    await ReadClearVulkanRenderPassAsync(registry, context, width, height, format, preferredDeviceType, ParseClearColor(r, g, b, a)),
                ["render", "vulkan", "render-pass", "capture-clear", var outputDirectory] =>
                    await CaptureClearVulkanRenderPassAsync(registry, context, "64", "64", "R8G8B8A8_UNorm", null, outputDirectory, null),
                ["render", "vulkan", "render-pass", "capture-clear", var width, var height, var format, var preferredDeviceType, var outputDirectory] =>
                    await CaptureClearVulkanRenderPassAsync(registry, context, width, height, format, preferredDeviceType, outputDirectory, null),
                ["render", "vulkan", "render-pass", "capture-clear", var width, var height, var format, var preferredDeviceType, var outputDirectory, var r, var g, var b, var a] =>
                    await CaptureClearVulkanRenderPassAsync(registry, context, width, height, format, preferredDeviceType, outputDirectory, ParseClearColor(r, g, b, a)),
                ["render", "plan", "create", var root, var backend, var name] =>
                    await CreateRenderPlanAsync(registry, context, root, backend, name),
                ["render", "plan", "inspect", var root] => await InspectRenderPlanAsync(registry, context, root),
                ["render", "plan", "validate", var root] => await ValidateRenderPlanAsync(registry, context, root),
                ["render", "plan", "execute", var root, var outputDirectory] =>
                    await ExecuteRenderPlanAsync(registry, context, root, outputDirectory),
                ["render", "resource", "add", var root, var id, var kind, var format, var usage] =>
                    await AddRenderResourceAsync(registry, context, root, id, kind, format, usage),
                ["render", "command-buffer", "record", var root, var id, var queue, var commandsJson] =>
                    await RecordRenderCommandBufferAsync(registry, context, root, id, queue, commandsJson),
                ["render", "viewport", "capture", var root, var scene, var frames, var outputDirectory] =>
                    await CaptureRuntimeViewportAsync(registry, context, root, scene, frames, outputDirectory, "320", "180"),
                ["render", "viewport", "capture", var root, var scene, var frames, var outputDirectory, var width, var height] =>
                    await CaptureRuntimeViewportAsync(registry, context, root, scene, frames, outputDirectory, width, height),
                ["mcp", "stdio"] => await RunMcpStdioAsync(registry, context),
                ["studio", "open", var root, var scene] => await OpenStudioModelAsync(root, scene),
                ["asset", "import", var root, var source, var kind, var displayName] =>
                    await ImportAssetAsync(registry, context, root, source, kind, displayName),
                ["asset", "import-report", var root, var source, var kind, var displayName] =>
                    await ImportAssetReportAsync(registry, context, root, source, kind, displayName),
                ["asset", "list", var root] => await ListAssetsAsync(registry, context, root, null),
                ["asset", "list", var root, var kind] => await ListAssetsAsync(registry, context, root, kind),
                ["module", "schemas"] => await ListSchemasAsync(registry, context, null),
                ["module", "schemas", var moduleId] => await ListSchemasAsync(registry, context, moduleId),
                ["module", "schemas", "project", var root] => await ListProjectSchemasAsync(registry, context, root),
                ["module", "sources", var root] => await ListModuleSourcesAsync(registry, context, root),
                ["module", "read-source", var root, var moduleName, var fileName] =>
                    await ReadModuleSourceAsync(registry, context, root, moduleName, fileName),
                ["module", "scaffold", var root, var moduleId, var displayName, var moduleName, var componentName] =>
                    await ScaffoldModuleAsync(registry, context, root, moduleId, displayName, moduleName, componentName),
                ["module", "scaffold-playable", var root, var moduleId, var displayName, var moduleName, var kind] =>
                    await ScaffoldPlayableModuleAsync(registry, context, root, moduleId, displayName, moduleName, kind),
                ["module", "write-source", var root, var moduleName, var fileName, var sourceOrPath] =>
                    await WriteModuleSourceAsync(registry, context, root, moduleName, fileName, sourceOrPath),
                ["build", "modules", var root] => await BuildModulesAsync(registry, context, root),
                ["build", "player", var root, var scene] => await BuildPlayerAsync(registry, context, root, scene, graphics: false),
                ["build", "player", var root, var scene, "--graphics"] => await BuildPlayerAsync(registry, context, root, scene, graphics: true),
                ["game", "create", var root, var name, var template] => await CreateGameAsync(registry, context, root, name, template),
                ["game", "create-playable", var root, var name, var template] => await CreatePlayableGameAsync(registry, context, root, name, template),
                ["game", "create-package-playable", var root, var name, var template] =>
                    await CreatePlayablePackageFromTemplateAsync(registry, context, root, name, template, null, null),
                ["game", "create-package-playable", var root, var name, var template, var outputDirectory] =>
                    await CreatePlayablePackageFromTemplateAsync(registry, context, root, name, template, outputDirectory, null),
                ["game", "create-package-playable", var root, var name, var template, var outputDirectory, var captureOutputDirectory] =>
                    await CreatePlayablePackageFromTemplateAsync(registry, context, root, name, template, outputDirectory, captureOutputDirectory),
                ["game", "verify-playable", var root] => await VerifyPlayableGameAsync(registry, context, root, "Main", "2", null, null, null),
                ["game", "verify-playable", var root, var scene] => await VerifyPlayableGameAsync(registry, context, root, scene, "2", null, null, null),
                ["game", "verify-playable", var root, var scene, var frames] => await VerifyPlayableGameAsync(registry, context, root, scene, frames, null, null, null),
                ["game", "verify-playable", var root, var scene, var frames, var assertionsJson] =>
                    await VerifyPlayableGameAsync(registry, context, root, scene, frames, null, assertionsJson, null),
                ["game", "verify-playable", var root, var scene, var frames, var inputsJson, var assertionsJson] =>
                    await VerifyPlayableGameAsync(registry, context, root, scene, frames, inputsJson, assertionsJson, null),
                ["game", "verify-playable", var root, var scene, var frames, var inputsJson, var assertionsJson, var drawAssertionsJson] =>
                    await VerifyPlayableGameAsync(registry, context, root, scene, frames, inputsJson, assertionsJson, drawAssertionsJson),
                ["game", "package-playable", var root] => await PackagePlayableGameAsync(registry, context, root, "Main", null, graphics: false),
                ["game", "package-playable", var root, var scene] => await PackagePlayableGameAsync(registry, context, root, scene, null, graphics: false),
                ["game", "package-playable", var root, var scene, var outputDirectory] =>
                    await PackagePlayableGameAsync(registry, context, root, scene, outputDirectory, graphics: false),
                ["game", "package-playable", var root, var scene, var outputDirectory, "--graphics"] =>
                    await PackagePlayableGameAsync(registry, context, root, scene, outputDirectory, graphics: true),
                ["game", "inspect-package", var packagePath] => await InspectPlayablePackageAsync(registry, context, packagePath),
                ["game", "audit-package", var packagePath] => await AuditPlayablePackageAsync(registry, context, packagePath, null),
                ["game", "audit-package", var packagePath, var outputDirectory] =>
                    await AuditPlayablePackageAsync(registry, context, packagePath, outputDirectory),
                ["game", "run-package", var packagePath] => await RunPlayablePackageAsync(registry, context, packagePath, "2"),
                ["game", "run-package", var packagePath, var frames] => await RunPlayablePackageAsync(registry, context, packagePath, frames),
                ["game", "capture-package-frame", var packagePath, var outputDirectory] =>
                    await CapturePlayablePackageFrameAsync(registry, context, packagePath, outputDirectory, "1"),
                ["game", "capture-package-frame", var packagePath, var outputDirectory, var frameIndex] =>
                    await CapturePlayablePackageFrameAsync(registry, context, packagePath, outputDirectory, frameIndex),
                ["project", "create", var root, var name, var capabilities] => await CreateProjectAsync(registry, context, root, name, capabilities),
                ["capability", "add", var root, var capability] => await AddCapabilityAsync(registry, context, root, capability),
                ["scene", "create", var root, var name, var capabilities] => await CreateSceneAsync(registry, context, root, name, capabilities),
                ["entity", "create", var root, var scene, var name, var tags] => await CreateEntityAsync(registry, context, root, scene, name, tags),
                ["entity", "inspect", var root, var scene, var entityId] => await InspectEntityAsync(registry, context, root, scene, entityId),
                ["component", "set", var root, var scene, var entityId, var componentType, var propertyName, var value] =>
                    await SetComponentPropertyAsync(registry, context, root, scene, entityId, componentType, propertyName, value),
                ["level", "entity", "duplicate", var root, var scene, var entityId, var name] =>
                    await DuplicateEntityAsync(registry, context, root, scene, entityId, name),
                ["level", "entity", "parent", var root, var scene, var entityId, var parentId] =>
                    await ParentEntityAsync(registry, context, root, scene, entityId, parentId),
                ["level", "prefab", "create", var root, var scene, var entityId, var prefabName] =>
                    await CreatePrefabAsync(registry, context, root, scene, entityId, prefabName),
                ["level", "prefab", "instantiate", var root, var scene, var prefabId, var name] =>
                    await InstantiatePrefabAsync(registry, context, root, scene, prefabId, name),
                ["level", "entity", "snap", var root, var scene, var entityId, var gridSize] =>
                    await SnapEntityAsync(registry, context, root, scene, entityId, gridSize),
                ["play", "scene", var root, var scene, var frames] => await PlaySceneAsync(registry, context, root, scene, frames, null),
                ["play", "scene", var root, var scene, var frames, var inputsJson] => await PlaySceneAsync(registry, context, root, scene, frames, inputsJson),
                ["play", "capture-frame", var root, var scene, var outputDirectory] =>
                    await CapturePlayableFrameAsync(registry, context, root, scene, outputDirectory, "1", null),
                ["play", "capture-frame", var root, var scene, var outputDirectory, var frameIndex] =>
                    await CapturePlayableFrameAsync(registry, context, root, scene, outputDirectory, frameIndex, null),
                ["play", "capture-frame", var root, var scene, var outputDirectory, var frameIndex, var inputsJson] =>
                    await CapturePlayableFrameAsync(registry, context, root, scene, outputDirectory, frameIndex, inputsJson),
                ["playtest", "scene", var root, var scene, var frames, var assertionsJson] =>
                    await PlaytestSceneAsync(registry, context, root, scene, frames, null, assertionsJson, null),
                ["playtest", "scene", var root, var scene, var frames, var inputsJson, var assertionsJson] =>
                    await PlaytestSceneAsync(registry, context, root, scene, frames, inputsJson, assertionsJson, null),
                ["playtest", "scene", var root, var scene, var frames, var inputsJson, var assertionsJson, var drawAssertionsJson] =>
                    await PlaytestSceneAsync(registry, context, root, scene, frames, inputsJson, assertionsJson, drawAssertionsJson),
                ["run", "scene", var root, var scene, var seconds] => await RunSceneAsync(registry, context, root, scene, seconds),
                ["runtime", "inspect", var root, var scene, var frames] => await InspectRuntimeAsync(registry, context, root, scene, frames),
                ["context", "engine"] => await PrintEngineStatusAsync(registry, context),
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
        registry.Register(new CreatePlayableGameFromTemplateCommand());
        registry.Register(new CreatePlayablePackageFromTemplateCommand());
        registry.Register(new InspectGameTemplateCommand());
        registry.Register(new VerifyMvpTemplatesCommand());
        registry.Register(new VerifyPlayableGameCommand());
        registry.Register(new PackagePlayableGameCommand());
        registry.Register(new InspectPlayablePackageCommand());
        registry.Register(new RunPlayablePackageCommand());
        registry.Register(new CapturePlayablePackageFrameCommand());
        registry.Register(new AuditPlayablePackageCommand());
        registry.Register(new ListGameTemplatesCommand());
        registry.Register(new GetProjectSummaryCommand());
        registry.Register(new GetSceneSummaryCommand());
        registry.Register(new GetEngineStatusCommand());
        registry.Register(new ListComponentSchemasCommand());
        registry.Register(new ListModuleSourcesCommand());
        registry.Register(new ReadModuleSourceCommand());
        registry.Register(new ScaffoldModuleCommand());
        registry.Register(new ScaffoldPlayableModuleCommand());
        registry.Register(new WriteModuleSourceCommand());
        registry.Register(new ListRenderBackendsCommand());
        registry.Register(new ProbeVulkanBackendCommand());
        registry.Register(new BootstrapVulkanLogicalDeviceCommand());
        registry.Register(new SubmitEmptyVulkanCommandBufferCommand());
        registry.Register(new CreateMappedVulkanBufferCommand());
        registry.Register(new CreateBoundVulkanImageCommand());
        registry.Register(new CreateVulkanRenderTargetCommand());
        registry.Register(new SubmitClearVulkanRenderPassCommand());
        registry.Register(new ReadClearVulkanRenderPassCommand());
        registry.Register(new CaptureClearVulkanRenderPassCommand());
        registry.Register(new CreateRenderPlanCommand());
        registry.Register(new AddRenderResourceCommand());
        registry.Register(new RecordRenderCommandBufferCommand());
        registry.Register(new InspectRenderPlanCommand());
        registry.Register(new ValidateRenderPlanCommand());
        registry.Register(new ExecuteRenderPlanCommand());
        registry.Register(new BuildModulesCommand());
        registry.Register(new BuildPlayerCommand());
        registry.Register(new ImportAssetCommand());
        registry.Register(new ImportAssetWithReportCommand());
        registry.Register(new ListAssetsCommand());
        registry.Register(new DuplicateEntityCommand());
        registry.Register(new ParentEntityCommand());
        registry.Register(new CreatePrefabFromEntityCommand());
        registry.Register(new InstantiatePrefabCommand());
        registry.Register(new SnapEntityToGridCommand());
        registry.Register(new PlaySceneCommand());
        registry.Register(new PlaytestSceneCommand());
        registry.Register(new RunSceneCommand());
        registry.Register(new InspectSceneRuntimeCommand());
        registry.Register(new CaptureScreenshotCommand());
        registry.Register(new CaptureRuntimeViewportCommand());
        registry.Register(new CapturePlayableFrameCommand());
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

    private static async Task<int> InspectTemplateAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string templateId)
    {
        var result = await registry.ExecuteAsync<InspectGameTemplateRequest, InspectGameTemplateResult>(
            "rekall.templates.inspect",
            new InspectGameTemplateRequest(templateId),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"{result.Value.Template.Id}: {result.Value.Template.DisplayName}");
        Console.WriteLine(result.Value.Template.Description);
        Console.WriteLine($"Capabilities: {string.Join(", ", result.Value.Template.Capabilities)}");
        Console.WriteLine("Draw commands:");
        foreach (var command in result.Value.Template.DrawCommands)
        {
            Console.WriteLine($"  {command.Id}: {command.Kind} - {command.Purpose}");
        }

        Console.WriteLine("Suggested tools:");
        foreach (var command in result.Value.SuggestedCommands)
        {
            Console.WriteLine($"  {command.Tool}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> VerifyMvpTemplatesAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string? workRoot,
        bool cleanup)
    {
        var result = await registry.ExecuteAsync<VerifyMvpTemplatesRequest, VerifyMvpTemplatesResult>(
            "rekall.templates.verify_mvp",
            new VerifyMvpTemplatesRequest(workRoot, Cleanup: cleanup),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        foreach (var template in result.Value.Templates)
        {
            Console.WriteLine($"{template.TemplateId}: {template.Ready} - {template.Summary}");
            Console.WriteLine($"  Frames: {template.FrameCount}; Draw commands: {template.DrawCommandCount}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Ready ? 0 : 1;
    }

    private static async Task<int> ListRenderBackendsAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context)
    {
        var result = await registry.ExecuteAsync<ListRenderBackendsRequest, ListRenderBackendsResult>(
            "rekall.render.backends",
            new ListRenderBackendsRequest(),
            context);
        Console.WriteLine(result.Summary);
        foreach (var backend in result.Value.Backends)
        {
            Console.WriteLine($"{backend.Id}: {backend.DisplayName} [{backend.Status}]");
            Console.WriteLine($"  {string.Join(", ", backend.AgentExposedCapabilities)}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ProbeVulkanBackendAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context)
    {
        var result = await registry.ExecuteAsync<ProbeVulkanBackendRequest, ProbeVulkanBackendResult>(
            "rekall.render.vulkan.probe",
            new ProbeVulkanBackendRequest(),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Available: {result.Value.Available}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        Console.WriteLine($"API: {result.Value.ApiVersion ?? "<unknown>"}");
        Console.WriteLine($"Physical devices: {result.Value.PhysicalDevices.Count}");
        foreach (var device in result.Value.PhysicalDevices)
        {
            Console.WriteLine($"  {device.Name} [{device.DeviceType}] API {device.ApiVersion}");
        }

        Console.WriteLine($"Instance extensions: {result.Value.InstanceExtensions.Count}");
        foreach (var extension in result.Value.InstanceExtensions.Take(12))
        {
            Console.WriteLine($"  {extension}");
        }

        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> BootstrapVulkanLogicalDeviceAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string? preferredDeviceType)
    {
        var result = await registry.ExecuteAsync<BootstrapVulkanLogicalDeviceRequest, BootstrapVulkanLogicalDeviceResult>(
            "rekall.render.vulkan.device.bootstrap",
            new BootstrapVulkanLogicalDeviceRequest(preferredDeviceType),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Available: {result.Value.Available}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        if (result.Value.SelectedDevice is { } device)
        {
            Console.WriteLine($"Selected device: {device.Name} [{device.DeviceType}] API {device.ApiVersion}");
            Console.WriteLine($"Graphics queue family: {device.GraphicsQueueFamily.Index}");
            Console.WriteLine($"Queue capabilities: {string.Join(", ", device.GraphicsQueueFamily.Capabilities)}");
        }

        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> SubmitEmptyVulkanCommandBufferAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string? preferredDeviceType)
    {
        var result = await registry.ExecuteAsync<SubmitEmptyVulkanCommandBufferRequest, SubmitEmptyVulkanCommandBufferResult>(
            "rekall.render.vulkan.command_buffer.submit_empty",
            new SubmitEmptyVulkanCommandBufferRequest(preferredDeviceType),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Submitted: {result.Value.Submitted}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        if (result.Value.SelectedDevice is { } device)
        {
            Console.WriteLine($"Selected device: {device.Name} [{device.DeviceType}] API {device.ApiVersion}");
            Console.WriteLine($"Graphics queue family: {device.GraphicsQueueFamily.Index}");
        }

        Console.WriteLine($"Command pool created: {result.Value.CommandPoolCreated}");
        Console.WriteLine($"Command buffer allocated: {result.Value.CommandBufferAllocated}");
        Console.WriteLine($"Fence signaled: {result.Value.FenceSignaled}");
        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateMappedVulkanBufferAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string sizeBytes,
        string usage,
        string? preferredDeviceType)
    {
        var size = ulong.Parse(sizeBytes, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<CreateMappedVulkanBufferRequest, CreateMappedVulkanBufferResult>(
            "rekall.render.vulkan.buffer.create_mapped",
            new CreateMappedVulkanBufferRequest(size, usage, preferredDeviceType),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Created: {result.Value.Created}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        if (result.Value.SelectedDevice is { } device)
        {
            Console.WriteLine($"Selected device: {device.Name} [{device.DeviceType}] API {device.ApiVersion}");
        }

        Console.WriteLine($"Size bytes: {result.Value.SizeBytes}");
        Console.WriteLine($"Usage: {result.Value.Usage}");
        Console.WriteLine($"Memory type: {result.Value.MemoryTypeIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<none>"}");
        Console.WriteLine($"Memory properties: {string.Join(", ", result.Value.MemoryProperties)}");
        Console.WriteLine($"Bound: {result.Value.Bound}");
        Console.WriteLine($"Mapped: {result.Value.Mapped}");
        Console.WriteLine($"Bytes written: {result.Value.BytesWritten}");
        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateBoundVulkanImageAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string width,
        string height,
        string format,
        string usage,
        string? preferredDeviceType)
    {
        var parsedWidth = uint.Parse(width, System.Globalization.CultureInfo.InvariantCulture);
        var parsedHeight = uint.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<CreateBoundVulkanImageRequest, CreateBoundVulkanImageResult>(
            "rekall.render.vulkan.image.create_bound",
            new CreateBoundVulkanImageRequest(parsedWidth, parsedHeight, format, usage, preferredDeviceType),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Created: {result.Value.Created}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        if (result.Value.SelectedDevice is { } device)
        {
            Console.WriteLine($"Selected device: {device.Name} [{device.DeviceType}] API {device.ApiVersion}");
        }

        Console.WriteLine($"Extent: {result.Value.Width}x{result.Value.Height}");
        Console.WriteLine($"Format: {result.Value.Format}");
        Console.WriteLine($"Usage: {result.Value.Usage}");
        Console.WriteLine($"Memory type: {result.Value.MemoryTypeIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<none>"}");
        Console.WriteLine($"Memory properties: {string.Join(", ", result.Value.MemoryProperties)}");
        Console.WriteLine($"Bound: {result.Value.Bound}");
        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateVulkanRenderTargetAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string width,
        string height,
        string format,
        string? preferredDeviceType)
    {
        var parsedWidth = uint.Parse(width, System.Globalization.CultureInfo.InvariantCulture);
        var parsedHeight = uint.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<CreateVulkanRenderTargetRequest, CreateVulkanRenderTargetResult>(
            "rekall.render.vulkan.render_target.create",
            new CreateVulkanRenderTargetRequest(parsedWidth, parsedHeight, format, preferredDeviceType),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Created: {result.Value.Created}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        if (result.Value.SelectedDevice is { } device)
        {
            Console.WriteLine($"Selected device: {device.Name} [{device.DeviceType}] API {device.ApiVersion}");
        }

        Console.WriteLine($"Extent: {result.Value.Width}x{result.Value.Height}");
        Console.WriteLine($"Format: {result.Value.Format}");
        Console.WriteLine($"Image created: {result.Value.ImageCreated}");
        Console.WriteLine($"Image view created: {result.Value.ImageViewCreated}");
        Console.WriteLine($"Render pass created: {result.Value.RenderPassCreated}");
        Console.WriteLine($"Framebuffer created: {result.Value.FramebufferCreated}");
        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> SubmitClearVulkanRenderPassAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string width,
        string height,
        string format,
        string? preferredDeviceType,
        RekallAgeVulkanClearColor? clearColor)
    {
        var parsedWidth = uint.Parse(width, System.Globalization.CultureInfo.InvariantCulture);
        var parsedHeight = uint.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<SubmitClearVulkanRenderPassRequest, SubmitClearVulkanRenderPassResult>(
            "rekall.render.vulkan.render_pass.submit_clear",
            new SubmitClearVulkanRenderPassRequest(parsedWidth, parsedHeight, format, preferredDeviceType, clearColor),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Submitted: {result.Value.Submitted}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        if (result.Value.SelectedDevice is { } device)
        {
            Console.WriteLine($"Selected device: {device.Name} [{device.DeviceType}] API {device.ApiVersion}");
            Console.WriteLine($"Graphics queue family: {device.GraphicsQueueFamily.Index}");
        }

        Console.WriteLine($"Extent: {result.Value.Width}x{result.Value.Height}");
        Console.WriteLine($"Format: {result.Value.Format}");
        Console.WriteLine($"Clear color: {FormatClearColor(result.Value.ClearColor)}");
        Console.WriteLine($"Image created: {result.Value.ImageCreated}");
        Console.WriteLine($"Image view created: {result.Value.ImageViewCreated}");
        Console.WriteLine($"Render pass created: {result.Value.RenderPassCreated}");
        Console.WriteLine($"Framebuffer created: {result.Value.FramebufferCreated}");
        Console.WriteLine($"Command pool created: {result.Value.CommandPoolCreated}");
        Console.WriteLine($"Command buffer allocated: {result.Value.CommandBufferAllocated}");
        Console.WriteLine($"Render pass began: {result.Value.RenderPassBegan}");
        Console.WriteLine($"Render pass ended: {result.Value.RenderPassEnded}");
        Console.WriteLine($"Fence signaled: {result.Value.FenceSignaled}");
        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ReadClearVulkanRenderPassAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string width,
        string height,
        string format,
        string? preferredDeviceType,
        RekallAgeVulkanClearColor? clearColor)
    {
        var parsedWidth = uint.Parse(width, System.Globalization.CultureInfo.InvariantCulture);
        var parsedHeight = uint.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<ReadClearVulkanRenderPassRequest, ReadClearVulkanRenderPassResult>(
            "rekall.render.vulkan.render_pass.read_clear",
            new ReadClearVulkanRenderPassRequest(parsedWidth, parsedHeight, format, preferredDeviceType, clearColor),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Readback: {result.Value.Readback}");
        Console.WriteLine($"Submitted: {result.Value.Submitted}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        if (result.Value.SelectedDevice is { } device)
        {
            Console.WriteLine($"Selected device: {device.Name} [{device.DeviceType}] API {device.ApiVersion}");
            Console.WriteLine($"Graphics queue family: {device.GraphicsQueueFamily.Index}");
        }

        Console.WriteLine($"Extent: {result.Value.Width}x{result.Value.Height}");
        Console.WriteLine($"Format: {result.Value.Format}");
        Console.WriteLine($"Clear color: {FormatClearColor(result.Value.ClearColor)}");
        Console.WriteLine($"Buffer created: {result.Value.BufferCreated}");
        Console.WriteLine($"Buffer bound: {result.Value.BufferBound}");
        Console.WriteLine($"Buffer mapped: {result.Value.BufferMapped}");
        Console.WriteLine($"Bytes read: {result.Value.BytesRead}");
        Console.WriteLine($"Non-zero bytes: {result.Value.NonZeroBytes}");
        Console.WriteLine($"First pixel: {result.Value.FirstPixel.R},{result.Value.FirstPixel.G},{result.Value.FirstPixel.B},{result.Value.FirstPixel.A}");
        Console.WriteLine($"Byte checksum: {result.Value.ByteChecksum}");
        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CaptureClearVulkanRenderPassAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string width,
        string height,
        string format,
        string? preferredDeviceType,
        string outputDirectory,
        RekallAgeVulkanClearColor? clearColor)
    {
        var parsedWidth = uint.Parse(width, System.Globalization.CultureInfo.InvariantCulture);
        var parsedHeight = uint.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<CaptureClearVulkanRenderPassRequest, CaptureClearVulkanRenderPassResult>(
            "rekall.render.vulkan.render_pass.capture_clear",
            new CaptureClearVulkanRenderPassRequest(parsedWidth, parsedHeight, format, preferredDeviceType, outputDirectory, clearColor),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Captured: {result.Value.Captured}");
        Console.WriteLine($"Output: {result.Value.OutputPath}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        if (result.Value.SelectedDevice is { } device)
        {
            Console.WriteLine($"Selected device: {device.Name} [{device.DeviceType}] API {device.ApiVersion}");
            Console.WriteLine($"Graphics queue family: {device.GraphicsQueueFamily.Index}");
        }

        Console.WriteLine($"Extent: {result.Value.Width}x{result.Value.Height}");
        Console.WriteLine($"Format: {result.Value.Format}");
        Console.WriteLine($"Clear color: {FormatClearColor(result.Value.ClearColor)}");
        Console.WriteLine($"Bytes read: {result.Value.BytesRead}");
        Console.WriteLine($"Non-zero bytes: {result.Value.NonZeroBytes}");
        Console.WriteLine($"First pixel: {result.Value.FirstPixel.R},{result.Value.FirstPixel.G},{result.Value.FirstPixel.B},{result.Value.FirstPixel.A}");
        Console.WriteLine($"Byte checksum: {result.Value.ByteChecksum}");
        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ValidateRenderPlanAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<ValidateRenderPlanRequest, ValidateRenderPlanResult>(
            "rekall.render.plan.validate",
            new ValidateRenderPlanRequest(root),
            context);
        Console.WriteLine(result.Summary);
        foreach (var issue in result.Value.Issues)
        {
            Console.WriteLine($"{issue.Code}: {issue.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateRenderPlanAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string backend,
        string name)
    {
        var result = await registry.ExecuteAsync<CreateRenderPlanRequest, CreateRenderPlanResult>(
            "rekall.render.plan.create",
            new CreateRenderPlanRequest(root, backend, name),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> AddRenderResourceAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string id,
        string kind,
        string format,
        string usage)
    {
        var result = await registry.ExecuteAsync<AddRenderResourceRequest, AddRenderResourceResult>(
            "rekall.render.resource.add",
            new AddRenderResourceRequest(root, id, kind, format, SplitCsv(usage)),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectRenderPlanAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<InspectRenderPlanRequest, InspectRenderPlanResult>(
            "rekall.render.plan.inspect",
            new InspectRenderPlanRequest(root),
            context);
        var plan = result.Value.Plan;
        Console.WriteLine($"{plan.Name}: {plan.BackendId}");
        Console.WriteLine($"Resources: {plan.Resources.Count}");
        Console.WriteLine($"Pipelines: {plan.Pipelines.Count}");
        Console.WriteLine($"Command buffers: {plan.CommandBuffers.Count}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ExecuteRenderPlanAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string outputDirectory)
    {
        var result = await registry.ExecuteAsync<ExecuteRenderPlanRequest, ExecuteRenderPlanResult>(
            "rekall.render.plan.execute",
            new ExecuteRenderPlanRequest(root, outputDirectory),
            context);
        Console.WriteLine(result.Summary);
        if (result.Ok)
        {
            Console.WriteLine($"{result.Value.OutputPath} ({result.Value.Width}x{result.Value.Height}, nonblank={result.Value.NonBlank})");
        }
        else
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"{error.Code}: {error.Message}");
            }
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RecordRenderCommandBufferAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string id,
        string queue,
        string commandsJson)
    {
        var payload = File.Exists(commandsJson)
            ? await File.ReadAllTextAsync(commandsJson, context.CancellationToken)
            : commandsJson;
        RekallAgeRenderCommand[] commands;
        try
        {
            commands = System.Text.Json.JsonSerializer.Deserialize<RekallAgeRenderCommand[]>(
                payload,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? [];
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.Error.WriteLine($"Render commands JSON is invalid: {ex.Message}");
            return 1;
        }

        var result = await registry.ExecuteAsync<RecordRenderCommandBufferRequest, RecordRenderCommandBufferResult>(
            "rekall.render.command_buffer.record",
            new RecordRenderCommandBufferRequest(root, id, queue, commands),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
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

    private static async Task<int> OpenStudioModelAsync(string root, string scene)
    {
        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, scene, CancellationToken.None);
        Console.WriteLine($"{model.Project.Name}: {model.Scene.Name}");
        Console.WriteLine($"Root entities: {model.Scene.RootEntities.Count}");
        Console.WriteLine($"Assets: {model.Assets.Assets.Count}");
        Console.WriteLine($"Diagnostics: {model.Diagnostics.Issues.Count}");
        return 0;
    }

    private static async Task<int> ImportAssetReportAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string source,
        string kind,
        string displayName)
    {
        var result = await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report",
            new ImportAssetWithReportRequest(root, source, kind, displayName),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Imported: {result.Value.Report.Imported}");
        Console.WriteLine($"Asset: {result.Value.Report.AssetId}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> DuplicateEntityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string name)
    {
        var result = await registry.ExecuteAsync<DuplicateEntityRequest, DuplicateEntityResult>(
            "rekall.level.entity.duplicate",
            new DuplicateEntityRequest(root, scene, entityId, name),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EntityId);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ParentEntityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string parentId)
    {
        var normalizedParent = parentId.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : parentId;
        var result = await registry.ExecuteAsync<ParentEntityRequest, ParentEntityResult>(
            "rekall.level.entity.parent",
            new ParentEntityRequest(root, scene, entityId, normalizedParent),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreatePrefabAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string prefabName)
    {
        var result = await registry.ExecuteAsync<CreatePrefabFromEntityRequest, CreatePrefabFromEntityResult>(
            "rekall.level.prefab.create_from_entity",
            new CreatePrefabFromEntityRequest(root, scene, entityId, prefabName),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.PrefabId);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InstantiatePrefabAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string prefabId,
        string name)
    {
        var result = await registry.ExecuteAsync<InstantiatePrefabRequest, InstantiatePrefabResult>(
            "rekall.level.prefab.instantiate",
            new InstantiatePrefabRequest(root, scene, prefabId, name),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EntityId);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> SnapEntityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string gridSize)
    {
        var result = await registry.ExecuteAsync<SnapEntityToGridRequest, SnapEntityToGridResult>(
            "rekall.level.entity.snap_to_grid",
            new SnapEntityToGridRequest(
                root,
                scene,
                entityId,
                double.Parse(gridSize, System.Globalization.CultureInfo.InvariantCulture)),
            context);
        Console.WriteLine(result.Summary);
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
        string frames,
        string? inputsJson)
    {
        var count = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var inputs = await ParsePlaybackInputsAsync(inputsJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<PlaySceneRequest, PlaySceneResult>(
            "rekall.play.scene",
            new PlaySceneRequest(root, scene, count, inputs),
            context);
        Console.WriteLine(result.Summary);
        foreach (var frame in result.Value.Frames)
        {
            Console.WriteLine("FRAME");
            Console.Write(frame);
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> PlaytestSceneAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string? inputsJson,
        string assertionsJson,
        string? drawAssertionsJson)
    {
        var count = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var inputs = await ParsePlaybackInputsAsync(inputsJson, context.CancellationToken);
        var assertions = await ParseFrameAssertionsAsync(assertionsJson, context.CancellationToken);
        var drawAssertions = await ParseDrawAssertionsAsync(drawAssertionsJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<PlaytestSceneRequest, PlaytestSceneResult>(
            "rekall.playtest.scene",
            new PlaytestSceneRequest(root, scene, count, inputs, assertions, drawAssertions),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Passed: {result.Value.Passed}");
        Console.WriteLine($"Kind: {result.Value.Kind}");
        Console.WriteLine($"Frames: {result.Value.Frames.Count}");
        foreach (var assertion in result.Value.Assertions)
        {
            var expected = assertion.MustContain ? "contains" : "does not contain";
            Console.WriteLine($"Assertion frame {assertion.FrameIndex} {expected} \"{assertion.Contains}\": {assertion.Passed}");
        }

        foreach (var assertion in result.Value.DrawAssertions)
        {
            var id = assertion.Id ?? "<any>";
            var kind = assertion.Kind ?? "<any>";
            var text = assertion.TextContains ?? "<any>";
            Console.WriteLine($"Draw assertion frame {assertion.FrameIndex} id={id} kind={kind} text={text}: {assertion.Passed}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CapturePlayableFrameAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string outputDirectory,
        string frameIndex,
        string? inputsJson)
    {
        var parsedFrameIndex = int.Parse(frameIndex, System.Globalization.CultureInfo.InvariantCulture);
        var inputs = await ParsePlaybackInputsAsync(inputsJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<CapturePlayableFrameRequest, CapturePlayableFrameResult>(
            "rekall.play.capture_frame",
            new CapturePlayableFrameRequest(root, scene, outputDirectory, parsedFrameIndex, Inputs: inputs),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"{result.Value.OutputPath} ({result.Value.Width}x{result.Value.Height}, nonblank={result.Value.NonBlank})");
        Console.WriteLine($"Draw commands: {result.Value.DrawCommandCount} [{string.Join(", ", result.Value.DrawCommandKinds)}]");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Captured && result.Value.NonBlank ? 0 : 1;
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

    private static async Task<int> ListModuleSourcesAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<ListModuleSourcesRequest, ListModuleSourcesResult>(
            "rekall.module.list_sources",
            new ListModuleSourcesRequest(root),
            context);
        Console.WriteLine(result.Summary);
        foreach (var source in result.Value.Sources)
        {
            Console.WriteLine($"{source.ModuleName}/{source.FileName}: {source.SourcePath} ({source.Bytes} bytes)");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ReadModuleSourceAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string moduleName,
        string fileName)
    {
        var result = await registry.ExecuteAsync<ReadModuleSourceRequest, ReadModuleSourceResult>(
            "rekall.module.read_source",
            new ReadModuleSourceRequest(root, moduleName, fileName),
            context);
        Console.WriteLine(result.Summary);
        if (result.Ok)
        {
            Console.Write(result.Value.Source);
        }
        else
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"{error.Code}: {error.Message}");
            }
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

    private static async Task<int> ScaffoldPlayableModuleAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string moduleId,
        string displayName,
        string moduleName,
        string kind)
    {
        var result = await registry.ExecuteAsync<ScaffoldPlayableModuleRequest, ScaffoldPlayableModuleResult>(
            "rekall.module.scaffold_playable",
            new ScaffoldPlayableModuleRequest(root, moduleId, displayName, moduleName, kind),
            context);
        Console.WriteLine(result.Value.SourcePath);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> WriteModuleSourceAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string moduleName,
        string fileName,
        string sourceOrPath)
    {
        var source = File.Exists(sourceOrPath)
            ? await File.ReadAllTextAsync(sourceOrPath, context.CancellationToken)
            : sourceOrPath;
        var result = await registry.ExecuteAsync<WriteModuleSourceRequest, WriteModuleSourceResult>(
            "rekall.module.write_source",
            new WriteModuleSourceRequest(root, moduleName, fileName, source),
            context);
        Console.WriteLine(result.Summary);
        if (result.Ok)
        {
            Console.WriteLine($"{result.Value.SourcePath} ({result.Value.BytesWritten} bytes)");
        }
        else
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"{error.Code}: {error.Message}");
            }
        }

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

    private static async Task<int> CreatePlayableGameAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string name,
        string template)
    {
        var result = await registry.ExecuteAsync<CreatePlayableGameFromTemplateRequest, CreatePlayableGameFromTemplateResult>(
            "rekall.workflow.create_playable_game_from_template",
            new CreatePlayableGameFromTemplateRequest(root, name, template),
            context);
        Console.WriteLine(result.Summary);
        if (result.Ok)
        {
            Console.WriteLine($"Module source: {result.Value.ModuleSourcePath}");
            Console.WriteLine($"Module assembly: {result.Value.ModuleAssemblyPath}");
        }
        else
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"{error.Code}: {error.Message}");
            }
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreatePlayablePackageFromTemplateAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string name,
        string template,
        string? outputDirectory,
        string? captureOutputDirectory)
    {
        var result = await registry.ExecuteAsync<CreatePlayablePackageFromTemplateRequest, CreatePlayablePackageFromTemplateResult>(
            "rekall.workflow.create_playable_package_from_template",
            new CreatePlayablePackageFromTemplateRequest(
                root,
                name,
                template,
                OutputDirectory: outputDirectory,
                CaptureOutputDirectory: captureOutputDirectory),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        Console.WriteLine($"Template: {result.Value.TemplateId}");
        Console.WriteLine($"Archive: {result.Value.Package.ArchivePath}");
        Console.WriteLine($"Manifest: {result.Value.Package.ManifestPath}");
        Console.WriteLine($"Run exit code: {result.Value.Run.ExitCode}");
        Console.WriteLine($"Capture: {result.Value.Capture.OutputPath}");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Ready ? 0 : 1;
    }

    private static async Task<int> VerifyPlayableGameAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string? inputsJson,
        string? assertionsJson,
        string? drawAssertionsJson)
    {
        var count = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var inputs = await ParsePlaybackInputsAsync(inputsJson, context.CancellationToken);
        var assertions = await ParseFrameAssertionsAsync(assertionsJson, context.CancellationToken);
        var drawAssertions = await ParseDrawAssertionsAsync(drawAssertionsJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<VerifyPlayableGameRequest, VerifyPlayableGameResult>(
            "rekall.workflow.verify_playable_game",
            new VerifyPlayableGameRequest(root, scene, count, inputs, assertions, drawAssertions),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        foreach (var check in result.Value.Checks)
        {
            Console.WriteLine($"{check.Name}: {check.Passed} - {check.Summary}");
        }

        foreach (var assertion in result.Value.DrawAssertions)
        {
            var id = assertion.Id ?? "<any>";
            var kind = assertion.Kind ?? "<any>";
            var text = assertion.TextContains ?? "<any>";
            Console.WriteLine($"Draw assertion frame {assertion.FrameIndex} id={id} kind={kind} text={text}: {assertion.Passed}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> PackagePlayableGameAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string? outputDirectory,
        bool graphics)
    {
        var result = await registry.ExecuteAsync<PackagePlayableGameRequest, PackagePlayableGameResult>(
            "rekall.workflow.package_playable_game",
            new PackagePlayableGameRequest(root, scene, outputDirectory, Graphics: graphics),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        if (result.Ok)
        {
            Console.WriteLine($"Launch: {result.Value.LaunchPath}");
            Console.WriteLine($"Manifest: {result.Value.ManifestPath}");
            Console.WriteLine($"Archive: {result.Value.ArchivePath}");
            Console.WriteLine($"Arguments: {string.Join(' ', result.Value.Arguments)}");
        }
        else
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"{error.Code}: {error.Message}");
            }
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectPlayablePackageAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string packagePath)
    {
        var result = await registry.ExecuteAsync<InspectPlayablePackageRequest, InspectPlayablePackageResult>(
            "rekall.workflow.inspect_playable_package",
            new InspectPlayablePackageRequest(packagePath),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        Console.WriteLine($"Template: {result.Value.Manifest.SourceTemplateId ?? "<none>"}");
        Console.WriteLine($"Scene: {result.Value.Manifest.SceneName}");
        Console.WriteLine($"Launch: {result.Value.Manifest.LaunchPath}");
        Console.WriteLine($"Draw commands: {result.Value.Manifest.DrawCommands.Count}");
        Console.WriteLine($"Draw assertions: {result.Value.Manifest.DrawAssertions.Count}");
        Console.WriteLine($"Files: {result.Value.FileCount}");
        Console.WriteLine("Key artifacts:");
        foreach (var artifact in result.Value.KeyArtifacts)
        {
            Console.WriteLine($"  {artifact}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Ready ? 0 : 1;
    }

    private static async Task<int> RunPlayablePackageAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string packagePath,
        string frames)
    {
        var frameCount = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<RunPlayablePackageRequest, RunPlayablePackageResult>(
            "rekall.workflow.run_playable_package",
            new RunPlayablePackageRequest(packagePath, frameCount),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        Console.WriteLine($"Launch: {result.Value.LaunchPath}");
        Console.WriteLine($"Game: {result.Value.GameRoot}");
        Console.WriteLine($"Exit code: {result.Value.ExitCode}");
        Console.Write(result.Value.Output);
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Ready ? 0 : 1;
    }

    private static async Task<int> AuditPlayablePackageAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string packagePath,
        string? outputDirectory)
    {
        var result = await registry.ExecuteAsync<AuditPlayablePackageRequest, AuditPlayablePackageResult>(
            "rekall.workflow.audit_playable_package",
            new AuditPlayablePackageRequest(packagePath, outputDirectory),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        Console.WriteLine($"Files: {result.Value.Inspection.FileCount}");
        Console.WriteLine($"Missing key artifacts: {result.Value.MissingKeyArtifacts.Count}");
        foreach (var missing in result.Value.MissingKeyArtifacts)
        {
            Console.WriteLine($"  {missing}");
        }

        Console.WriteLine($"Run exit code: {result.Value.Run.ExitCode}");
        Console.WriteLine($"Captured: {result.Value.Capture.Captured}");
        Console.WriteLine($"Capture: {result.Value.Capture.OutputPath}");
        Console.WriteLine($"Non-blank: {result.Value.Capture.NonBlank}");
        foreach (var check in result.Value.Checks)
        {
            Console.WriteLine($"{check.Name}: {check.Passed} - {check.Summary}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Ready ? 0 : 1;
    }

    private static async Task<int> CapturePlayablePackageFrameAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string packagePath,
        string outputDirectory,
        string frameIndex)
    {
        var frame = int.Parse(frameIndex, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<CapturePlayablePackageFrameRequest, CapturePlayablePackageFrameResult>(
            "rekall.workflow.capture_playable_package_frame",
            new CapturePlayablePackageFrameRequest(packagePath, outputDirectory, frame),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Captured: {result.Value.Captured}");
        Console.WriteLine($"Output: {result.Value.OutputPath}");
        Console.WriteLine($"Kind: {result.Value.Kind}");
        Console.WriteLine($"Frame: {result.Value.FrameIndex}");
        Console.WriteLine($"Non-blank: {result.Value.NonBlank}");
        Console.WriteLine($"Draw commands: {result.Value.DrawCommandCount}");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Captured && result.Value.NonBlank ? 0 : 1;
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

    private static async Task<int> PrintEngineStatusAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context)
    {
        var result = await registry.ExecuteAsync<GetEngineStatusRequest, GetEngineStatusResult>(
            "rekall.context.engine_status",
            new GetEngineStatusRequest(),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EngineName);
        Console.WriteLine($"Agent-first: {result.Value.AgentFirst}");
        Console.WriteLine($"Rendering: {result.Value.RenderingPosture}");
        Console.WriteLine($"MVP templates: {string.Join(", ", result.Value.MvpTemplateIds)}");
        Console.WriteLine("Workflow tools:");
        foreach (var workflow in result.Value.WorkflowTools)
        {
            var marker = workflow.Recommended ? "recommended" : "available";
            Console.WriteLine($"  {workflow.Tool} [{marker}] - {workflow.Purpose}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
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

    private static async Task<int> InspectRuntimeAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames)
    {
        var frameCount = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<InspectSceneRuntimeRequest, InspectSceneRuntimeResult>(
            "rekall.runtime.inspect_scene",
            new InspectSceneRuntimeRequest(root, scene, frameCount),
            context);

        Console.WriteLine(result.Summary);
        Console.WriteLine($"Entities: {result.Value.EntityCount}");
        Console.WriteLine($"Renderable: {result.Value.RenderableCount}");
        Console.WriteLine($"Physics bodies: {result.Value.PhysicsBodyCount}");
        Console.WriteLine($"Physics colliders: {result.Value.PhysicsColliderCount}");
        Console.WriteLine($"Audio: {result.Value.AudioListenerCount} listeners, {result.Value.AudioEmitterCount} emitters");
        Console.WriteLine($"Animation players: {result.Value.AnimationPlayerCount}");
        Console.WriteLine($"UI elements: {result.Value.UiElementCount}");
        foreach (var observation in result.Value.Observations)
        {
            Console.WriteLine($"{observation.Severity} {observation.Code} {observation.Subsystem} {observation.TargetId}: {observation.Message}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CaptureRuntimeViewportAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string outputDirectory,
        string width,
        string height)
    {
        var frameCount = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var viewportWidth = int.Parse(width, System.Globalization.CultureInfo.InvariantCulture);
        var viewportHeight = int.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<CaptureRuntimeViewportRequest, CaptureRuntimeViewportResult>(
            "rekall.render.capture_runtime_viewport",
            new CaptureRuntimeViewportRequest(root, scene, frameCount, outputDirectory, viewportWidth, viewportHeight, true),
            context);

        Console.WriteLine($"Runtime viewport {scene} frame {result.Value.FrameIndex}: {result.Value.Width}x{result.Value.Height}");
        Console.WriteLine(result.Value.ScreenshotPath);
        Console.WriteLine($"Active camera: {result.Value.ActiveCamera ?? "(none)"}");
        Console.WriteLine($"Renderable: {result.Value.RenderableCount}");
        Console.WriteLine($"Renderable kinds: {string.Join(", ", result.Value.RenderableKinds)}");
        Console.WriteLine($"Asset-backed: {result.Value.AssetBackedRenderableCount}");
        Console.WriteLine($"Fallback: {result.Value.FallbackRenderableCount}");
        Console.WriteLine($"Missing assets: {result.Value.MissingAssetCount}");
        Console.WriteLine($"Unsupported assets: {result.Value.UnsupportedAssetCount}");
        foreach (var code in result.Value.AssetIssueCodes)
        {
            Console.WriteLine($"Asset issue: {code}");
        }

        Console.WriteLine($"Observations: {result.Value.ObservationCount}");
        foreach (var code in result.Value.ObservationCodes)
        {
            Console.WriteLine($"Observation: {code}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Captured && result.Value.NonBlank ? 0 : 1;
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

    private static RekallAgeVulkanClearColor ParseClearColor(string r, string g, string b, string a)
    {
        return new RekallAgeVulkanClearColor(
            float.Parse(r, System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(g, System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(b, System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(a, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string FormatClearColor(RekallAgeVulkanClearColor color)
    {
        return string.Join(
            ',',
            color.R.ToString(System.Globalization.CultureInfo.InvariantCulture),
            color.G.ToString(System.Globalization.CultureInfo.InvariantCulture),
            color.B.ToString(System.Globalization.CultureInfo.InvariantCulture),
            color.A.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async ValueTask<IReadOnlyList<RekallAgePlaybackInput>?> ParsePlaybackInputsAsync(
        string? inputsJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputsJson))
        {
            return null;
        }

        var payload = File.Exists(inputsJson)
            ? await File.ReadAllTextAsync(inputsJson, cancellationToken)
            : inputsJson;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<RekallAgePlaybackInput[]>(
                payload,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ArgumentException($"Playback inputs JSON is invalid: {ex.Message}", ex);
        }
    }

    private static async ValueTask<IReadOnlyList<RekallAgeFrameAssertion>?> ParseFrameAssertionsAsync(
        string? assertionsJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assertionsJson))
        {
            return null;
        }

        var payload = File.Exists(assertionsJson)
            ? await File.ReadAllTextAsync(assertionsJson, cancellationToken)
            : assertionsJson;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<RekallAgeFrameAssertion[]>(
                payload,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ArgumentException($"Frame assertions JSON is invalid: {ex.Message}", ex);
        }
    }

    private static async ValueTask<IReadOnlyList<RekallAgeDrawCommandAssertion>?> ParseDrawAssertionsAsync(
        string? assertionsJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assertionsJson))
        {
            return null;
        }

        var payload = File.Exists(assertionsJson)
            ? await File.ReadAllTextAsync(assertionsJson, cancellationToken)
            : assertionsJson;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<RekallAgeDrawCommandAssertion[]>(
                payload,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ArgumentException($"Draw assertions JSON is invalid: {ex.Message}", ex);
        }
    }
}
