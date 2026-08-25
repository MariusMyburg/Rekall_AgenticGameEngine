using Rekall.Age.Agent;
using Rekall.Age.Agent.Commands;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets.Commands;
using Rekall.Age.Build.Commands;
using Rekall.Age.Build.Distribution;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Modules.Sdk;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Project;
using Rekall.Age.Project.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Validation;
using Rekall.Age.Validation.Commands;
using Rekall.Age.Workflows;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.World;
using Rekall.Age.World.Commands;
using System.Globalization;
using System.Text.Json.Nodes;
using Serilog;
using Serilog.Events;

return await RekallAgeCli.RunAsync(args, CancellationToken.None);

internal static class RekallAgeCli
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var logDirectory = ConfigureLogging(args);
        Log.Information("Rekall AGE command starting. Args={Args}", DescribeInvocation(args));
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: rekall-age <agent|game|project|capability|scene|entity|component|input|shader|asset|geometry|mesh|level|studio|play|playtest|run|runtime|multiplayer|context|compatibility|recovery|diagnostics|transaction|capture|render|module|build|validation|command|mcp> ...");
            Log.Information("Rekall AGE command finished with usage error. LogDirectory={LogDirectory}", logDirectory);
            Log.CloseAndFlush();
            return 2;
        }

        try
        {
            var registry = BuildRegistry();
            var transaction = RekallAgeTransaction.Begin(DescribeInvocation(args));
            var context = new RekallAgeCommandContext(IsMcpStdio(args) ? "mcp" : "cli", transaction, cancellationToken);
            var exitCode = args switch
            {
                ["agent", "providers"] => ListLanguageModelProviders(),
                ["agent", "models", var provider] =>
                    await ListLanguageModelsAsync(registry, provider, cancellationToken),
                ["agent", "run", var provider, var model, var task] =>
                    await RunLanguageModelAgentAsync(registry, provider, model, task, "24", cancellationToken),
                ["agent", "run", var provider, var model, var task, var maxTurns] =>
                    await RunLanguageModelAgentAsync(registry, provider, model, task, maxTurns, cancellationToken),
                ["agent", "run-project", var provider, var model, var root, var scene, var task] =>
                    await RunProjectLanguageModelAgentAsync(registry, provider, model, root, scene, task, "24", cancellationToken),
                ["agent", "run-project", var provider, var model, var root, var scene, var task, var maxTurns] =>
                    await RunProjectLanguageModelAgentAsync(registry, provider, model, root, scene, task, maxTurns, cancellationToken),
                ["distribution", "assemble", var output, var cli, var studio, var headless, var windows, var sdk, var readme, var notice, var thirdParty] =>
                    await AssembleDistributionAsync(
                        registry, context, output, cli, studio, headless, windows, sdk, readme, notice, thirdParty),
                ["render", "backends"] => await ListRenderBackendsAsync(registry, context),
                ["render", "device", "inspect-workload", var workloadJson] =>
                    await InspectRenderingDeviceWorkloadAsync(registry, context, workloadJson),
                ["render", "device", "inspect-runtime-workload", var workloadJson] =>
                    await InspectRuntimeGpuWorkloadAsync(registry, context, workloadJson),
                ["shader", "inspect-pipeline", var root, var vertex, var fragment] =>
                    await InspectShaderPipelineAsync(registry, context, root, vertex, fragment),
                ["shader", "write", var root, var name, var stage, var source] =>
                    await WriteShaderSourceAsync(registry, context, root, name, stage, source),
                ["shader", "write-include", var root, var name, var source] =>
                    await WriteShaderIncludeAsync(registry, context, root, name, source),
                ["shader", "preprocess", var root, var name, var stage] =>
                    await PreprocessShaderSourceAsync(registry, context, root, name, stage),
                ["shader", "assign-pipeline", var root, var scene, var entityId, var vertex, var fragment] =>
                    await AssignShaderPipelineAsync(registry, context, root, scene, entityId, vertex, fragment),
                ["render", "mesh", "inspect", var root, var scene] =>
                    await InspectSceneMeshGeometryAsync(registry, context, root, scene, "0"),
                ["render", "mesh", "inspect", var root, var scene, var frames] =>
                    await InspectSceneMeshGeometryAsync(registry, context, root, scene, frames),
                ["render", "stereo", "inspect", var root, var scene] =>
                    await InspectStereoRenderPlanAsync(registry, context, root, scene, "0", "1920", "1080"),
                ["render", "stereo", "inspect", var root, var scene, var frames] =>
                    await InspectStereoRenderPlanAsync(registry, context, root, scene, frames, "1920", "1080"),
                ["render", "stereo", "inspect", var root, var scene, var frames, var width, var height] =>
                    await InspectStereoRenderPlanAsync(registry, context, root, scene, frames, width, height),
                ["render", "performance", "budget", var root, var scene] =>
                    await InspectScenePerformanceBudgetAsync(registry, context, root, scene, "0", "1920", "1080", "desktop60"),
                ["render", "performance", "budget", var root, var scene, var profile] =>
                    await InspectScenePerformanceBudgetAsync(registry, context, root, scene, "0", "1920", "1080", profile),
                ["render", "performance", "budget", var root, var scene, var profile, var frames] =>
                    await InspectScenePerformanceBudgetAsync(registry, context, root, scene, frames, "1920", "1080", profile),
                ["render", "performance", "budget", var root, var scene, var profile, var frames, var width, var height] =>
                    await InspectScenePerformanceBudgetAsync(registry, context, root, scene, frames, width, height, profile),
                ["render", "performance", "budget", var root, var scene, var profile, var frames, var width, var height, var qualityPreset, var overridesJson, var includeGpuTimings] =>
                    await InspectScenePerformanceBudgetAsync(
                        registry,
                        context,
                        root,
                        scene,
                        frames,
                        width,
                        height,
                        profile,
                        qualityPreset,
                        overridesJson,
                        includeGpuTimings),
                ["render", "virtual-geometry", "inspect", var root, var scene] =>
                    await InspectVirtualGeometrySceneAsync(registry, context, root, scene, "0", "1920", "1080"),
                ["render", "virtual-geometry", "inspect", var root, var scene, var frames] =>
                    await InspectVirtualGeometrySceneAsync(registry, context, root, scene, frames, "1920", "1080"),
                ["render", "virtual-geometry", "inspect", var root, var scene, var frames, var width, var height] =>
                    await InspectVirtualGeometrySceneAsync(registry, context, root, scene, frames, width, height),
                ["render", "virtual-geometry", "apply", var root, var scene] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, "10000", "1920", "1080", dryRun: false),
                ["render", "virtual-geometry", "apply", var root, var scene, "--dry-run"] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, "10000", "1920", "1080", dryRun: true),
                ["render", "virtual-geometry", "apply", var root, var scene, var minSourceTriangles] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, minSourceTriangles, "1920", "1080", dryRun: false),
                ["render", "virtual-geometry", "apply", var root, var scene, var minSourceTriangles, "--dry-run"] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, minSourceTriangles, "1920", "1080", dryRun: true),
                ["render", "virtual-geometry", "apply", var root, var scene, var minSourceTriangles, var width, var height] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, minSourceTriangles, width, height, dryRun: false),
                ["render", "virtual-geometry", "apply", var root, var scene, var minSourceTriangles, var width, var height, "--dry-run"] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, minSourceTriangles, width, height, dryRun: true),
                ["render", "virtual-geometry", "apply-entity", var root, var scene, var entityName] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, "10000", "1920", "1080", dryRun: false, entityName),
                ["render", "virtual-geometry", "apply-entity", var root, var scene, var entityName, "--dry-run"] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, "10000", "1920", "1080", dryRun: true, entityName),
                ["render", "virtual-geometry", "apply-entity", var root, var scene, var entityName, var minSourceTriangles] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, minSourceTriangles, "1920", "1080", dryRun: false, entityName),
                ["render", "virtual-geometry", "apply-entity", var root, var scene, var entityName, var minSourceTriangles, "--dry-run"] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, minSourceTriangles, "1920", "1080", dryRun: true, entityName),
                ["render", "virtual-geometry", "apply-entity", var root, var scene, var entityName, var minSourceTriangles, var width, var height] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, minSourceTriangles, width, height, dryRun: false, entityName),
                ["render", "virtual-geometry", "apply-entity", var root, var scene, var entityName, var minSourceTriangles, var width, var height, "--dry-run"] =>
                    await ApplyVirtualGeometryToSceneAsync(registry, context, root, scene, minSourceTriangles, width, height, dryRun: true, entityName),
                ["render", "visibility", "inspect", var root, var scene] =>
                    await InspectSceneVisibilityAsync(registry, context, root, scene, "0"),
                ["render", "visibility", "inspect", var root, var scene, var frames] =>
                    await InspectSceneVisibilityAsync(registry, context, root, scene, frames),
                ["render", "openxr", "probe"] => await ProbeOpenXrRuntimeAsync(registry, context),
                ["render", "openxr", "bootstrap-session"] => await BootstrapOpenXrSessionAsync(registry, context),
                ["render", "openxr", "submit-clear"] => await SubmitOpenXrClearAsync("120"),
                ["render", "openxr", "submit-clear", var frames] => await SubmitOpenXrClearAsync(frames),
                ["render", "openxr", "submit-scene", var root, var scene] =>
                    await SubmitOpenXrSoftwareSceneAsync(root, scene, "0", "0", "0"),
                ["render", "openxr", "submit-scene", var root, var scene, var frames] =>
                    await SubmitOpenXrSoftwareSceneAsync(root, scene, frames, "0", "0"),
                ["render", "openxr", "submit-scene", var root, var scene, var frames, var width, var height] =>
                    await SubmitOpenXrSoftwareSceneAsync(root, scene, frames, width, height),
                ["render", "openxr", "frame-plan", var root, var scene] =>
                    await InspectOpenXrHeadsetFramePlanAsync(registry, context, root, scene, "0", "1920", "1080"),
                ["render", "openxr", "frame-plan", var root, var scene, var frames] =>
                    await InspectOpenXrHeadsetFramePlanAsync(registry, context, root, scene, frames, "1920", "1080"),
                ["render", "openxr", "frame-plan", var root, var scene, var frames, var width, var height] =>
                    await InspectOpenXrHeadsetFramePlanAsync(registry, context, root, scene, frames, width, height),
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
                    await CaptureRuntimeViewportAsync(registry, context, root, scene, frames, outputDirectory, "320", "180", "software"),
                ["render", "viewport", "capture", var root, var scene, var frames, var outputDirectory, var width, var height] =>
                    await CaptureRuntimeViewportAsync(registry, context, root, scene, frames, outputDirectory, width, height, "software"),
                ["render", "viewport", "capture", var root, var scene, var frames, var outputDirectory, var width, var height, var backend] =>
                    await CaptureRuntimeViewportAsync(registry, context, root, scene, frames, outputDirectory, width, height, backend, null),
                ["render", "viewport", "capture", var root, var scene, var frames, var outputDirectory, var width, var height, var backend, var inputsJson] =>
                    await CaptureRuntimeViewportAsync(registry, context, root, scene, frames, outputDirectory, width, height, backend, inputsJson),
                ["render", "viewport", "capture", var root, var scene, var frames, var outputDirectory, var width, var height, var backend, var inputsJson, var qualityPreset, var overridesJson, var includeGpuTimings] =>
                    await CaptureRuntimeViewportAsync(
                        registry,
                        context,
                        root,
                        scene,
                        frames,
                        outputDirectory,
                        width,
                        height,
                        backend,
                        inputsJson,
                        qualityPreset,
                        overridesJson,
                        includeGpuTimings),
                ["render", "quality", "compare", var root, var scene, var frames, var outputDirectory, var width, var height, var backend, var presets, var overridesJson, var includeGpuTimings] =>
                    await CompareQualityPresetsAsync(
                        registry,
                        context,
                        root,
                        scene,
                        frames,
                        outputDirectory,
                        width,
                        height,
                        backend,
                        presets,
                        overridesJson,
                        includeGpuTimings),
                ["render", "glb", "export", var root, var scene, var outputPath] =>
                    await ExportSceneGlbAsync(registry, context, root, scene, outputPath, "0"),
                ["render", "glb", "export", var root, var scene, var outputPath, var frames] =>
                    await ExportSceneGlbAsync(registry, context, root, scene, outputPath, frames),
                ["mcp", "stdio"] => await RunMcpStdioAsync(registry, context),
                ["command", "execute", var name, var argumentsJson] =>
                    await ExecuteRegisteredCommandAsync(registry, context, name, argumentsJson),
                ["studio", "open", var root, var scene] => await OpenStudioModelAsync(root, scene),
                ["input", "inspect", var root, var scene] => await InspectInputBindingsAsync(registry, context, root, scene),
                ["input", "rebind", var root, var scene, var entityId, var actionName, var bindingJson] =>
                    await RebindInputActionAsync(registry, context, root, scene, entityId, actionName, bindingJson, remove: false),
                ["input", "remove", var root, var scene, var entityId, var actionName] =>
                    await RebindInputActionAsync(registry, context, root, scene, entityId, actionName, null, remove: true),
                ["asset", "import", var root, var source, var kind, var displayName] =>
                    await ImportAssetAsync(registry, context, root, source, kind, displayName),
                ["asset", "import-report", var root, var source, var kind, var displayName] =>
                    await ImportAssetReportAsync(registry, context, root, source, kind, displayName),
                ["asset", "tripo", "generate", var root, var prompt, var displayName] =>
                    await GenerateTripoModelAsync(registry, context, root, prompt, displayName, null),
                ["asset", "tripo", "generate", var root, var prompt, var displayName, var modelVersion] =>
                    await GenerateTripoModelAsync(registry, context, root, prompt, displayName, modelVersion),
                ["asset", "list", var root] => await ListAssetsAsync(registry, context, root, null),
                ["asset", "list", var root, var kind] => await ListAssetsAsync(registry, context, root, kind),
                ["module", "schemas"] => await ListSchemasAsync(registry, context, null),
                ["module", "schemas", var moduleId] => await ListSchemasAsync(registry, context, moduleId),
                ["module", "schemas", "project", var root] => await ListProjectSchemasAsync(registry, context, root),
                ["module", "trust", var root] => await InspectModuleTrustAsync(registry, context, root),
                ["diagnostics", "failures"] => await InspectFailureReportsAsync(registry, context, null),
                ["diagnostics", "failures", var root] => await InspectFailureReportsAsync(registry, context, root),
                ["compatibility", "inspect", var root] => await InspectProjectCompatibilityAsync(registry, context, root),
                ["compatibility", "migrate", var root] => await MigrateProjectCompatibilityAsync(registry, context, root, apply: false),
                ["compatibility", "migrate", var root, "--apply"] => await MigrateProjectCompatibilityAsync(registry, context, root, apply: true),
                ["recovery", "inspect", "project", var root] =>
                    await InspectDocumentRecoveryAsync(registry, context, root, "project", null),
                ["recovery", "inspect", "scene", var root, var scene] =>
                    await InspectDocumentRecoveryAsync(registry, context, root, "scene", scene),
                ["recovery", "restore", "project", var root, var expectedRevision] =>
                    await RestoreDocumentRecoveryAsync(registry, context, root, "project", null, expectedRevision),
                ["recovery", "restore", "scene", var root, var scene, var expectedRevision] =>
                    await RestoreDocumentRecoveryAsync(registry, context, root, "scene", scene, expectedRevision),
                ["module", "sources", var root] => await ListModuleSourcesAsync(registry, context, root),
                ["module", "read-source", var root, var moduleName, var fileName] =>
                    await ReadModuleSourceAsync(registry, context, root, moduleName, fileName),
                ["module", "install-sdk", var root] => await InstallModuleSdkAsync(registry, context, root),
                ["module", "scaffold", var root, var moduleId, var displayName, var moduleName, var componentName] =>
                    await ScaffoldModuleAsync(registry, context, root, moduleId, displayName, moduleName, componentName),
                ["module", "scaffold-playable", var root, var moduleId, var displayName, var moduleName] =>
                    await ScaffoldPlayableModuleAsync(registry, context, root, moduleId, displayName, moduleName),
                ["module", "scaffold-runtime-system", var root, var moduleId, var displayName, var moduleName, var componentName, var systemName] =>
                    await ScaffoldRuntimeSystemModuleAsync(registry, context, root, moduleId, displayName, moduleName, componentName, systemName),
                ["module", "write-source", var root, var moduleName, var fileName, var sourceOrPath] =>
                    await WriteModuleSourceAsync(registry, context, root, moduleName, fileName, sourceOrPath),
                ["build", "modules", var root] => await BuildModulesAsync(registry, context, root),
                ["build", "player", var root, var scene] => await BuildPlayerAsync(registry, context, root, scene, graphics: false),
                ["build", "player", var root, var scene, "--graphics"] => await BuildPlayerAsync(registry, context, root, scene, graphics: true),
                ["game", "gauntlet", var root, var name] => await RunAgentAuthoringGauntletAsync(registry, context, root, name, "Main", null),
                ["game", "gauntlet", var root, var name, var outputDirectory] =>
                    await RunAgentAuthoringGauntletAsync(registry, context, root, name, "Main", outputDirectory),
                ["game", "gauntlet", var root, var name, var scene, var outputDirectory] =>
                    await RunAgentAuthoringGauntletAsync(registry, context, root, name, scene, outputDirectory),
                ["game", "verify-playable", var root] => await VerifyPlayableGameAsync(registry, context, root, "Main", "2", null, null, null),
                ["game", "verify-playable", var root, var scene] => await VerifyPlayableGameAsync(registry, context, root, scene, "2", null, null, null),
                ["game", "verify-playable", var root, var scene, var frames] => await VerifyPlayableGameAsync(registry, context, root, scene, frames, null, null, null),
                ["game", "verify-playable", var root, var scene, var frames, var assertionsJson] =>
                    await VerifyPlayableGameAsync(registry, context, root, scene, frames, null, assertionsJson, null),
                ["game", "verify-playable", var root, var scene, var frames, var inputsJson, var assertionsJson] =>
                    await VerifyPlayableGameAsync(registry, context, root, scene, frames, inputsJson, assertionsJson, null),
                ["game", "verify-playable", var root, var scene, var frames, var inputsJson, var assertionsJson, var drawAssertionsJson] =>
                    await VerifyPlayableGameAsync(registry, context, root, scene, frames, inputsJson, assertionsJson, drawAssertionsJson),
                ["game", "package-playable", var root] => await PackagePlayableGameAsync(registry, context, root, "Main", null),
                ["game", "package-playable", var root, var scene] => await PackagePlayableGameAsync(registry, context, root, scene, null),
                ["game", "package-playable", var root, var scene, var outputDirectory] =>
                    await PackagePlayableGameAsync(registry, context, root, scene, outputDirectory),
                ["game", "package-playable", var root, var scene, var outputDirectory, "--graphics"] =>
                    await PackagePlayableGameAsync(registry, context, root, scene, outputDirectory, graphics: true),
                ["game", "package-playable", var root, var scene, var outputDirectory, "--target", "windows"] =>
                    await PackagePlayableGameAsync(registry, context, root, scene, outputDirectory, target: RekallAgePlayablePackageTargets.Windows),
                ["game", "package-playable", var root, var scene, var outputDirectory, "--target", "headless"] =>
                    await PackagePlayableGameAsync(registry, context, root, scene, outputDirectory, target: RekallAgePlayablePackageTargets.Headless),
                ["game", "inspect-package", var packagePath] => await InspectPlayablePackageAsync(registry, context, packagePath),
                ["game", "relocate-package", var packagePath, var destination] =>
                    await RelocatePlayablePackageAsync(registry, context, packagePath, destination),
                ["game", "audit-package", var packagePath] => await AuditPlayablePackageAsync(registry, context, packagePath, null),
                ["game", "audit-package", var packagePath, var outputDirectory] =>
                    await AuditPlayablePackageAsync(registry, context, packagePath, outputDirectory),
                ["game", "run-package", var packagePath] => await RunPlayablePackageAsync(registry, context, packagePath, "2"),
                ["game", "run-package", var packagePath, var frames] => await RunPlayablePackageAsync(registry, context, packagePath, frames),
                ["game", "capture-package-frame", var packagePath, var outputDirectory] =>
                    await CapturePlayablePackageFrameAsync(registry, context, packagePath, outputDirectory, "1"),
                ["game", "capture-package-frame", var packagePath, var outputDirectory, var frameIndex] =>
                    await CapturePlayablePackageFrameAsync(registry, context, packagePath, outputDirectory, frameIndex, null),
                ["game", "capture-package-frame", var packagePath, var outputDirectory, var frameIndex, var inputsJson] =>
                    await CapturePlayablePackageFrameAsync(registry, context, packagePath, outputDirectory, frameIndex, inputsJson),
                ["game", "publish-web", var root] => await PublishWebGameAsync(registry, context, root, "Main", null),
                ["game", "publish-web", var root, var scene] => await PublishWebGameAsync(registry, context, root, scene, null),
                ["game", "publish-web", var root, var scene, var outputDirectory] =>
                    await PublishWebGameAsync(registry, context, root, scene, outputDirectory),
                ["game", "audit-web", var root] => await AuditWebGameAsync(registry, context, root, "Main", null),
                ["game", "audit-web", var root, var scene] => await AuditWebGameAsync(registry, context, root, scene, null),
                ["game", "audit-web", var root, var scene, var outputDirectory] =>
                    await AuditWebGameAsync(registry, context, root, scene, outputDirectory),
                ["project", "create", var root, var name, var capabilities] => await CreateProjectAsync(registry, context, root, name, capabilities),
                ["capability", "add", var root, var capability] => await AddCapabilityAsync(registry, context, root, capability),
                ["capability", "add", var root, var capability, var expectedRevision] =>
                    await AddCapabilityAsync(registry, context, root, capability, expectedRevision),
                ["scene", "create", var root, var name, var capabilities] => await CreateSceneAsync(registry, context, root, name, capabilities),
                ["entity", "create", var root, var scene, var name, var tags] => await CreateEntityAsync(registry, context, root, scene, name, tags),
                ["entity", "create", var root, var scene, var name, var tags, var expectedRevision] =>
                    await CreateEntityAsync(registry, context, root, scene, name, tags, expectedRevision),
                ["entity", "inspect", var root, var scene, var entityId] => await InspectEntityAsync(registry, context, root, scene, entityId),
                ["component", "set", var root, var scene, var entityId, var componentType, var propertyName, var value] =>
                    await SetComponentPropertyAsync(registry, context, root, scene, entityId, componentType, propertyName, value),
                ["component", "add", var root, var scene, var entityId, var componentType, var propertiesJson] =>
                    await AddComponentAsync(registry, context, root, scene, entityId, componentType, propertiesJson),
                ["component", "remove-property", var root, var scene, var entityId, var componentType, var propertyName] =>
                    await RemoveComponentPropertyAsync(registry, context, root, scene, entityId, componentType, propertyName),
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
                ["geometry", "primitive", "create", var root, var scene, var name, var primitive] =>
                    await CreateGeometryPrimitiveAsync(registry, context, root, scene, name, primitive, "0", "0", "0", "#8ab4f8"),
                ["geometry", "primitive", "create", var root, var scene, var name, var primitive, var x, var y, var z] =>
                    await CreateGeometryPrimitiveAsync(registry, context, root, scene, name, primitive, x, y, z, "#8ab4f8"),
                ["geometry", "primitive", "create", var root, var scene, var name, var primitive, var x, var y, var z, var color] =>
                    await CreateGeometryPrimitiveAsync(registry, context, root, scene, name, primitive, x, y, z, color),
                ["geometry", "mesh", "create", var root, var scene, var name, var verticesJson, var indicesJson] =>
                    await CreateGeometryMeshAsync(registry, context, root, scene, name, verticesJson, indicesJson, "0", "0", "0", "#8ab4f8"),
                ["geometry", "mesh", "create", var root, var scene, var name, var verticesJson, var indicesJson, var x, var y, var z] =>
                    await CreateGeometryMeshAsync(registry, context, root, scene, name, verticesJson, indicesJson, x, y, z, "#8ab4f8"),
                ["geometry", "mesh", "create", var root, var scene, var name, var verticesJson, var indicesJson, var x, var y, var z, var color] =>
                    await CreateGeometryMeshAsync(registry, context, root, scene, name, verticesJson, indicesJson, x, y, z, color, null),
                ["geometry", "mesh", "create", var root, var scene, var name, var verticesJson, var indicesJson, var x, var y, var z, var color, var textureAssetId] =>
                    await CreateGeometryMeshAsync(registry, context, root, scene, name, verticesJson, indicesJson, x, y, z, color, textureAssetId),
                ["geometry", "recipe", "create", var root, var scene, var name, var partsJson] =>
                    await CreateGeometryRecipeAsync(registry, context, root, scene, name, partsJson, "0", "0", "0", "#8ab4f8"),
                ["geometry", "recipe", "create", var root, var scene, var name, var partsJson, var x, var y, var z] =>
                    await CreateGeometryRecipeAsync(registry, context, root, scene, name, partsJson, x, y, z, "#8ab4f8"),
                ["geometry", "recipe", "create", var root, var scene, var name, var partsJson, var x, var y, var z, var color] =>
                    await CreateGeometryRecipeAsync(registry, context, root, scene, name, partsJson, x, y, z, color),
                ["geometry", "extrusion", "create", var root, var scene, var name, var profileJson, var depth] =>
                    await CreateGeometryExtrusionAsync(registry, context, root, scene, name, profileJson, depth, "0", "0", "0", "#8ab4f8"),
                ["geometry", "extrusion", "create", var root, var scene, var name, var profileJson, var depth, var x, var y, var z] =>
                    await CreateGeometryExtrusionAsync(registry, context, root, scene, name, profileJson, depth, x, y, z, "#8ab4f8"),
                ["geometry", "extrusion", "create", var root, var scene, var name, var profileJson, var depth, var x, var y, var z, var color] =>
                    await CreateGeometryExtrusionAsync(registry, context, root, scene, name, profileJson, depth, x, y, z, color),
                ["planet", "import-ksa", var root, var scene, var ksaRoot, var bodyId] =>
                    await ImportKsaPlanetAsync(registry, context, root, scene, ksaRoot, bodyId, null),
                ["planet", "import-ksa", var root, var scene, var ksaRoot, var bodyId, var entityName] =>
                    await ImportKsaPlanetAsync(registry, context, root, scene, ksaRoot, bodyId, entityName),
                ["solar", "import-ksa-system", var root, var scene, var ksaRoot] =>
                    await ImportKsaSolarSystemAsync(registry, context, root, scene, ksaRoot, "SolSystem.xml", "0.000001", "0.00002"),
                ["solar", "import-ksa-system", var root, var scene, var ksaRoot, var systemFileName] =>
                    await ImportKsaSolarSystemAsync(registry, context, root, scene, ksaRoot, systemFileName, "0.000001", "0.00002"),
                ["solar", "import-ksa-system", var root, var scene, var ksaRoot, var systemFileName, var distanceScale, var radiusScale] =>
                    await ImportKsaSolarSystemAsync(registry, context, root, scene, ksaRoot, systemFileName, distanceScale, radiusScale),
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
                ["run", "scene", var root, var scene, var seconds] => await RunSceneAsync(registry, context, root, scene, seconds, null),
                ["run", "scene", var root, var scene, var seconds, var inputsJson] => await RunSceneAsync(registry, context, root, scene, seconds, inputsJson),
                ["runtime", "inspect", var root, var scene, var frames] => await InspectRuntimeAsync(registry, context, root, scene, frames, null, null),
                ["runtime", "inspect", var root, var scene, var frames, var inputsJson] => await InspectRuntimeAsync(registry, context, root, scene, frames, inputsJson, null),
                ["runtime", "inspect", var root, var scene, var frames, var inputsJson, var assertionsJson] => await InspectRuntimeAsync(registry, context, root, scene, frames, inputsJson, assertionsJson),
                ["runtime", "soak", var root, var scene] => await InspectRuntimeSoakAsync(
                    registry, context, root, scene, "3600", "600", "0", "-1", "0", "128", "1024"),
                ["runtime", "soak", var root, var scene, var frames, var checkpointInterval, var minimumFramesPerSecond,
                    var maximumRetainedMemoryGrowthBytes, var maximumEntityGrowth, var maximumObservations, var maximumEvents] =>
                    await InspectRuntimeSoakAsync(
                        registry,
                        context,
                        root,
                        scene,
                        frames,
                        checkpointInterval,
                        minimumFramesPerSecond,
                        maximumRetainedMemoryGrowthBytes,
                        maximumEntityGrowth,
                        maximumObservations,
                        maximumEvents),
                ["multiplayer", "host", var root, var scene] => await MultiplayerHostAsync(registry, context, root, scene, "30"),
                ["multiplayer", "host", var root, var scene, var durationSeconds] => await MultiplayerHostAsync(registry, context, root, scene, durationSeconds),
                ["multiplayer", "status", var root, var scene] => await MultiplayerStatusAsync(registry, context, root, scene),
                ["multiplayer", "connect", var root, var scene, var clientId] => await MultiplayerConnectAsync(registry, context, root, scene, clientId, null),
                ["multiplayer", "connect", var root, var scene, var clientId, var displayName] => await MultiplayerConnectAsync(registry, context, root, scene, clientId, displayName),
                ["multiplayer", "disconnect", var root, var scene, var clientId] => await MultiplayerDisconnectAsync(registry, context, root, scene, clientId),
                ["multiplayer", "input", var root, var scene, var clientId, var sequence, var networkId, var inputJson] =>
                    await MultiplayerSubmitInputAsync(registry, context, root, scene, clientId, sequence, networkId, inputJson),
                ["multiplayer", "tick", var root, var scene] => await MultiplayerTickAsync(registry, context, root, scene, "1"),
                ["multiplayer", "tick", var root, var scene, var ticks] => await MultiplayerTickAsync(registry, context, root, scene, ticks),
                ["multiplayer", "snapshot", var root, var scene] => await MultiplayerSnapshotAsync(registry, context, root, scene),
                ["multiplayer", "delta", var root, var scene, var fromServerTick] => await MultiplayerDeltaAsync(registry, context, root, scene, fromServerTick),
                ["context", "engine"] => await PrintEngineStatusAsync(registry, context),
                ["context", "doctor"] => await PrintEngineDoctorAsync(registry, context, null),
                ["context", "doctor", var root] => await PrintEngineDoctorAsync(registry, context, root),
                ["context", "summary", var root] => await PrintSummaryAsync(registry, context, root),
                ["context", "scene", var root, var scene] => await PrintSceneSummaryAsync(registry, context, root, scene),
                ["validation", "project", var root] => await ValidateProjectAsync(registry, context, root),
                ["validation", "scene", var root, var scene] => await ValidateSceneAsync(registry, context, root, scene),
                ["scene", "validate", var root, var scene] => await ValidateSceneAsync(registry, context, root, scene),
                ["transaction", "history", var root] => await PrintTransactionHistoryAsync(registry, context, root, "20"),
                ["transaction", "history", var root, var limit] => await PrintTransactionHistoryAsync(registry, context, root, limit),
                ["transaction", "restore-preimage", var root, var transactionId, var relativePath] =>
                    await RestoreTransactionPreimageAsync(registry, context, root, transactionId, relativePath),
                ["capture", "screenshot", var root, var scene] => await CaptureAsync(registry, context, root, scene),
                _ => PrintUnknown(args)
            };
            if (exitCode == 0)
            {
                await PersistTransactionAsync(context);
            }
            else
            {
                Log.Warning("Rekall AGE command returned non-zero exit code. ExitCode={ExitCode} Args={Args}", exitCode, DescribeInvocation(args));
            }

            Log.Information("Rekall AGE command finished. ExitCode={ExitCode} LogDirectory={LogDirectory}", exitCode, logDirectory);
            return exitCode;
        }
        catch (RekallAgeCodedBoundaryException ex)
        {
            Log.Error(ex, "CLI command rejected at a coded boundary. Code={Code} Target={Target} Args={Args} LogDirectory={LogDirectory}", ex.Code, ex.Target, DescribeInvocation(args), logDirectory);
            Console.Error.WriteLine($"{ex.Code}: {ex.Message}");
            return 1;
        }
        catch (RekallAgeLanguageModelProviderException ex)
        {
            Log.Error(ex, "Language-model provider command failed. Provider={Provider} Code={Code} Args={Args} LogDirectory={LogDirectory}", ex.ProviderId, ex.Code, DescribeInvocation(args), logDirectory);
            WriteLanguageModelProviderDiagnostic(ex);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Log.Information("Language-model command cancelled. Args={Args} LogDirectory={LogDirectory}", DescribeInvocation(args), logDirectory);
            Console.Error.WriteLine("REKALL_LANGUAGE_MODEL_CANCELLED: The language-model operation was cancelled.");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            Log.Error(ex, "CLI command failed. Args={Args} LogDirectory={LogDirectory}", DescribeInvocation(args), logDirectory);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled CLI command exception. Args={Args} LogDirectory={LogDirectory}", DescribeInvocation(args), logDirectory);
            Console.Error.WriteLine($"Unexpected error. See log: {logDirectory}");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static string ConfigureLogging(string[] args)
    {
        var applicationName = IsMcpStdio(args) ? "Mcp" : "Cli";
        var envName = applicationName.Equals("Mcp", StringComparison.Ordinal)
            ? "REKALL_AGE_MCP_LOG_DIR"
            : "REKALL_AGE_CLI_LOG_DIR";
        var logDirectory = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            logDirectory = Environment.GetEnvironmentVariable("REKALL_AGE_LOG_DIR");
        }

        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Rekall AGE",
                applicationName,
                "Logs");
        }

        Directory.CreateDirectory(logDirectory);
        var filePrefix = applicationName.ToLowerInvariant();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, $"{filePrefix}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:O} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        return logDirectory;
    }

    private static bool IsMcpStdio(string[] args)
    {
        return args is ["mcp", "stdio", ..];
    }

    private static RekallAgeCommandRegistry BuildRegistry() => RekallAgeDefaultCommandRegistry.Create();

    private static async Task<int> ExecuteRegisteredCommandAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string name,
        string argumentsJson)
    {
        var result = await registry.ExecuteJsonAsync(name, argumentsJson, context);
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, options));
        return result.Ok ? 0 : 1;
    }

    private static string DescribeInvocation(string[] args) =>
        args is ["command", "execute", var name, _]
            ? $"command execute {name} <json>"
            : string.Join(' ', args);

    private static async ValueTask PersistTransactionAsync(RekallAgeCommandContext context)
    {
        if (context.Transaction.ChangedResources.Count == 0)
        {
            return;
        }

        var projectRoot = RekallAgeTransactionProjectRootResolver.Resolve(context.Transaction.ChangedResources);
        if (projectRoot is null)
        {
            return;
        }

        await new RekallAgeTransactionLogStore().AppendAsync(
            projectRoot,
            context.Transaction,
            context.Actor,
            context.CancellationToken);
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

    private static async Task<int> InspectStereoRenderPlanAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string width,
        string height)
    {
        var frameCount = int.Parse(frames, CultureInfo.InvariantCulture);
        var viewportWidth = int.Parse(width, CultureInfo.InvariantCulture);
        var viewportHeight = int.Parse(height, CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<InspectStereoRenderPlanRequest, InspectStereoRenderPlanResult>(
            "rekall.render.stereo.inspect_plan",
            new InspectStereoRenderPlanRequest(root, scene, frameCount, viewportWidth, viewportHeight),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Active camera: {result.Value.ActiveCamera ?? "<none>"}");
        Console.WriteLine($"Stereo enabled: {result.Value.StereoEnabled}");
        Console.WriteLine($"Render mode: {result.Value.RenderMode}");
        Console.WriteLine($"Eyes: {result.Value.EyeCount}; eye uniforms: {result.Value.EyeUniformCount}");
        Console.WriteLine($"Shared geometry buffers: {result.Value.SharedGeometryBuffers}");
        Console.WriteLine($"Vertices: {result.Value.VertexCount}; indices: {result.Value.IndexCount}; draws: {result.Value.DrawCount}");
        Console.WriteLine($"Preview submissions: {result.Value.CurrentPreviewDrawSubmissions}; target multiview submissions: {result.Value.TargetMultiviewDrawSubmissions}");
        foreach (var eye in result.Value.Eyes)
        {
            Console.WriteLine($"  {eye.Name}: offsetX={eye.OffsetX:F4}, viewport={eye.ViewportX:F0},{eye.ViewportY:F0} {eye.ViewportWidth:F0}x{eye.ViewportHeight:F0}");
        }

        foreach (var recommendation in result.Value.Recommendations)
        {
            Console.WriteLine($"Recommendation: {recommendation}");
        }

        foreach (var warning in result.Value.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectScenePerformanceBudgetAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string width,
        string height,
        string profile,
        string? qualityPreset = null,
        string? qualityOverridesJson = null,
        string includeGpuTimings = "false")
    {
        var frameCount = int.Parse(frames, CultureInfo.InvariantCulture);
        var viewportWidth = int.Parse(width, CultureInfo.InvariantCulture);
        var viewportHeight = int.Parse(height, CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<InspectScenePerformanceBudgetRequest, InspectScenePerformanceBudgetResult>(
            "rekall.render.performance.inspect_scene_budget",
            new InspectScenePerformanceBudgetRequest(root, scene, frameCount, viewportWidth, viewportHeight, profile)
            {
                QualityPreset = qualityPreset,
                QualityOverrides = ParseQualityOverrides(qualityOverridesJson),
                IncludeGpuTimings = bool.Parse(includeGpuTimings)
            },
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Profile: {result.Value.Profile}; target FPS: {result.Value.TargetFramesPerSecond}");
        Console.WriteLine($"Entities: {result.Value.EntityCount}; renderables: {result.Value.RenderableCount}; meshes: {result.Value.MeshCount}");
        Console.WriteLine($"Draw calls: {result.Value.DrawCalls}; estimated invocations: {result.Value.EstimatedDrawInvocations}");
        Console.WriteLine($"Triangles: {result.Value.Triangles}; vertices: {result.Value.Vertices}");
        Console.WriteLine($"Textures: {result.Value.TextureCount}; runtime textures: {result.Value.RuntimeTextureCount}; asset issues: {result.Value.AssetIssueCount}");
        Console.WriteLine($"Stereo: {result.Value.StereoEnabled}; multiview: {result.Value.UsesSinglePassMultiview}; eyes: {result.Value.EyeCount}");
        Console.WriteLine($"Render target pixels: {result.Value.EstimatedRenderTargetPixels}; geometry bytes: {result.Value.EstimatedGeometryBytes}");
        PrintQualityReport(result.Value.QualityPlan, result.Value.GpuTimings, result.Value.ResourceBytes);
        Console.WriteLine($"Resource bytes: {result.Value.ResourceBytes}; workload draws: {result.Value.RenderWorkloadDrawCount}; workload dispatches: {result.Value.RenderWorkloadDispatchCount}");
        Console.WriteLine($"Budget: draws {result.Value.Limits.MaxDrawInvocations}, triangles {result.Value.Limits.MaxTriangles}, vertices {result.Value.Limits.MaxVertices}, textures {result.Value.Limits.MaxTextures}, pixels {result.Value.Limits.MaxRenderTargetPixels}");
        foreach (var camera in result.Value.CameraMasks)
        {
            Console.WriteLine($"Camera: {camera.EntityName}; active: {camera.Active}; order: {camera.RenderOrder}; viewport: {camera.ViewportX},{camera.ViewportY} {camera.ViewportWidth}x{camera.ViewportHeight}; culling mask: {camera.CullingMask}");
        }

        foreach (var layer in result.Value.LayerBreakdown)
        {
            Console.WriteLine($"Layer: {layer.Layer}; renderables: {layer.RenderableCount}; meshes: {layer.MeshCount}; draws: {layer.DrawCalls}; triangles: {layer.Triangles}; vertices: {layer.Vertices}");
        }

        foreach (var renderable in result.Value.CulledRenderables)
        {
            Console.WriteLine($"Culled: {renderable.EntityName}; layer: {renderable.Layer}; reason: {renderable.Reason}; camera: {renderable.CameraEntityName ?? "none"}; mask: {renderable.CullingMask}");
        }

        foreach (var blocker in result.Value.Blockers)
        {
            Console.WriteLine($"Blocker: {blocker}");
        }

        foreach (var warning in result.Value.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }

        foreach (var recommendation in result.Value.Recommendations)
        {
            Console.WriteLine($"Recommendation: {recommendation}");
        }

        foreach (var command in result.Value.SuggestedCommands)
        {
            Console.WriteLine($"Next: {command}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ProbeOpenXrRuntimeAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context)
    {
        var result = await registry.ExecuteAsync<ProbeOpenXrRuntimeRequest, ProbeOpenXrRuntimeResult>(
            "rekall.render.openxr.probe",
            new ProbeOpenXrRuntimeRequest(),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Loader available: {result.Value.LoaderAvailable}");
        Console.WriteLine($"Runtime available: {result.Value.RuntimeAvailable}");
        Console.WriteLine($"Loader: {result.Value.LoaderName ?? "<none>"}");
        Console.WriteLine($"Extensions: {result.Value.ExtensionCount}");
        Console.WriteLine($"XR_KHR_vulkan_enable2: {result.Value.VulkanEnable2Available}");
        Console.WriteLine($"Primary stereo ready: {result.Value.PrimaryStereoReady}");
        Console.WriteLine($"Headset launch ready: {result.Value.HeadsetLaunchReady}");
        foreach (var extension in result.Value.InstanceExtensions.Take(12))
        {
            Console.WriteLine($"  {extension.Name} v{extension.Version}");
        }

        foreach (var step in result.Value.RequiredNextSteps)
        {
            Console.WriteLine($"Next: {step}");
        }

        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectSceneMeshGeometryAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames)
    {
        var result = await registry.ExecuteAsync<InspectSceneMeshGeometryRequest, InspectSceneMeshGeometryResult>(
            "rekall.render.inspect_scene_mesh_geometry",
            new InspectSceneMeshGeometryRequest(root, scene, int.Parse(frames, CultureInfo.InvariantCulture)),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Meshes: {result.Value.MeshCount}; vertices: {result.Value.VertexCount}; indices: {result.Value.IndexCount}; triangles: {result.Value.TriangleCount}");
        foreach (var mesh in result.Value.Meshes)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"Mesh {mesh.EntityName}[{mesh.MeshIndex}]: primitive={mesh.Primitive} vertices={mesh.VertexCount} indices={mesh.IndexCount} triangles={mesh.TriangleCount} min=({mesh.Minimum.X:G9},{mesh.Minimum.Y:G9},{mesh.Minimum.Z:G9}) max=({mesh.Maximum.X:G9},{mesh.Maximum.Y:G9},{mesh.Maximum.Z:G9}) morphTargets={mesh.MorphTargetCount} morphSource={mesh.MorphWeightSource}"));
        }
        if (result.Value.Truncated) Console.WriteLine("Mesh inspection truncated at 256 summaries.");
        foreach (var issue in result.Value.AssetIssues) Console.WriteLine($"{issue.Code}: {issue.Message}");
        if (result.Value.AssetIssuesTruncated) Console.WriteLine("Asset issue inspection truncated at 256 entries.");
        foreach (var error in result.Errors) Console.WriteLine($"{error.Code}: {error.Message}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectShaderPipelineAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string vertex,
        string fragment)
    {
        var result = await registry.ExecuteAsync<InspectShaderPipelineRequest, InspectShaderPipelineResult>(
            "rekall.shader.inspect_pipeline",
            new InspectShaderPipelineRequest(root, vertex, fragment),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Valid: {result.Value.Valid}; ABI: {result.Value.AbiVersion}; SHA-256: {result.Value.ContentHash}");
        Console.WriteLine($"SPIR-V bytes: vertex={result.Value.VertexSpirvBytes}; fragment={result.Value.FragmentSpirvBytes}");
        foreach (var element in result.Value.VertexElements)
        {
            Console.WriteLine($"Vertex location={element.Location} name={element.Name} format={element.Format}");
        }
        foreach (var resource in result.Value.Resources)
        {
            Console.WriteLine($"Resource set={resource.Set} binding={resource.Binding} name={resource.Name} kind={resource.Kind} stages={resource.Stages}");
        }
        foreach (var diagnostic in result.Value.Diagnostics)
        {
            Console.WriteLine($"Diagnostic: {diagnostic}");
        }
        return result.Value.Valid ? 0 : 1;
    }

    private static async Task<int> WriteShaderSourceAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string name,
        string stage,
        string source)
    {
        var result = await registry.ExecuteAsync<WriteShaderSourceRequest, WriteShaderSourceResult>(
            "rekall.shader.write",
            new WriteShaderSourceRequest(root, name, stage, source),
            context);
        Console.WriteLine(result.Summary);
        foreach (var error in result.Value.Errors) Console.WriteLine($"Diagnostic: {error}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> AssignShaderPipelineAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string vertex,
        string fragment)
    {
        var result = await registry.ExecuteAsync<AssignShaderPipelineRequest, AssignShaderPipelineResult>(
            "rekall.shader.assign_pipeline",
            new AssignShaderPipelineRequest(root, scene, entityId, vertex, fragment),
            context);
        Console.WriteLine(result.Summary);
        foreach (var error in result.Value.Errors) Console.WriteLine($"Diagnostic: {error}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectVirtualGeometrySceneAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string width,
        string height)
    {
        var frameCount = int.Parse(frames, CultureInfo.InvariantCulture);
        var viewportWidth = int.Parse(width, CultureInfo.InvariantCulture);
        var viewportHeight = int.Parse(height, CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<InspectVirtualGeometrySceneRequest, InspectVirtualGeometrySceneResult>(
            "rekall.render.virtual_geometry.inspect_scene",
            new InspectVirtualGeometrySceneRequest(root, scene, frameCount, viewportWidth, viewportHeight),
            context);

        Console.WriteLine(result.Summary);
        Console.WriteLine($"Renderables: {result.Value.RenderableCount}; virtual geometry: {result.Value.VirtualGeometryRenderableCount}");
        Console.WriteLine($"Triangles: source {result.Value.SourceTriangles}; selected {result.Value.SelectedTriangles}; reduced {result.Value.ReducedTriangles}");
        foreach (var renderable in result.Value.Renderables)
        {
            Console.WriteLine(
                $"Virtual geometry: {renderable.EntityName}; enabled: {renderable.Enabled}; meshes: {renderable.MeshCount}; source: {renderable.SourceTriangles}; selected: {renderable.SelectedTriangles}; reduced: {renderable.ReducedTriangles}; lod: {renderable.SelectedLodLevel}; max selected: {renderable.MaxSelectedTriangles}; cluster triangles: {renderable.ClusterTriangleCount}; pixel error: {renderable.TargetPixelError:F3}");
        }

        foreach (var recommendation in result.Value.Recommendations)
        {
            Console.WriteLine($"Recommendation: {recommendation}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ApplyVirtualGeometryToSceneAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string minSourceTriangles,
        string width,
        string height,
        bool dryRun,
        string? entityName = null)
    {
        var minimum = int.Parse(minSourceTriangles, CultureInfo.InvariantCulture);
        var viewportWidth = int.Parse(width, CultureInfo.InvariantCulture);
        var viewportHeight = int.Parse(height, CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<ApplyVirtualGeometryToSceneRequest, ApplyVirtualGeometryToSceneResult>(
            "rekall.render.virtual_geometry.apply_scene",
            new ApplyVirtualGeometryToSceneRequest(root, scene, minimum, viewportWidth, viewportHeight, DryRun: dryRun, EntityName: entityName),
            context);

        Console.WriteLine(result.Summary);
        Console.WriteLine($"Dry run: {result.Value.DryRun}");
        if (!string.IsNullOrWhiteSpace(entityName))
        {
            Console.WriteLine($"Entity: {entityName}");
        }

        Console.WriteLine($"Candidates: {result.Value.CandidateEntityCount}; applied: {result.Value.AppliedEntityCount}; skipped existing: {result.Value.SkippedExistingEntityCount}");
        foreach (var applied in result.Value.AppliedEntities)
        {
            Console.WriteLine($"Applied: {applied.EntityName}; source triangles: {applied.SourceTriangles}");
        }

        foreach (var skipped in result.Value.SkippedExistingEntities)
        {
            Console.WriteLine($"Skipped existing: {skipped.EntityName}; source triangles: {skipped.SourceTriangles}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> BootstrapOpenXrSessionAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context)
    {
        var result = await registry.ExecuteAsync<BootstrapOpenXrSessionRequest, BootstrapOpenXrSessionResult>(
            "rekall.render.openxr.bootstrap_session",
            new BootstrapOpenXrSessionRequest(),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Loader available: {result.Value.LoaderAvailable}");
        Console.WriteLine($"Runtime available: {result.Value.RuntimeAvailable}");
        Console.WriteLine($"Instance created: {result.Value.InstanceCreated}");
        Console.WriteLine($"HMD system available: {result.Value.HmdSystemAvailable}");
        Console.WriteLine($"System id: {result.Value.SystemId?.ToString(CultureInfo.InvariantCulture) ?? "<none>"}");
        Console.WriteLine($"XR_KHR_vulkan_enable2: {result.Value.VulkanEnable2Available}");
        Console.WriteLine($"Vulkan requirements ready: {result.Value.VulkanGraphicsRequirementsReady}");
        if (result.Value.VulkanGraphicsRequirements is not null)
        {
            Console.WriteLine($"Vulkan API range: {result.Value.VulkanGraphicsRequirements.MinimumApiVersion}..{result.Value.VulkanGraphicsRequirements.MaximumApiVersion}");
        }

        Console.WriteLine($"Primary stereo view config ready: {result.Value.PrimaryStereoViewConfigurationReady}");
        foreach (var view in result.Value.PrimaryStereoViews)
        {
            Console.WriteLine(
                $"View {view.Index}: recommended {view.RecommendedImageRectWidth}x{view.RecommendedImageRectHeight}, max {view.MaxImageRectWidth}x{view.MaxImageRectHeight}, samples {view.RecommendedSwapchainSampleCount}/{view.MaxSwapchainSampleCount}");
        }

        Console.WriteLine($"Headset session ready: {result.Value.HeadsetSessionReady}");
        foreach (var extension in result.Value.EnabledExtensions)
        {
            Console.WriteLine($"Enabled extension: {extension}");
        }

        foreach (var extension in result.Value.MissingExtensions)
        {
            Console.WriteLine($"Missing extension: {extension}");
        }

        foreach (var step in result.Value.NextRenderSteps)
        {
            Console.WriteLine($"Next: {step}");
        }

        foreach (var error in result.Value.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectSceneVisibilityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames)
    {
        var frameCount = int.Parse(frames, CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<InspectSceneVisibilityRequest, InspectSceneVisibilityResult>(
            "rekall.render.visibility.inspect_scene",
            new InspectSceneVisibilityRequest(root, scene, frameCount),
            context);

        Console.WriteLine(result.Summary);
        Console.WriteLine($"Renderables: {result.Value.TotalRenderableCount}");
        foreach (var camera in result.Value.Cameras)
        {
            Console.WriteLine($"Camera: {camera.EntityName}; active: {camera.Active}; order: {camera.RenderOrder}; viewport: {camera.ViewportX},{camera.ViewportY} {camera.ViewportWidth}x{camera.ViewportHeight}; culling mask: {camera.CullingMask}; visible: {camera.VisibleRenderableCount}; culled: {camera.CulledRenderableCount}");
            foreach (var renderable in camera.VisibleRenderables)
            {
                Console.WriteLine($"  Visible: {renderable.EntityName}; kind: {renderable.Kind}; layer: {renderable.Layer}");
            }

            foreach (var renderable in camera.CulledRenderables)
            {
                Console.WriteLine($"  Culled: {renderable.EntityName}; kind: {renderable.Kind}; layer: {renderable.Layer}; reason: {renderable.Reason}");
            }
        }

        foreach (var renderable in result.Value.UnseenByActiveCameraRenderables)
        {
            Console.WriteLine($"Unseen by active camera: {renderable.EntityName}; kind: {renderable.Kind}; layer: {renderable.Layer}");
        }

        foreach (var renderable in result.Value.UnseenByAnyCameraRenderables)
        {
            Console.WriteLine($"Unseen by any camera: {renderable.EntityName}; kind: {renderable.Kind}; layer: {renderable.Layer}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> SubmitOpenXrClearAsync(string frames)
    {
        if (!int.TryParse(frames, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frameCount))
        {
            Console.WriteLine($"Invalid frame count '{frames}'.");
            return 2;
        }

        var result = await new RekallAgeSilkOpenXrHeadsetClearSubmitter().SubmitAsync(
            new RekallAgeOpenXrHeadsetClearSubmitRequest(FrameCount: frameCount),
            CancellationToken.None);
        Console.WriteLine(result.Submitted
            ? $"Submitted {result.SubmittedFrames} OpenXR headset clear frame(s)."
            : "OpenXR headset clear submit failed.");
        Console.WriteLine($"Instance: {result.InstanceCreated}");
        Console.WriteLine($"Vulkan instance: {result.VulkanInstanceCreated}");
        Console.WriteLine($"Vulkan device: {result.VulkanDeviceCreated}");
        Console.WriteLine($"Session: {result.SessionCreated}");
        Console.WriteLine($"Reference space: {result.ReferenceSpaceCreated}");
        Console.WriteLine($"Swapchain: {result.SwapchainCreated}");
        Console.WriteLine($"Recommended eye size: {result.RecommendedWidth}x{result.RecommendedHeight}");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return result.Submitted ? 0 : 1;
    }

    private static Task<int> SubmitOpenXrSoftwareSceneAsync(
        string root,
        string scene,
        string frames,
        string width,
        string height)
    {
        if (!int.TryParse(frames, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frameCount)
            || !int.TryParse(width, NumberStyles.Integer, CultureInfo.InvariantCulture, out var renderWidth)
            || !int.TryParse(height, NumberStyles.Integer, CultureInfo.InvariantCulture, out var renderHeight))
        {
            Console.WriteLine("Invalid OpenXR scene submit frame count or render size.");
            return Task.FromResult(2);
        }

        var result = new RekallAgeSilkOpenXrHeadsetClearSubmitter().SubmitSoftwareScene(
            new RekallAgeOpenXrHeadsetSoftwareSceneSubmitRequest(
                root,
                scene,
                FrameCount: frameCount,
                RenderWidth: renderWidth,
                RenderHeight: renderHeight),
            CancellationToken.None);
        Console.WriteLine(result.Submitted
            ? $"Submitted {result.SubmittedFrames} OpenXR headset scene frame(s)."
            : "OpenXR headset scene submit failed.");
        Console.WriteLine($"Instance: {result.InstanceCreated}");
        Console.WriteLine($"Vulkan instance: {result.VulkanInstanceCreated}");
        Console.WriteLine($"Vulkan device: {result.VulkanDeviceCreated}");
        Console.WriteLine($"Session: {result.SessionCreated}");
        Console.WriteLine($"Reference space: {result.ReferenceSpaceCreated}");
        Console.WriteLine($"Swapchain: {result.SwapchainCreated}");
        Console.WriteLine($"Recommended eye size: {result.RecommendedWidth}x{result.RecommendedHeight}");
        Console.WriteLine($"Submitted eye size: {result.RenderWidth}x{result.RenderHeight}");
        Console.WriteLine($"Rendering backend: {result.RenderingBackend}");
        Console.WriteLine($"Native Vulkan frames: {result.NativeVulkanFrames}");
        Console.WriteLine($"Software fallback frames: {result.SoftwareFallbackFrames}");
        foreach (var reason in result.NativeVulkanFallbackReasons)
        {
            Console.WriteLine($"Native Vulkan fallback: {reason}");
        }
        Console.WriteLine($"Active camera: {result.ActiveCamera ?? "<none>"}");
        Console.WriteLine($"Renderables: {result.RenderableCount}");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        return Task.FromResult(result.Submitted ? 0 : 1);
    }

    private static async Task<int> InspectOpenXrHeadsetFramePlanAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string width,
        string height)
    {
        var frameCount = int.Parse(frames, CultureInfo.InvariantCulture);
        var viewportWidth = int.Parse(width, CultureInfo.InvariantCulture);
        var viewportHeight = int.Parse(height, CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<InspectOpenXrHeadsetFramePlanRequest, InspectOpenXrHeadsetFramePlanResult>(
            "rekall.render.openxr.inspect_headset_frame_plan",
            new InspectOpenXrHeadsetFramePlanRequest(root, scene, frameCount, viewportWidth, viewportHeight),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Headset session ready: {result.Value.HeadsetSessionReady}");
        Console.WriteLine($"HMD system available: {result.Value.HmdSystemAvailable}");
        Console.WriteLine($"System id: {result.Value.SystemId?.ToString(CultureInfo.InvariantCulture) ?? "<none>"}");
        Console.WriteLine($"Active camera: {result.Value.ActiveCamera ?? "<none>"}");
        Console.WriteLine($"Stereo enabled: {result.Value.StereoEnabled}");
        Console.WriteLine($"View configuration: {result.Value.ViewConfiguration}");
        Console.WriteLine($"Render mode: {result.Value.StereoRenderMode}");
        Console.WriteLine($"Eyes: {result.Value.EyeCount}; multiview: {result.Value.UsesMultiview}");
        Console.WriteLine($"Color swapchains: {result.Value.ColorSwapchainCount}; depth swapchains: {result.Value.DepthSwapchainCount}; array size: {result.Value.SwapchainArraySize}");
        Console.WriteLine($"Recommended eye size: {result.Value.RecommendedEyeWidth}x{result.Value.RecommendedEyeHeight}");
        Console.WriteLine($"Geometry: {result.Value.VertexCount} vertices, {result.Value.IndexCount} indices, {result.Value.DrawCount} draws");
        Console.WriteLine($"Native Vulkan target: {result.Value.NativeVulkanTargetKind}; ready: {result.Value.NativeVulkanSceneTargetReady}");
        Console.WriteLine($"Native Vulkan sync owner: {result.Value.NativeVulkanSynchronizationOwner}");
        Console.WriteLine($"Native Vulkan ownership: colorImages={result.Value.NativeVulkanOwnsColorImages}; depthImages={result.Value.NativeVulkanOwnsDepthImages}; readback={result.Value.NativeVulkanOwnsReadbackBuffers}");
        Console.WriteLine($"Native Vulkan framebuffers: {result.Value.NativeVulkanFramebufferCountPerSwapchainImage} per swapchain image; color views: {result.Value.NativeVulkanColorImageViewCountPerSwapchainImage}");
        Console.WriteLine($"Native Vulkan command passes: {result.Value.NativeVulkanRenderPassesPerFrame}; frame uniforms: {result.Value.NativeVulkanFrameUniformBuffers}; compositor color handoff: {result.Value.NativeVulkanLeavesColorForCompositor}");
        foreach (var step in result.Value.NativeVulkanRenderSteps)
        {
            Console.WriteLine($"Native Vulkan: {step}");
        }

        foreach (var call in result.Value.RequiredOpenXrCalls)
        {
            Console.WriteLine($"Call: {call}");
        }

        foreach (var step in result.Value.FrameLoopSteps)
        {
            Console.WriteLine($"Frame: {step}");
        }

        foreach (var blocker in result.Value.Blockers)
        {
            Console.WriteLine($"Blocker: {blocker}");
        }

        foreach (var warning in result.Value.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
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
        Console.WriteLine($"Transactions: {model.Transactions.Transactions.Count}");
        Console.WriteLine($"Components: {model.SceneSummary.ComponentCount}");
        Console.WriteLine($"Actions: {model.Actions.Actions.Count}");
        foreach (var action in model.Actions.Actions.Where(action => action.Recommended))
        {
            Console.WriteLine($"- {action.Tool}: {action.Label}");
        }

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

    private static async Task<int> GenerateTripoModelAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string prompt,
        string displayName,
        string? modelVersion)
    {
        var result = await registry.ExecuteAsync<GenerateTripoModelRequest, GenerateTripoModelResult>(
            "rekall.asset.tripo.generate_model",
            new GenerateTripoModelRequest(
                root,
                prompt,
                displayName,
                ModelVersion: modelVersion),
            context);
        Console.WriteLine(result.Summary);
        if (result.Value.TaskId is not null)
        {
            Console.WriteLine($"Task: {result.Value.TaskId}");
            Console.WriteLine($"Status: {result.Value.Status}");
        }

        if (result.Value.Asset is not null)
        {
            Console.WriteLine($"Asset: {result.Value.Asset.Id}");
            Console.WriteLine($"Imported: {result.Value.Asset.ImportedPath}");
        }

        foreach (var nextAction in result.Value.NextActions)
        {
            Console.WriteLine($"Next: {nextAction}");
        }

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

    private static async Task<int> CreateGeometryPrimitiveAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string name,
        string primitive,
        string x,
        string y,
        string z,
        string color)
    {
        var result = await registry.ExecuteAsync<CreateGeometryPrimitiveRequest, CreateGeometryPrimitiveResult>(
            "rekall.geometry.create_primitive",
            new CreateGeometryPrimitiveRequest(
                root,
                scene,
                name,
                primitive,
                double.Parse(x, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(y, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(z, System.Globalization.CultureInfo.InvariantCulture),
                Color: color),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EntityId);
        Console.WriteLine(result.Value.Primitive);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateGeometryMeshAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string name,
        string verticesJson,
        string indicesJson,
        string x,
        string y,
        string z,
        string color,
        string? textureAssetId = null)
    {
        var result = await registry.ExecuteAsync<CreateGeometryMeshRequest, CreateGeometryMeshResult>(
            "rekall.geometry.create_mesh",
            new CreateGeometryMeshRequest(
                root,
                scene,
                name,
                ParseGeometryMeshVertices(verticesJson),
                ParseGeometryMeshIndices(indicesJson),
                double.Parse(x, CultureInfo.InvariantCulture),
                double.Parse(y, CultureInfo.InvariantCulture),
                double.Parse(z, CultureInfo.InvariantCulture),
                Color: color,
                TextureAssetId: textureAssetId),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EntityId);
        Console.WriteLine(result.Value.VertexCount);
        Console.WriteLine(result.Value.IndexCount);
        return result.Ok ? 0 : 1;
    }

    private static IReadOnlyList<CreateGeometryMeshVertex> ParseGeometryMeshVertices(string json)
    {
        var node = JsonNode.Parse(ReadJsonArgument(json)) as JsonArray
            ?? throw new ArgumentException("Geometry mesh vertices must be a JSON array.");
        var vertices = new List<CreateGeometryMeshVertex>(node.Count);
        for (var i = 0; i < node.Count; i++)
        {
            var item = node[i] as JsonObject
                ?? throw new ArgumentException("Geometry mesh vertices must be JSON objects.");
            vertices.Add(new CreateGeometryMeshVertex(
                ReadRequiredNumber(item, "x"),
                ReadRequiredNumber(item, "y"),
                ReadRequiredNumber(item, "z"),
                ReadOptionalNumber(item, "nx") ?? ReadOptionalNumber(item, "normalX"),
                ReadOptionalNumber(item, "ny") ?? ReadOptionalNumber(item, "normalY"),
                ReadOptionalNumber(item, "nz") ?? ReadOptionalNumber(item, "normalZ"),
                ReadOptionalNumber(item, "r"),
                ReadOptionalNumber(item, "g"),
                ReadOptionalNumber(item, "b"),
                ReadOptionalNumber(item, "a"),
                ReadNumber(item, "u", 0),
                ReadNumber(item, "v", 0)));
        }

        return vertices;
    }

    private static IReadOnlyList<ushort> ParseGeometryMeshIndices(string json)
    {
        var node = JsonNode.Parse(ReadJsonArgument(json)) as JsonArray
            ?? throw new ArgumentException("Geometry mesh indices must be a JSON array.");
        var indices = new List<ushort>(node.Count);
        foreach (var item in node)
        {
            if (item is not JsonValue value || !value.TryGetValue<int>(out var integer) || integer < 0 || integer > ushort.MaxValue)
            {
                throw new ArgumentException("Geometry mesh indices must be unsigned 16-bit integers.");
            }

            indices.Add((ushort)integer);
        }

        return indices;
    }

    private static async Task<int> CreateGeometryRecipeAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string name,
        string partsJson,
        string x,
        string y,
        string z,
        string color)
    {
        var result = await registry.ExecuteAsync<CreateGeometryRecipeRequest, CreateGeometryRecipeResult>(
            "rekall.geometry.create_recipe",
            new CreateGeometryRecipeRequest(
                root,
                scene,
                name,
                ParseGeometryRecipeParts(partsJson),
                double.Parse(x, CultureInfo.InvariantCulture),
                double.Parse(y, CultureInfo.InvariantCulture),
                double.Parse(z, CultureInfo.InvariantCulture),
                Color: color),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EntityId);
        Console.WriteLine(result.Value.PartCount);
        Console.WriteLine(result.Value.VertexCount);
        Console.WriteLine(result.Value.IndexCount);
        return result.Ok ? 0 : 1;
    }

    private static IReadOnlyList<CreateGeometryRecipePart> ParseGeometryRecipeParts(string json)
    {
        var node = JsonNode.Parse(ReadJsonArgument(json)) as JsonArray
            ?? throw new ArgumentException("Geometry recipe parts must be a JSON array.");
        var parts = new List<CreateGeometryRecipePart>(node.Count);
        foreach (var itemNode in node)
        {
            var item = itemNode as JsonObject
                ?? throw new ArgumentException("Geometry recipe parts must be JSON objects.");
            parts.Add(new CreateGeometryRecipePart(
                ReadRequiredString(item, "kind"),
                ReadNumber(item, "x", 0),
                ReadNumber(item, "y", 0),
                ReadNumber(item, "z", 0),
                ReadNumber(item, "pitch", 0),
                ReadNumber(item, "yaw", 0),
                ReadNumber(item, "roll", 0),
                ReadNumber(item, "scaleX", 1),
                ReadNumber(item, "scaleY", 1),
                ReadNumber(item, "scaleZ", 1),
                ReadOptionalString(item, "color"),
                ReadInt(item, "segments", 24),
                ReadInt(item, "rings", 12)));
        }

        return parts;
    }

    private static async Task<int> CreateGeometryExtrusionAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string name,
        string profileJson,
        string depth,
        string x,
        string y,
        string z,
        string color)
    {
        var result = await registry.ExecuteAsync<CreateGeometryExtrusionRequest, CreateGeometryExtrusionResult>(
            "rekall.geometry.create_extrusion",
            new CreateGeometryExtrusionRequest(
                root,
                scene,
                name,
                ParseGeometryExtrusionProfile(profileJson),
                double.Parse(depth, CultureInfo.InvariantCulture),
                double.Parse(x, CultureInfo.InvariantCulture),
                double.Parse(y, CultureInfo.InvariantCulture),
                double.Parse(z, CultureInfo.InvariantCulture),
                Color: color),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EntityId);
        Console.WriteLine(result.Value.VertexCount);
        Console.WriteLine(result.Value.IndexCount);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ImportKsaPlanetAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string ksaRoot,
        string bodyId,
        string? entityName)
    {
        var result = await registry.ExecuteAsync<ImportKsaPlanetRequest, ImportKsaPlanetResult>(
            "rekall.planet.import_ksa",
            new ImportKsaPlanetRequest(root, scene, ksaRoot, bodyId, entityName),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EntityId);
        Console.WriteLine(result.Value.ImportedAssetCount);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ImportKsaSolarSystemAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string ksaRoot,
        string systemFileName,
        string distanceScale,
        string radiusScale)
    {
        var result = await registry.ExecuteAsync<ImportKsaSolarSystemRequest, ImportKsaSolarSystemResult>(
            "rekall.solar.import_ksa_system",
            new ImportKsaSolarSystemRequest(
                root,
                scene,
                ksaRoot,
                systemFileName,
                DistanceScale: double.Parse(distanceScale, CultureInfo.InvariantCulture),
                RadiusScale: double.Parse(radiusScale, CultureInfo.InvariantCulture)),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Bodies: {result.Value.BodyCount}");
        Console.WriteLine($"Imported assets: {result.Value.ImportedAssetCount}");
        Console.WriteLine(string.Join(", ", result.Value.BodyIds));
        return result.Ok ? 0 : 1;
    }

    private static IReadOnlyList<CreateGeometryExtrusionPoint> ParseGeometryExtrusionProfile(string json)
    {
        var node = JsonNode.Parse(ReadJsonArgument(json)) as JsonArray
            ?? throw new ArgumentException("Geometry extrusion profile must be a JSON array.");
        var profile = new List<CreateGeometryExtrusionPoint>(node.Count);
        foreach (var itemNode in node)
        {
            var item = itemNode as JsonObject
                ?? throw new ArgumentException("Geometry extrusion profile points must be JSON objects.");
            profile.Add(new CreateGeometryExtrusionPoint(
                ReadRequiredNumber(item, "x"),
                ReadRequiredNumber(item, "y")));
        }

        return profile;
    }

    private static string ReadJsonArgument(string value)
    {
        if (value.Length > 1 && value[0] == '@')
        {
            return File.ReadAllText(value[1..]);
        }

        return value;
    }

    private static double ReadRequiredNumber(JsonObject item, string name)
    {
        if (!item.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            throw new ArgumentException($"Geometry mesh vertex is missing required '{name}' coordinate.");
        }

        return ReadNumber(value, name);
    }

    private static string ReadRequiredString(JsonObject item, string name)
    {
        return ReadOptionalString(item, name)
            ?? throw new ArgumentException($"JSON object is missing required '{name}' string property.");
    }

    private static string? ReadOptionalString(JsonObject item, string name)
    {
        if (!item.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        throw new ArgumentException($"JSON object property '{name}' must be a string.");
    }

    private static double ReadNumber(JsonObject item, string name, double fallback)
    {
        return item.TryGetPropertyValue(name, out var node) && node is JsonValue value
            ? ReadNumber(value, name)
            : fallback;
    }

    private static double? ReadOptionalNumber(JsonObject item, string name)
    {
        return item.TryGetPropertyValue(name, out var node) && node is JsonValue value
            ? ReadNumber(value, name)
            : null;
    }

    private static double ReadNumber(JsonValue value, string name)
    {
        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Geometry mesh vertex property '{name}' must be numeric.");
    }

    private static int ReadInt(JsonObject item, string name, int fallback)
    {
        if (!item.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return (int)number;
        }

        if (value.TryGetValue<string>(out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"JSON object property '{name}' must be an integer.");
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
        var inputs = await ParseRuntimeInputFramesAsync(inputsJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<CapturePlayableFrameRequest, CapturePlayableFrameResult>(
            "rekall.play.capture_frame",
            new CapturePlayableFrameRequest(root, scene, outputDirectory, parsedFrameIndex, InputFrames: inputs),
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
        string moduleName)
    {
        var result = await registry.ExecuteAsync<ScaffoldPlayableModuleRequest, ScaffoldPlayableModuleResult>(
            "rekall.module.scaffold_playable",
            new ScaffoldPlayableModuleRequest(root, moduleId, displayName, moduleName),
            context);
        Console.WriteLine(result.Value.SourcePath);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ScaffoldRuntimeSystemModuleAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string moduleId,
        string displayName,
        string moduleName,
        string componentName,
        string systemName)
    {
        var result = await registry.ExecuteAsync<ScaffoldRuntimeSystemModuleRequest, ScaffoldRuntimeSystemModuleResult>(
            "rekall.module.scaffold_runtime_system",
            new ScaffoldRuntimeSystemModuleRequest(root, moduleId, displayName, moduleName, componentName, systemName),
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

    private static async Task<int> RunAgentAuthoringGauntletAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string name,
        string scene,
        string? outputDirectory)
    {
        var result = await registry.ExecuteAsync<RunAgentAuthoringGauntletRequest, RunAgentAuthoringGauntletResult>(
            "rekall.workflow.agent_authoring_gauntlet",
            new RunAgentAuthoringGauntletRequest(root, name, scene, outputDirectory),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Agent authoring gauntlet ready: {result.Value.Ready}");
        Console.WriteLine($"Authoring mode: {result.Value.AuthoringMode}");
        Console.WriteLine($"Project: {result.Value.ProjectRoot}");
        Console.WriteLine($"Scene: {result.Value.SceneName}");
        foreach (var check in result.Value.Checks)
        {
            Console.WriteLine($"{check.Name}: {check.Passed} - {check.Summary}");
        }

        if (result.Value.Package is not null)
        {
            Console.WriteLine($"Package archive: {result.Value.Package.ArchivePath}");
            Console.WriteLine($"Package manifest: {result.Value.Package.ManifestPath}");
        }

        if (result.Value.Audit is not null)
        {
            Console.WriteLine($"Proof frame: {result.Value.Audit.Capture.OutputPath}");
            Console.WriteLine($"Proof frame non-blank: {result.Value.Audit.Capture.NonBlank}");
        }

        Console.WriteLine("Next actions:");
        foreach (var action in result.Value.NextActions)
        {
            Console.WriteLine($"  {action}");
        }

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
        bool graphics = false,
        string? target = null)
    {
        var result = await registry.ExecuteAsync<PackagePlayableGameRequest, PackagePlayableGameResult>(
            "rekall.workflow.package_playable_game",
            new PackagePlayableGameRequest(root, scene, outputDirectory, Graphics: graphics, Target: target),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        if (result.Ok)
        {
            Console.WriteLine($"Target: {result.Value.Target}");
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

    private static async Task<int> PublishWebGameAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string? outputDirectory)
    {
        var result = await registry.ExecuteAsync<PublishWebGameRequest, PublishWebGameResult>(
            "rekall.game.publish_web",
            new PublishWebGameRequest(root, scene, outputDirectory),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        if (result.Ok)
        {
            Console.WriteLine($"Output: {result.Value.OutputDirectory}");
            Console.WriteLine($"Manifest: {result.Value.ManifestPath}");
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

    private static async Task<int> AuditWebGameAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string? outputDirectory)
    {
        var result = await registry.ExecuteAsync<AuditWebGameRequest, AuditWebGameResult>(
            "rekall.game.audit_web",
            new AuditWebGameRequest(root, scene, outputDirectory),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        Console.WriteLine($"Output: {result.Value.Publish.OutputDirectory}");
        foreach (var check in result.Value.Checks)
        {
            Console.WriteLine($"  [{(check.Passed ? "pass" : "fail")}] {check.Name}: {check.Summary}");
        }
        Console.WriteLine($"Browser smoke frame verified: {result.Value.BrowserSmokeFrameVerified} ({result.Value.BrowserSmokeFrameSummary})");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
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
        Console.WriteLine($"Scene: {result.Value.Manifest.SceneName}");
        Console.WriteLine($"Launch: {result.Value.Manifest.LaunchPath}");
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

    private static async Task<int> InstallModuleSdkAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<InstallModuleSdkRequest, RekallAgeModuleSdkInstallation>(
            "rekall.module.install_sdk",
            new InstallModuleSdkRequest(root),
            context);
        Console.WriteLine(result.Summary);
        if (result.Ok)
        {
            Console.WriteLine($"SDK root: {result.Value.SdkRoot}");
            Console.WriteLine($"Integrity manifest: {result.Value.ManifestPath}");
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

    private static async Task<int> AddComponentAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string componentType,
        string propertiesJson)
    {
        var properties = JsonNode.Parse(propertiesJson) as JsonObject
            ?? throw new ArgumentException("Component properties must be a JSON object.", nameof(propertiesJson));
        var result = await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(root, scene, entityId, componentType, properties),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RelocatePlayablePackageAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string packagePath,
        string destination)
    {
        var result = await registry.ExecuteAsync<RelocatePlayablePackageRequest, RelocatePlayablePackageResult>(
            "rekall.workflow.relocate_playable_package",
            new RelocatePlayablePackageRequest(packagePath, destination),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        Console.WriteLine($"Package: {result.Value.PackagePath}");
        Console.WriteLine($"Manifest: {result.Value.ManifestPath}");
        Console.WriteLine($"Files: {result.Value.FileCount}");
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
        string frameIndex,
        string? inputsJson = null)
    {
        var frame = int.Parse(frameIndex, System.Globalization.CultureInfo.InvariantCulture);
        var inputs = await ParseRuntimeInputFramesAsync(inputsJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<CapturePlayablePackageFrameRequest, CapturePlayablePackageFrameResult>(
            "rekall.workflow.capture_playable_package_frame",
            new CapturePlayablePackageFrameRequest(packagePath, outputDirectory, frame, InputFrames: inputs),
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
        string capability,
        string? expectedRevision = null)
    {
        var result = await registry.ExecuteAsync<AddCapabilityRequest, AddCapabilityResult>(
            "rekall.capability.add",
            new AddCapabilityRequest(root, capability, expectedRevision),
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
        string tags,
        string? expectedRevision = null)
    {
        var result = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, scene, name, SplitCsv(tags), expectedRevision),
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
        Console.WriteLine($"Revision: {summary.Revision}");
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
        Console.WriteLine($"Version: {result.Value.Product.Version}");
        Console.WriteLine($"Channel: {result.Value.Product.Channel}");
        Console.WriteLine($"Proprietary: {result.Value.Product.Proprietary}");
        Console.WriteLine($"Supported host: {result.Value.Product.SupportedHost}");
        Console.WriteLine($"Module SDK compatibility: {result.Value.Product.ModuleSdkCompatibilityVersion}");
        Console.WriteLine($"Rendering: {result.Value.RenderingPosture}");
        Console.WriteLine("Capabilities:");
        foreach (var capability in result.Value.Capabilities)
        {
            Console.WriteLine($"  {capability.Id} [{capability.Stability}] - {capability.Summary}");
        }

        Console.WriteLine("Workflow tools:");
        foreach (var workflow in result.Value.WorkflowTools)
        {
            var marker = workflow.Recommended ? "recommended" : "available";
            Console.WriteLine($"  {workflow.Tool} [{marker}] - {workflow.Purpose}");
        }

        Console.WriteLine("Authoring contracts:");
        foreach (var contract in result.Value.AuthoringContracts)
        {
            Console.WriteLine($"  {contract.Name}: {contract.PrimaryType}");
            Console.WriteLine($"    {contract.Purpose}");
            Console.WriteLine($"    Capabilities: {string.Join(", ", contract.Capabilities)}");
            Console.WriteLine($"    Tools: {string.Join(", ", contract.RelatedTools)}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectModuleTrustAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<InspectModuleTrustRequest, InspectModuleTrustResult>(
            "rekall.module.inspect_trust",
            new InspectModuleTrustRequest(root),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Ready: {result.Value.Ready}");
        Console.WriteLine($"Trust posture: {result.Value.TrustPosture}");
        foreach (var module in result.Value.Modules)
        {
            Console.WriteLine($"Module {module.ModuleName}: ready={module.Ready}; artifacts={module.OutputFiles.Count}; receipt={module.ReceiptPath}");
        }
        foreach (var issue in result.Value.Issues)
        {
            Console.WriteLine($"{issue.Code}: {issue.Message} [{issue.Target}]");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectFailureReportsAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string? root)
    {
        var result = await registry.ExecuteAsync<InspectFailureReportsRequest, InspectFailureReportsResult>(
            "rekall.diagnostics.inspect_failures",
            new InspectFailureReportsRequest(root),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Diagnostics root: {result.Value.Root}");
        foreach (var report in result.Value.Reports)
        {
            Console.WriteLine(
                $"{report.TimestampUtc:O} {report.Component} {report.Outcome} {report.Code} " +
                $"mode={report.RecoveryMode} attempts={report.Attempts} frames={report.CompletedFrames}/{report.RequestedFrames?.ToString() ?? "continuous"}");
            Console.WriteLine($"  Report: {report.Path}");
            Console.WriteLine($"  Exception: {report.ExceptionType}: {report.ExceptionMessage}");
        }
        foreach (var issue in result.Value.Issues)
        {
            Console.WriteLine($"{issue.Code}: {issue.Message} [{issue.Target}]");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectProjectCompatibilityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<InspectProjectCompatibilityRequest, InspectProjectCompatibilityResult>(
            "rekall.compatibility.inspect_project",
            new InspectProjectCompatibilityRequest(root),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Project root: {result.Value.ProjectRoot}");
        Console.WriteLine($"Current: {result.Value.IsCurrent}; migratable: {result.Value.CanMigrate}");
        foreach (var document in result.Value.Documents)
        {
            Console.WriteLine(
                $"{document.RelativePath}: {document.Status} {document.Code} " +
                $"schema={document.DetectedVersion?.ToString() ?? "unknown"}/{document.CurrentVersion}");
        }
        foreach (var blocker in result.Value.Blockers)
        {
            Console.WriteLine($"{blocker.Code}: {blocker.Message} [{blocker.Target}]");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> MigrateProjectCompatibilityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        bool apply)
    {
        var result = await registry.ExecuteAsync<MigrateProjectCompatibilityRequest, MigrateProjectCompatibilityResult>(
            "rekall.compatibility.migrate_project",
            new MigrateProjectCompatibilityRequest(root, apply),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Applied: {result.Value.Applied}; no-op: {result.Value.NoOp}");
        if (!string.IsNullOrWhiteSpace(result.Value.BackupRoot))
        {
            Console.WriteLine($"Backup root: {result.Value.BackupRoot}");
        }
        foreach (var document in result.Value.Documents)
        {
            Console.WriteLine(
                $"{document.RelativePath}: schema {document.FromVersion}->{document.ToVersion} " +
                $"sha256={document.OriginalSha256}->{document.MigratedSha256}");
        }
        foreach (var blocker in result.Value.Blockers)
        {
            Console.WriteLine($"{blocker.Code}: {blocker.Message} [{blocker.Target}]");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectDocumentRecoveryAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string kind,
        string? sceneName)
    {
        var result = await registry.ExecuteAsync<InspectDocumentRecoveryRequest, InspectDocumentRecoveryResult>(
            "rekall.recovery.inspect_document",
            new InspectDocumentRecoveryRequest(root, kind, sceneName),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Document: {result.Value.DocumentPath}");
        Console.WriteLine($"Previous: {result.Value.PreviousPath}");
        Console.WriteLine(
            $"Primary: {result.Value.Primary.Code} revision={result.Value.Primary.Revision}; " +
            $"previous: {result.Value.Previous.Code} revision={result.Value.Previous.Revision}; " +
            $"recoverable: {result.Value.Recoverable}");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message} [{error.Target}]");
        }
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RestoreDocumentRecoveryAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string kind,
        string? sceneName,
        string expectedRevision)
    {
        var result = await registry.ExecuteAsync<RestoreDocumentRecoveryRequest, RestoreDocumentRecoveryResult>(
            "rekall.recovery.restore_document",
            new RestoreDocumentRecoveryRequest(root, kind, expectedRevision, sceneName),
            context);
        Console.WriteLine(result.Summary);
        if (result.Ok)
        {
            Console.WriteLine($"Restored revision: {result.Value.RestoredRevision}");
            Console.WriteLine($"Quarantine directory: {result.Value.QuarantineDirectory}");
        }
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message} [{error.Target}]");
        }
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RemoveComponentPropertyAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string componentType,
        string propertyName)
    {
        var result = await registry.ExecuteAsync<RemoveComponentPropertyRequest, RemoveComponentPropertyResult>(
            "rekall.component.remove_property",
            new RemoveComponentPropertyRequest(root, scene, entityId, componentType, propertyName),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static int ListLanguageModelProviders()
    {
        foreach (var provider in new RekallAgeLanguageModelProviderCatalog().Providers)
        {
            Console.WriteLine($"{provider.Id}\t{provider.DisplayName}\t{provider.DefaultModel}\t{provider.AuthenticationKind}");
        }

        return 0;
    }

    private static void WriteLanguageModelProviderDiagnostic(RekallAgeLanguageModelProviderException error)
    {
        Console.Error.WriteLine($"{error.Code}: {error.Message}");
        if (!string.IsNullOrWhiteSpace(error.RequestedValue))
        {
            Console.Error.WriteLine($"Requested: {BoundedLanguageModelDiagnosticValue(error.RequestedValue)}");
        }

        if (!string.IsNullOrWhiteSpace(error.ResolvedValue))
        {
            Console.Error.WriteLine($"Resolved: {BoundedLanguageModelDiagnosticValue(error.ResolvedValue)}");
        }
    }

    private static string BoundedLanguageModelDiagnosticValue(string value) =>
        value.Length <= 128 ? value : value[..127] + "…";

    private static async Task<int> ListLanguageModelsAsync(
        RekallAgeCommandRegistry registry,
        string provider,
        CancellationToken cancellationToken)
    {
        var catalog = new RekallAgeLanguageModelProviderCatalog();
        var descriptor = catalog.Providers.SingleOrDefault(item => item.Id.Equals(provider, StringComparison.OrdinalIgnoreCase));
        using var lease = catalog.Acquire(provider, registry);
        var models = await lease.Runner.ListModelsAsync(cancellationToken);
        Console.WriteLine($"{descriptor?.DisplayName ?? lease.ProviderId} models: {models.Count}");
        foreach (var model in models)
        {
            Console.WriteLine($"{model.Id}\t{model.SizeBytes}");
        }

        return 0;
    }

    private static async Task<int> RunLanguageModelAgentAsync(
        RekallAgeCommandRegistry registry,
        string provider,
        string model,
        string taskOrPath,
        string maxTurnsText,
        CancellationToken cancellationToken)
    {
        var task = File.Exists(taskOrPath)
            ? await File.ReadAllTextAsync(taskOrPath, cancellationToken)
            : taskOrPath;
        if (!int.TryParse(maxTurnsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxTurns))
        {
            Console.Error.WriteLine($"Invalid maximum turn count '{maxTurnsText}'.");
            return 2;
        }

        using var lease = new RekallAgeLanguageModelProviderCatalog().Acquire(provider, registry);
        var tools = new RekallAgeMcpAgentToolExecutor(registry, $"rekall-{lease.ProviderId}-agent", progressiveDiscovery: true);
        var agent = new RekallAgeLanguageModelAgent(lease.ModelClient, tools);
        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest(
                model,
                RekallAgeEmbeddedAgentContract.SystemPrompt,
                task)
            {
                MaxTurns = maxTurns,
                RequireCompletionAudit = true
            },
            cancellationToken);
        Console.WriteLine(result.FinalContent);
        Console.WriteLine($"Provider: {lease.ProviderId}");
        Console.WriteLine("Tool execution trace: " + string.Join(" -> ", result.ToolExecutions.Select(execution =>
            execution.Name == "rekall.tools.execute"
                ? execution.Arguments["name"]?.GetValue<string>() ?? execution.Name
                : execution.Name)));
        var failures = RekallAgeLanguageModelAgentDiagnostics.FormatFailures(result.ToolExecutions);
        if (failures.Length > 0)
        {
            Console.WriteLine(failures);
        }
        Console.WriteLine(
            $"Agent completed={result.Completed} stop={result.StopReason} turns={result.Turns} tools={result.ToolCallCount} promptTokens={result.Usage.PromptTokens} completionTokens={result.Usage.CompletionTokens}");
        return result.Completed ? 0 : 1;
    }

    private static async Task<int> RunProjectLanguageModelAgentAsync(
        RekallAgeCommandRegistry registry,
        string provider,
        string model,
        string projectRoot,
        string sceneName,
        string taskOrPath,
        string maxTurnsText,
        CancellationToken cancellationToken)
    {
        var task = File.Exists(taskOrPath)
            ? await File.ReadAllTextAsync(taskOrPath, cancellationToken)
            : taskOrPath;
        if (!int.TryParse(maxTurnsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxTurns))
        {
            Console.Error.WriteLine($"Invalid maximum turn count '{maxTurnsText}'.");
            return 2;
        }

        using var lease = new RekallAgeLanguageModelProviderCatalog().Acquire(provider, registry);
        var result = await lease.Runner.RunAsync(
            new RekallAgeProjectAgentSessionRequest(projectRoot, sceneName, model, task)
            {
                MaxTurns = maxTurns,
                RequireCompletionAudit = true
            },
            progress: null,
            cancellationToken);
        Console.WriteLine(result.AgentResult.FinalContent);
        Console.WriteLine($"Provider: {lease.ProviderId}");
        Console.WriteLine("Tool execution trace: " + string.Join(" -> ", result.AgentResult.ToolExecutions.Select(execution =>
            execution.Name == "rekall.tools.execute"
                ? execution.Arguments["name"]?.GetValue<string>() ?? execution.Name
                : execution.Name)));
        var failures = RekallAgeLanguageModelAgentDiagnostics.FormatFailures(result.AgentResult.ToolExecutions);
        if (failures.Length > 0) Console.WriteLine(failures);
        Console.WriteLine(result.Summary);
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> AssembleDistributionAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string output,
        string cli,
        string studio,
        string headless,
        string windows,
        string sdk,
        string readme,
        string notice,
        string thirdParty)
    {
        var result = await registry.ExecuteAsync<AssembleDistributionRequest, AssembleDistributionCommandResult>(
            "rekall.distribution.assemble",
            new AssembleDistributionRequest(
                output,
                cli,
                studio,
                headless,
                windows,
                sdk,
                readme,
                notice,
                thirdParty),
            context);
        Console.WriteLine(result.Summary);
        if (result.Value.Manifest is not null)
        {
            Console.WriteLine($"Root: {result.Value.Root}");
            Console.WriteLine($"Manifest: {result.Value.ManifestPath}");
            Console.WriteLine($"Archive: {result.Value.ArchivePath}");
            Console.WriteLine($"Version: {result.Value.Manifest.ProductVersion}");
            Console.WriteLine($"Files: {result.Value.Manifest.Files.Count}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> PrintEngineDoctorAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string? projectRoot)
    {
        var result = await registry.ExecuteAsync<InspectEngineDoctorRequest, InspectEngineDoctorResult>(
            "rekall.context.doctor",
            new InspectEngineDoctorRequest(projectRoot),
            context);
        Console.WriteLine(
            $"Rekall AGE doctor {result.Value.Product.Version} [{result.Value.Product.Channel}]");
        foreach (var check in result.Value.Checks)
        {
            Console.WriteLine($"{check.Id}: {check.Status} [{check.Severity}] - {check.Summary}");
            foreach (var evidence in check.Evidence)
            {
                Console.WriteLine($"  {evidence}");
            }

            foreach (var action in check.NextActions)
            {
                Console.WriteLine($"  Next: {action.Tool}");
            }
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ValidateProjectAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root)
    {
        var result = await registry.ExecuteAsync<ValidateProjectRequest, ValidateProjectResult>(
            "rekall.validation.project",
            new ValidateProjectRequest(root),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Status: {result.Value.Status}");
        Console.WriteLine(
            $"Scenes: {result.Value.SceneCount}; issues: {result.Value.IssueCount} " +
            $"(blocking {result.Value.BlockingCount}, warnings {result.Value.WarningCount})");
        foreach (var scene in result.Value.Scenes)
        {
            Console.WriteLine($"  {scene.SceneName}: {scene.Status}, {scene.IssueCount} issue(s)");
            foreach (var issue in scene.Issues)
            {
                Console.WriteLine($"    {issue.Severity} {issue.Code} {issue.Target ?? scene.SceneName}: {issue.Message}");
            }
        }
        foreach (var action in result.Value.SuggestedNextActions)
        {
            Console.WriteLine($"Next: {action.Tool}");
        }
        return result.Ok && result.Value.BlockingCount == 0 ? 0 : 1;
    }

    private static async Task<int> ValidateSceneAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene)
    {
        var result = await registry.ExecuteAsync<ValidateSceneRequest, ValidateSceneResult>(
            "rekall.validation.scene",
            new ValidateSceneRequest(root, scene),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Scene: {result.Value.SceneName}");
        Console.WriteLine($"Status: {result.Value.Status}");
        Console.WriteLine(
            $"Issues: {result.Value.IssueCount} (blocking {result.Value.BlockingCount}, warnings {result.Value.WarningCount})");
        foreach (var issue in result.Value.Issues)
        {
            Console.WriteLine($"{issue.Severity} {issue.Code} {issue.Target ?? "<scene>"}: {issue.Message}");
        }

        foreach (var action in result.Value.SuggestedNextActions)
        {
            Console.WriteLine($"Next: {action.Tool}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.BlockingCount == 0 ? 0 : 1;
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
        Console.WriteLine($"Revision: {summary.Revision}");
        Console.WriteLine($"Components: {string.Join(", ", summary.ComponentTypes)}");
        if (!string.IsNullOrWhiteSpace(summary.HeadsetCameraName))
        {
            Console.WriteLine($"Headset camera: {summary.HeadsetCameraName}");
        }

        foreach (var camera in summary.Cameras)
        {
            Console.WriteLine($"Camera: {camera.EntityName}; kind: {camera.Kind}; active: {camera.Active}; order: {camera.RenderOrder}; viewport: {camera.ViewportX},{camera.ViewportY} {camera.ViewportWidth}x{camera.ViewportHeight}; culling mask: {camera.CullingMask}; stereo: {camera.StereoMode}; headset: {camera.DrivesHeadsetOutput}");
        }

        foreach (var layer in summary.RenderLayers)
        {
            Console.WriteLine($"Render layer: {layer.Layer}; renderables: {layer.RenderableCount}; entities: {string.Join(", ", layer.EntityNames)}");
        }

        foreach (var entity in summary.Entities)
        {
            Console.WriteLine($"- {entity.Name}: {string.Join(", ", entity.Components)}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> PrintTransactionHistoryAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string limit)
    {
        var count = int.Parse(limit, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<ListTransactionHistoryRequest, ListTransactionHistoryResult>(
            "rekall.transaction.history",
            new ListTransactionHistoryRequest(root, count),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.LogPath);
        foreach (var transaction in result.Value.Transactions)
        {
            Console.WriteLine($"{transaction.StartedAtUtc:u} {transaction.Name} [{transaction.Actor}] {transaction.ChangedResources.Count} change(s) {transaction.Id}");
            foreach (var resource in transaction.ResourceChanges)
            {
                var state = resource.Exists
                    ? resource.SizeBytes is { } size ? $"file {size} bytes" : "directory"
                    : "missing";
                Console.WriteLine($"  - {resource.RelativePath} {resource.Kind} {state}");
            }

            foreach (var preimage in transaction.ResourcePreimages)
            {
                var snapshot = preimage.SnapshotPath is null ? "<none>" : preimage.SnapshotPath;
                Console.WriteLine($"  preimage {preimage.RelativePath} existed={preimage.ExistedBefore} snapshot={snapshot}");
            }
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RestoreTransactionPreimageAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string transactionId,
        string relativePath)
    {
        var result = await registry.ExecuteAsync<RestoreTransactionPreimageRequest, RestoreTransactionPreimageResult>(
            "rekall.transaction.restore_preimage",
            new RestoreTransactionPreimageRequest(root, transactionId, relativePath),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Restored: {result.Value.RelativePath}");
        Console.WriteLine($"Bytes: {result.Value.BytesRestored}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RunSceneAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string seconds,
        string? inputsJson)
    {
        var duration = double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture);
        var inputs = await ParseRuntimeInputFramesAsync(inputsJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<RunSceneRequest, RunSceneResult>(
            "rekall.run.scene",
            new RunSceneRequest(root, scene, duration, inputs),
            context);

        Console.WriteLine($"Simulated {scene}: {result.Value.FramesSimulated} frames");
        Console.WriteLine($"Systems: {string.Join(", ", result.Value.ActiveSystems)}");
        Console.WriteLine(
            $"Observation systems: {string.Join(", ", result.Value.Observations.Select(observation => observation.System).Distinct(StringComparer.Ordinal).OrderBy(system => system, StringComparer.Ordinal))}");
        Console.WriteLine($"Input actions: {result.Value.InputActionCount}");
        foreach (var action in result.Value.InputActions)
        {
            Console.WriteLine($"  {action.Name}: value={action.Value} down={action.IsDown} pressed={action.WasPressed} released={action.WasReleased}{FormatPhysicalInputSource(action)}");
        }

        Console.WriteLine($"XR actions: {result.Value.XrActionCount}");
        foreach (var action in result.Value.XrActions)
        {
            Console.WriteLine($"  {action.Hand}/{action.Name}: value={action.Value} down={action.IsDown} pressed={action.WasPressed} released={action.WasReleased}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> WriteShaderIncludeAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string name,
        string source)
    {
        var result = await registry.ExecuteAsync<WriteShaderIncludeRequest, WriteShaderIncludeResult>(
            "rekall.shader.write_include",
            new WriteShaderIncludeRequest(root, name, source),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Path: {result.Value.RelativePath}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectRenderingDeviceWorkloadAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string workloadJson)
    {
        var payload = File.Exists(workloadJson)
            ? await File.ReadAllTextAsync(workloadJson, context.CancellationToken)
            : workloadJson;
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        InspectRenderingDeviceWorkloadRequest? request;
        try
        {
            request = System.Text.Json.JsonSerializer.Deserialize<InspectRenderingDeviceWorkloadRequest>(payload, options);
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.Error.WriteLine($"Rendering workload JSON is invalid: {ex.Message}");
            return 1;
        }
        if (request is null)
        {
            Console.Error.WriteLine("Rendering workload JSON cannot be null.");
            return 1;
        }

        var result = await registry.ExecuteAsync<InspectRenderingDeviceWorkloadRequest, InspectRenderingDeviceWorkloadResult>(
            "rekall.render.device.inspect_workload",
            request,
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result.Value, options));
        return result.Ok && result.Value.Valid ? 0 : 1;
    }

    private static async Task<int> InspectRuntimeGpuWorkloadAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string workloadJson)
    {
        var payload = File.Exists(workloadJson)
            ? await File.ReadAllTextAsync(workloadJson, context.CancellationToken)
            : workloadJson;
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        InspectRuntimeGpuWorkloadRequest? request;
        try
        {
            request = System.Text.Json.JsonSerializer.Deserialize<InspectRuntimeGpuWorkloadRequest>(payload, options);
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.Error.WriteLine($"Runtime GPU workload JSON is invalid: {ex.Message}");
            return 1;
        }
        if (request is null)
        {
            Console.Error.WriteLine("Runtime GPU workload JSON cannot be null.");
            return 1;
        }

        var result = await registry.ExecuteAsync<InspectRuntimeGpuWorkloadRequest, InspectRuntimeGpuWorkloadResult>(
            "rekall.render.device.inspect_runtime_workload", request, context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result.Value, options));
        return result.Ok && result.Value.Valid ? 0 : 1;
    }

    private static async Task<int> PreprocessShaderSourceAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string name,
        string stage)
    {
        var result = await registry.ExecuteAsync<PreprocessShaderSourceRequest, PreprocessShaderSourceResult>(
            "rekall.shader.preprocess",
            new PreprocessShaderSourceRequest(root, name, stage),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Dependencies: {string.Join(", ", result.Value.Dependencies)}");
        foreach (var diagnostic in result.Value.Diagnostics)
        {
            Console.WriteLine($"  {diagnostic.Code}: {diagnostic.Message} ({diagnostic.Path}:{diagnostic.Line})");
        }
        if (result.Ok)
        {
            Console.WriteLine(result.Value.ExpandedSource);
        }
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectInputBindingsAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene)
    {
        var result = await registry.ExecuteAsync<InspectInputBindingsRequest, InspectInputBindingsResult>(
            "rekall.input.inspect_bindings",
            new InspectInputBindingsRequest(root, scene),
            context);
        Console.WriteLine(result.Summary);
        foreach (var binding in result.Value.Bindings)
        {
            Console.WriteLine($"  {binding.ActionName}: entity={binding.EntityName} ({binding.EntityId}) active={binding.Active} binding={binding.Binding.ToJsonString()}");
        }

        if (result.Value.Truncated)
        {
            Console.WriteLine($"  Output truncated: {result.Value.Bindings.Count}/{result.Value.TotalBindingCount} bindings shown.");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RebindInputActionAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string actionName,
        string? bindingJson,
        bool remove)
    {
        JsonObject? binding = null;
        if (!remove)
        {
            var payload = File.Exists(bindingJson)
                ? await File.ReadAllTextAsync(bindingJson, context.CancellationToken)
                : bindingJson;
            binding = JsonNode.Parse(payload ?? string.Empty) as JsonObject
                ?? throw new ArgumentException("Input binding must be a native JSON object or a path to a JSON object file.");
        }

        var result = await registry.ExecuteAsync<RebindInputActionRequest, RebindInputActionResult>(
            "rekall.input.rebind_action",
            new RebindInputActionRequest(root, scene, entityId, actionName, binding, remove),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InspectRuntimeAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string? inputsJson,
        string? assertionsJson)
    {
        var frameCount = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var inputs = await ParseRuntimeInputFramesAsync(inputsJson, context.CancellationToken);
        var assertions = await ParseRuntimeAssertionsAsync(assertionsJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<InspectSceneRuntimeRequest, InspectSceneRuntimeResult>(
            "rekall.runtime.inspect_scene",
            new InspectSceneRuntimeRequest(root, scene, frameCount, inputs, assertions),
            context);

        Console.WriteLine(result.Summary);
        Console.WriteLine($"Entities: {result.Value.EntityCount}");
        Console.WriteLine($"Renderable: {result.Value.RenderableCount}");
        Console.WriteLine($"Visible renderables: {result.Value.VisibleRenderableCount}");
        Console.WriteLine($"Culled renderables: {result.Value.CulledRenderableCount}");
        foreach (var renderable in result.Value.CulledRenderables)
        {
            Console.WriteLine($"Culled: {renderable.EntityName}; layer: {renderable.Layer}; reason: {renderable.Reason}; camera: {renderable.CameraEntityName ?? "none"}; mask: {renderable.CullingMask}");
        }

        Console.WriteLine($"Physics bodies: {result.Value.PhysicsBodyCount}");
        Console.WriteLine($"Physics colliders: {result.Value.PhysicsColliderCount}");
        Console.WriteLine($"Audio: {result.Value.AudioListenerCount} listeners, {result.Value.AudioEmitterCount} emitters");
        Console.WriteLine($"Audio runtime: {result.Value.ActiveAudioVoiceCount} active voices, {result.Value.AudioBusCount} buses, peak gain {result.Value.AudioPeakGain:F3}, {result.Value.AudioMixedSampleCount} mixed samples");
        foreach (var voice in result.Value.AudioVoices)
        {
            Console.WriteLine($"  Audio voice {voice.EntityName}: clip={voice.ClipAssetId} state={voice.State} loop={voice.Loop} time={voice.PlaybackSeconds:F3}/{voice.DurationSeconds:F3}");
        }
        Console.WriteLine($"Animation players: {result.Value.AnimationPlayerCount}");
        foreach (var player in result.Value.AnimationPlayers)
        {
            var graphState = player.StateName is null
                ? string.Empty
                : $" state={player.StateName} previous={player.PreviousStateName ?? "(none)"} transition={player.TransitionProgress:F3}";
            Console.WriteLine($"  Animation {player.EntityName}: kind={player.Kind} inline={player.InlineClip} playing={player.Playing} time={player.TimeSeconds:F3}/{player.DurationSeconds:F3} loop={player.LoopMode} layers={player.ActiveLayerCount}/{player.LayerCount}{graphState}");
            if (player.JointCount > 0)
            {
                Console.WriteLine($"    Skeleton: animation={player.AnimationName ?? "(none)"} skin={player.SkinName ?? "(unnamed)"} joints={player.JointCount}");
            }
            foreach (var layer in player.Layers)
            {
                Console.WriteLine($"    Layer {layer.Name}: clip={layer.ClipAssetId ?? "(none)"} weight={layer.Weight:F3}->{layer.TargetWeight:F3} time={layer.TimeSeconds:F3}/{layer.DurationSeconds:F3} playing={layer.Playing}");
            }
        }
        Console.WriteLine($"Morph states: {result.Value.MorphStates.Count}{(result.Value.MorphStatesTruncated ? "+ (truncated)" : string.Empty)}");
        foreach (var state in result.Value.MorphStates)
        {
            var weights = string.Join(",", state.Weights.Select(weight =>
                weight.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)));
            Console.WriteLine($"  Morph {state.EntityName}: count={state.Weights.Count} weights=[{weights}]");
        }
        Console.WriteLine($"UI: {result.Value.UiCanvasCount} canvases, {result.Value.UiElementCount} elements, {result.Value.InteractiveUiElementCount} interactive");
        foreach (var element in result.Value.UiElements)
        {
            var layout = element.Layout is null
                ? "layout=(unresolved)"
                : $"layout=({element.Layout.X:F1},{element.Layout.Y:F1},{element.Layout.Width:F1},{element.Layout.Height:F1}) canvas={element.Layout.CanvasEntityId}";
            Console.WriteLine($"  {element.EntityName}: kind={element.Kind} interactive={element.Interactive} text=\"{element.Text}\" {layout}");
        }
        if (result.Value.UiElementsTruncated)
        {
            Console.WriteLine("  UI element inspection truncated.");
        }
        Console.WriteLine($"Runtime entity states: {result.Value.EntityStates.Count}{(result.Value.EntityStatesTruncated ? "+ (truncated)" : string.Empty)}");
        foreach (var state in result.Value.EntityStates)
        {
            Console.WriteLine(
                $"  {state.EntityName}: position2D=({state.Transform.Position2D.X:F3},{state.Transform.Position2D.Y:F3}) delta2D=({state.PositionDelta2D.X:F3},{state.PositionDelta2D.Y:F3}) position3D=({state.Transform.Position3D.X:F3},{state.Transform.Position3D.Y:F3},{state.Transform.Position3D.Z:F3}) delta3D=({state.PositionDelta3D.X:F3},{state.PositionDelta3D.Y:F3},{state.PositionDelta3D.Z:F3}) rotation3D=({state.Transform.Rotation3D.X:F3},{state.Transform.Rotation3D.Y:F3},{state.Transform.Rotation3D.Z:F3}) components=[{string.Join(',', state.ComponentTypes)}]");
            if (state.Physics is { } physicsState)
            {
                Console.WriteLine(
                    $"    Physics {physicsState.Backend}: awake={physicsState.Awake} linear=({physicsState.LinearVelocity.X:F3},{physicsState.LinearVelocity.Y:F3},{physicsState.LinearVelocity.Z:F3}) speed={physicsState.LinearSpeed:F3} peak={physicsState.PeakLinearSpeed:F3}@{physicsState.PeakLinearSpeedFrame} angularDeg=({physicsState.AngularVelocityDegrees.X:F3},{physicsState.AngularVelocityDegrees.Y:F3},{physicsState.AngularVelocityDegrees.Z:F3}) angularSpeedDeg={physicsState.AngularSpeedDegrees:F3} peakAngularDeg={physicsState.PeakAngularSpeedDegrees:F3}@{physicsState.PeakAngularSpeedFrame} orientation=({physicsState.Orientation.X:F5},{physicsState.Orientation.Y:F5},{physicsState.Orientation.Z:F5},{physicsState.Orientation.W:F5})");
            }
        }
        Console.WriteLine($"Input actions: {result.Value.InputActionCount}");
        foreach (var action in result.Value.InputActions)
        {
            Console.WriteLine($"  {action.Name}: value={action.Value} down={action.IsDown} pressed={action.WasPressed} released={action.WasReleased}{FormatPhysicalInputSource(action)}");
        }

        Console.WriteLine($"Events: {result.Value.EventCount}");
        foreach (var runtimeEvent in result.Value.Events)
        {
            Console.WriteLine($"  {runtimeEvent.Type}: entity={runtimeEvent.EntityName} source={runtimeEvent.Source} handler={runtimeEvent.Handler ?? "none"}");
        }

        Console.WriteLine($"XR: {result.Value.XrRigCount} rigs, {result.Value.XrControllerCount} controllers, {result.Value.XrPoseCount} poses, {result.Value.XrActionCount} actions");
        foreach (var action in result.Value.XrActions)
        {
            Console.WriteLine($"  {action.Hand}/{action.Name}: value={action.Value} down={action.IsDown} pressed={action.WasPressed} released={action.WasReleased}");
        }

        Console.WriteLine($"Systems run: {string.Join(", ", result.Value.SystemsRun)}");
        Console.WriteLine($"Runtime assertions: {result.Value.AssertionResults.Count}; passed: {result.Value.AssertionsPassed}");
        foreach (var assertion in result.Value.AssertionResults)
        {
            Console.WriteLine($"  {(assertion.Passed ? "PASS" : "FAIL")} {assertion.Summary} actual={assertion.Actual?.ToJsonString() ?? "(missing)"}");
        }
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

    private static async Task<int> InspectRuntimeSoakAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string checkpointInterval,
        string minimumFramesPerSecond,
        string maximumRetainedMemoryGrowthBytes,
        string maximumEntityGrowth,
        string maximumObservations,
        string maximumEvents)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var request = new InspectRuntimeSoakRequest(
            root,
            scene,
            int.Parse(frames, culture),
            int.Parse(checkpointInterval, culture),
            double.Parse(minimumFramesPerSecond, culture),
            long.Parse(maximumRetainedMemoryGrowthBytes, culture),
            int.Parse(maximumEntityGrowth, culture),
            int.Parse(maximumObservations, culture),
            int.Parse(maximumEvents, culture));
        var result = await registry.ExecuteAsync<InspectRuntimeSoakRequest, InspectRuntimeSoakResult>(
            "rekall.runtime.inspect_soak",
            request,
            context);

        Console.WriteLine(result.Summary);
        Console.WriteLine($"Completed frames: {result.Value.CompletedFrames}/{result.Value.RequestedFrames}");
        Console.WriteLine($"Final frame: {result.Value.FinalFrameIndex}");
        Console.WriteLine($"Elapsed seconds: {result.Value.FinalElapsedSeconds:F9}");
        Console.WriteLine($"Wall milliseconds: {result.Value.WallMilliseconds:F3}");
        Console.WriteLine($"Frames per second: {result.Value.FramesPerSecond:F1}");
        Console.WriteLine($"Retained managed-memory growth: {result.Value.RetainedManagedMemoryGrowthBytes} bytes");
        Console.WriteLine($"Peak sampled managed memory: {result.Value.PeakSampledManagedMemoryBytes} bytes");
        Console.WriteLine($"Systems run: {string.Join(", ", result.Value.SystemsRun)}");
        Console.WriteLine($"Checkpoints: {result.Value.Checkpoints.Count}");
        foreach (var checkpoint in result.Value.Checkpoints)
        {
            Console.WriteLine(
                $"  Frame {checkpoint.FrameIndex}: completed={checkpoint.CompletedFrames} elapsed={checkpoint.ElapsedSeconds:F9}s wall={checkpoint.WallMilliseconds:F3}ms fps={checkpoint.FramesPerSecond:F1} entities={checkpoint.EntityCount} components={checkpoint.ComponentCount} observations={checkpoint.ObservationCount} events={checkpoint.EventCount} memory={checkpoint.SampledManagedMemoryBytes}");
        }

        foreach (var check in result.Value.Checks)
        {
            Console.WriteLine($"Check {check.Name}: {(check.Passed ? "PASS" : "FAIL")} - {check.Message}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> MultiplayerHostAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string durationSeconds)
    {
        var duration = double.Parse(durationSeconds, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<MultiplayerHostRequest, MultiplayerHostResult>(
            "rekall.multiplayer.host",
            new MultiplayerHostRequest(root, scene, duration),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Transport: {result.Value.Transport}");
        Console.WriteLine($"Pipe: {result.Value.PipeName}");
        Console.WriteLine($"Endpoint: {result.Value.Endpoint}");
        Console.WriteLine($"Session: {result.Value.SessionId}");
        Console.WriteLine($"Scene: {result.Value.SceneName}");
        Console.WriteLine($"Duration: {result.Value.DurationSeconds:F1}s");
        Console.WriteLine($"Network entities: {result.Value.NetworkEntityCount}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> MultiplayerStatusAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene)
    {
        var result = await registry.ExecuteAsync<MultiplayerStatusRequest, MultiplayerCommandResult>(
            "rekall.multiplayer.status",
            new MultiplayerStatusRequest(root, scene),
            context);
        PrintMultiplayerResult(result.Value);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> MultiplayerConnectAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string clientId,
        string? displayName)
    {
        var result = await registry.ExecuteAsync<MultiplayerConnectRequest, MultiplayerCommandResult>(
            "rekall.multiplayer.connect",
            new MultiplayerConnectRequest(root, scene, clientId, displayName),
            context);
        PrintMultiplayerResult(result.Value);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> MultiplayerDisconnectAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string clientId)
    {
        var result = await registry.ExecuteAsync<MultiplayerDisconnectRequest, MultiplayerCommandResult>(
            "rekall.multiplayer.disconnect",
            new MultiplayerDisconnectRequest(root, scene, clientId),
            context);
        PrintMultiplayerResult(result.Value);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> MultiplayerSubmitInputAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string clientId,
        string sequence,
        string networkId,
        string inputJson)
    {
        var parsedSequence = int.Parse(sequence, System.Globalization.CultureInfo.InvariantCulture);
        var input = await ParseRuntimeInputFrameAsync(inputJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<MultiplayerSubmitInputRequest, MultiplayerCommandResult>(
            "rekall.multiplayer.submit_input",
            new MultiplayerSubmitInputRequest(root, scene, clientId, parsedSequence, networkId, input),
            context);
        PrintMultiplayerResult(result.Value);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> MultiplayerTickAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string ticks)
    {
        var parsedTicks = int.Parse(ticks, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<MultiplayerTickRequest, MultiplayerCommandResult>(
            "rekall.multiplayer.tick",
            new MultiplayerTickRequest(root, scene, parsedTicks),
            context);
        PrintMultiplayerResult(result.Value);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> MultiplayerSnapshotAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene)
    {
        var result = await registry.ExecuteAsync<MultiplayerSnapshotRequest, MultiplayerCommandResult>(
            "rekall.multiplayer.snapshot",
            new MultiplayerSnapshotRequest(root, scene),
            context);
        PrintMultiplayerResult(result.Value);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> MultiplayerDeltaAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string fromServerTick)
    {
        var parsedFromServerTick = int.Parse(fromServerTick, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<MultiplayerDeltaRequest, MultiplayerCommandResult>(
            "rekall.multiplayer.delta",
            new MultiplayerDeltaRequest(root, scene, parsedFromServerTick),
            context);
        PrintMultiplayerResult(result.Value);
        return result.Ok ? 0 : 1;
    }

    private static void PrintMultiplayerResult(MultiplayerCommandResult result)
    {
        Console.WriteLine(result.Message);
        Console.WriteLine($"Transport: {result.Transport}");
        Console.WriteLine($"Pipe: {result.PipeName}");
        Console.WriteLine($"Endpoint: {result.Endpoint}");
        Console.WriteLine($"Session: {result.SessionId ?? "(none)"}");
        Console.WriteLine($"Operation: {result.Operation}");
        Console.WriteLine($"Connected: {result.Connected}");
        Console.WriteLine($"Applied: {result.Applied}");
        if (result.Reason is not null)
        {
            Console.WriteLine($"Accepted: {result.Accepted} ({result.Reason})");
        }

        Console.WriteLine($"Server tick: {result.ServerTick}");
        Console.WriteLine($"Server time: {result.ServerTimeSeconds:F3}s");
        Console.WriteLine($"Clients: {result.ClientCount}");
        Console.WriteLine($"Network entities: {result.EntityCount}");
        if (result.Snapshot is not null)
        {
            Console.WriteLine($"Snapshot entities: {result.Snapshot.Entities.Count}");
            Console.WriteLine($"Ack clients: {result.Snapshot.LastProcessedInputSequenceByClient.Count}");
        }

        if (result.Delta is not null)
        {
            Console.WriteLine($"Delta ticks: {result.Delta.FromServerTick}->{result.Delta.ToServerTick}");
            Console.WriteLine($"Delta changed: {result.Delta.ChangedEntities.Count}");
            Console.WriteLine($"Delta removed: {result.Delta.RemovedNetworkIds.Count}");
            Console.WriteLine($"Ack clients: {result.Delta.LastProcessedInputSequenceByClient.Count}");
        }
    }

    private static async Task<int> CaptureRuntimeViewportAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string outputDirectory,
        string width,
        string height,
        string backend,
        string? inputsJson = null,
        string? qualityPreset = null,
        string? qualityOverridesJson = null,
        string includeGpuTimings = "false")
    {
        var frameCount = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var viewportWidth = int.Parse(width, System.Globalization.CultureInfo.InvariantCulture);
        var viewportHeight = int.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        var inputs = await ParseRuntimeInputFramesAsync(inputsJson, context.CancellationToken);
        var result = await registry.ExecuteAsync<CaptureRuntimeViewportRequest, CaptureRuntimeViewportResult>(
            "rekall.render.capture_runtime_viewport",
            new CaptureRuntimeViewportRequest(
                root,
                scene,
                frameCount,
                outputDirectory,
                viewportWidth,
                viewportHeight,
                true,
                backend,
                Inputs: inputs)
            {
                QualityPreset = qualityPreset,
                QualityOverrides = ParseQualityOverrides(qualityOverridesJson),
                IncludeGpuTimings = bool.Parse(includeGpuTimings)
            },
            context);

        Console.WriteLine($"Runtime viewport {scene} frame {result.Value.FrameIndex}: {result.Value.Width}x{result.Value.Height}");
        Console.WriteLine(result.Value.ScreenshotPath);
        Console.WriteLine($"Backend: {result.Value.BackendId}");
        Console.WriteLine($"Hardware accelerated: {result.Value.HardwareAccelerated}");
        Console.WriteLine($"Acceleration: {result.Value.AccelerationStatus}");
        Console.WriteLine($"Selected device: {result.Value.SelectedDeviceName ?? "(none)"}");
        PrintQualityReport(result.Value.QualityPlan, result.Value.GpuTimings, result.Value.ResourceBytes);
        Console.WriteLine($"Workload: draws={result.Value.DrawCount}; dispatches={result.Value.DispatchCount}");
        foreach (var command in result.Value.SuggestedCommands)
        {
            Console.WriteLine($"Next: {command}");
        }
        Console.WriteLine(
            $"Input actions: {(result.Value.InputActions.Count == 0 ? "(none)" : string.Join(", ", result.Value.InputActions.Select(action => $"{action.Name}={action.Value:G}")))}");
        Console.WriteLine($"Elapsed simulation: {result.Value.ElapsedSeconds:F3}s");
        Console.WriteLine($"Active camera: {result.Value.ActiveCamera ?? "(none)"}");
        if (result.Value.LayoutDiagnostics.ActiveCamera is { } camera)
        {
            Console.WriteLine(
                $"Camera pose: position=({camera.X:F3}, {camera.Y:F3}, {camera.Z:F3}); rotation=({camera.RotationX:F2}, {camera.RotationY:F2}, {camera.RotationZ:F2}); fov={camera.FieldOfViewDegrees:F1}");
        }

        Console.WriteLine(
            $"Frame analysis: informative={result.Value.FrameAnalysis.VisuallyInformative}, analyzed={result.Value.FrameAnalysis.Analyzed}, distinctColors={result.Value.FrameAnalysis.DistinctColorCount}");
        Console.WriteLine(
            $"Dominant color: {result.Value.FrameAnalysis.DominantColorRatio:P1}, luminance={result.Value.FrameAnalysis.AverageLuminance:F3}, luminanceStdDev={result.Value.FrameAnalysis.LuminanceStandardDeviation:F3}");
        foreach (var code in result.Value.FrameAnalysis.WarningCodes)
        {
            Console.WriteLine($"Frame warning: {code}");
        }

        Console.WriteLine($"Renderable: {result.Value.RenderableCount}");
        Console.WriteLine($"Renderable kinds: {string.Join(", ", result.Value.RenderableKinds)}");
        Console.WriteLine(
            $"Layout bounds: spatial={result.Value.LayoutDiagnostics.WorldBounds.SpatialRenderableCount}, x={result.Value.LayoutDiagnostics.WorldBounds.SpanX:F2}, y={result.Value.LayoutDiagnostics.WorldBounds.SpanY:F2}, z={result.Value.LayoutDiagnostics.WorldBounds.SpanZ:F2}");
        foreach (var code in result.Value.LayoutDiagnostics.WarningCodes)
        {
            Console.WriteLine($"Layout warning: {code}");
        }

        foreach (var hint in result.Value.LayoutDiagnostics.AuthoringHints)
        {
            Console.WriteLine($"Layout hint: {hint}");
        }

        Console.WriteLine($"Culled renderables: {result.Value.CulledRenderableCount}");
        foreach (var renderable in result.Value.CulledRenderables)
        {
            Console.WriteLine($"Culled: {renderable.EntityName}; layer: {renderable.Layer}; reason: {renderable.Reason}; camera: {renderable.CameraEntityName ?? "none"}; mask: {renderable.CullingMask}");
        }

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

    private static async Task<int> CompareQualityPresetsAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string frames,
        string outputDirectory,
        string width,
        string height,
        string backend,
        string presets,
        string? overridesJson,
        string includeGpuTimings)
    {
        var request = new CompareQualityPresetsRequest(
            root,
            scene,
            presets.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            int.Parse(frames, CultureInfo.InvariantCulture),
            outputDirectory,
            int.Parse(width, CultureInfo.InvariantCulture),
            int.Parse(height, CultureInfo.InvariantCulture),
            backend,
            ParseQualityOverrides(overridesJson),
            bool.Parse(includeGpuTimings));
        var result = await registry.ExecuteAsync<CompareQualityPresetsRequest, CompareQualityPresetsResult>(
            "rekall.render.compare_quality_presets",
            request,
            context);
        Console.WriteLine($"Compared quality presets for {scene} at frame {result.Value.FrameIndex}");
        foreach (var capture in result.Value.Captures)
        {
            Console.WriteLine($"Preset: {capture.RequestedPreset} -> {capture.ResolvedPreset}");
            Console.WriteLine($"  Output: {capture.OutputWidth}x{capture.OutputHeight}; Internal: {capture.RenderWidth}x{capture.RenderHeight}");
            Console.WriteLine($"  Capture: {capture.ScreenshotPath}; nonblank={capture.NonBlank}");
            Console.WriteLine($"  Resources: {capture.ResourceBytes} bytes; draws={capture.DrawCount}; dispatches={capture.DispatchCount}");
            if (capture.GpuTimings.Available)
            {
                Console.WriteLine($"  GPU frame: {capture.GpuTimings.TotalMilliseconds!.Value:F6} ms");
                foreach (var pass in capture.GpuTimings.Passes)
                {
                    Console.WriteLine($"  GPU pass: {pass.Name}; {pass.Nanoseconds:F3} ns; {pass.Milliseconds:F6} ms");
                }
            }
            else
            {
                Console.WriteLine($"  GPU timings: {capture.GpuTimings.Code}");
            }

            foreach (var degradation in capture.Degradations)
            {
                Console.WriteLine($"  Degradation: {degradation.Code}; feature={degradation.Feature}; Requested={degradation.RequestedValue}; resolved={degradation.ResolvedValue}");
            }
        }

        foreach (var command in result.Value.NextCommands)
        {
            Console.WriteLine($"Next: {command}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Captures.All(capture => capture.NonBlank) ? 0 : 1;
    }

    private static RekallAgeRenderQualityOverrides? ParseQualityOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<RekallAgeRenderQualityOverrides>(
            json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static void PrintQualityReport(
        RekallAgeResolvedRenderFeaturePlan? quality,
        RekallAgeGpuFrameTimingReport timings,
        long resourceBytes)
    {
        if (quality is null)
        {
            return;
        }

        Console.WriteLine($"Requested quality: {quality.RequestedPreset}");
        Console.WriteLine($"Resolved quality: {quality.ResolvedPreset}");
        Console.WriteLine($"Internal resolution: {quality.RenderWidth}x{quality.RenderHeight}");
        Console.WriteLine($"Render resources: {resourceBytes} bytes (transient={quality.EstimatedTransientBytes}; persistent={quality.EstimatedPersistentBytes})");
        if (timings.Available)
        {
            Console.WriteLine($"GPU frame: {timings.TotalMilliseconds!.Value:F6} ms");
            foreach (var pass in timings.Passes)
            {
                Console.WriteLine($"GPU pass: {pass.Name}; {pass.Nanoseconds:F3} ns; {pass.Milliseconds:F6} ms");
            }
        }
        else
        {
            Console.WriteLine($"GPU timings: {timings.Code}");
        }

        foreach (var degradation in quality.Degradations)
        {
            Console.WriteLine($"Degradation: {degradation.Code}; feature={degradation.Feature}; Requested={degradation.RequestedValue}; resolved={degradation.ResolvedValue}");
        }
    }

    private static async Task<int> ExportSceneGlbAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string outputPath,
        string frames)
    {
        var frameCount = int.Parse(frames, System.Globalization.CultureInfo.InvariantCulture);
        var result = await registry.ExecuteAsync<ExportSceneGlbRequest, ExportSceneGlbResult>(
            "rekall.render.export_scene_glb",
            new ExportSceneGlbRequest(root, scene, outputPath, frameCount),
            context);

        Console.WriteLine(result.Summary);
        Console.WriteLine($"Output: {result.Value.OutputPath}");
        Console.WriteLine($"Frame: {result.Value.FrameIndex}");
        Console.WriteLine($"Nodes: {result.Value.NodeCount}");
        Console.WriteLine($"Meshes: {result.Value.MeshCount}");
        Console.WriteLine($"Materials: {result.Value.MaterialCount}");
        Console.WriteLine($"Images: {result.Value.ImageCount}");
        Console.WriteLine($"Bytes: {result.Value.BytesWritten}");
        foreach (var warning in result.Value.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
        }

        return result.Ok && result.Value.Exported ? 0 : 1;
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

    private static async ValueTask<IReadOnlyList<RekallAgeRuntimeInputFrame>?> ParseRuntimeInputFramesAsync(
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
            return System.Text.Json.JsonSerializer.Deserialize<RekallAgeRuntimeInputFrame[]>(
                payload,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ArgumentException($"Runtime input frames JSON is invalid: {ex.Message}", ex);
        }
    }

    private static string FormatPhysicalInputSource(RekallAgeRuntimeInputAction action)
    {
        return string.IsNullOrWhiteSpace(action.PhysicalDeviceId)
            ? string.Empty
            : $" device={action.PhysicalDeviceId} kind={action.PhysicalDeviceKind ?? "unknown"}";
    }

    private static async ValueTask<RekallAgeRuntimeInputFrame> ParseRuntimeInputFrameAsync(
        string inputJson,
        CancellationToken cancellationToken)
    {
        var payload = File.Exists(inputJson)
            ? await File.ReadAllTextAsync(inputJson, cancellationToken)
            : inputJson;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<RekallAgeRuntimeInputFrame>(
                payload,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new RekallAgeRuntimeInputFrame();
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ArgumentException($"Runtime input frame JSON is invalid: {ex.Message}", ex);
        }
    }

    private static async ValueTask<IReadOnlyList<InspectSceneRuntimeAssertion>?> ParseRuntimeAssertionsAsync(
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
            return System.Text.Json.JsonSerializer.Deserialize<InspectSceneRuntimeAssertion[]>(
                payload,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ArgumentException($"Runtime assertions JSON is invalid: {ex.Message}", ex);
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
