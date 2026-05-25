using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Build.Commands;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Mcp;

public sealed class McpCatalogTests
{
    [Fact]
    public void CatalogExposesRegisteredCommandSchemasAsTools()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        registry.Register(new ListTransactionHistoryCommand());
        registry.Register(new RestoreTransactionPreimageCommand());
        registry.Register(new CreateProjectCommand());
        registry.Register(new InspectGameTemplateCommand());
        registry.Register(new VerifyMvpTemplatesCommand());
        registry.Register(new CreateGameFromTemplateCommand());
        registry.Register(new CreatePlayableGameFromTemplateCommand());
        registry.Register(new CreatePlayablePackageFromTemplateCommand());
        registry.Register(new VerifyPlayableGameCommand());
        registry.Register(new PackagePlayableGameCommand());
        registry.Register(new InspectPlayablePackageCommand());
        registry.Register(new RunPlayablePackageCommand());
        registry.Register(new CapturePlayablePackageFrameCommand());
        registry.Register(new AuditPlayablePackageCommand());
        registry.Register(new PlaytestSceneCommand());
        registry.Register(new RunSceneCommand());
        registry.Register(new CaptureScreenshotCommand());
        registry.Register(new CaptureRuntimeViewportCommand());
        registry.Register(new CapturePlayableFrameCommand());
        registry.Register(new ListComponentSchemasCommand(GetType().Assembly));
        registry.Register(new ListModuleSourcesCommand());
        registry.Register(new ReadModuleSourceCommand());
        registry.Register(new ScaffoldModuleCommand());
        registry.Register(new ScaffoldRuntimeSystemModuleCommand());
        registry.Register(new WriteModuleSourceCommand());
        registry.Register(new BuildModulesCommand());
        registry.Register(new SubmitClearVulkanRenderPassCommand(new FakeVulkanRenderPassSubmission()));
        registry.Register(new ReadClearVulkanRenderPassCommand(new FakeVulkanRenderPassReadback()));
        registry.Register(new CaptureClearVulkanRenderPassCommand(new FakeVulkanRenderPassCapture()));
        registry.Register(new ListShaderSourcesCommand());
        registry.Register(new ReadShaderSourceCommand());
        registry.Register(new WriteShaderSourceCommand());
        registry.Register(new ValidateShaderSourceCommand());
        registry.Register(new AssignShaderPipelineCommand());
        registry.Register(new ApplySceneBlueprintCommand());
        registry.Register(new DeleteEntityCommand());
        registry.Register(new ImportKsaSolarSystemCommand());
        registry.Register(new LivePlayerStatusCommand());
        registry.Register(new LivePlayerReloadSceneCommand());
        registry.Register(new LivePlayerReloadAssetsCommand());
        registry.Register(new LivePlayerApplySceneBlueprintCommand());
        registry.Register(new LivePlayerApplySceneDiffCommand());

        var catalog = RekallAgeMcpCatalog.FromRegistry(registry);

        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.context.engine_status");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.transaction.history");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.transaction.restore_preimage");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.project.create");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.templates.inspect");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.templates.verify_mvp");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.create_game_from_template");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.create_playable_game_from_template");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.create_playable_package_from_template");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.verify_playable_game");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.package_playable_game");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.inspect_playable_package");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.run_playable_package");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.capture_playable_package_frame");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.audit_playable_package");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.playtest.scene");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.run.scene");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.capture.screenshot");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.render.capture_runtime_viewport");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.play.capture_frame");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.component_schemas");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.list_sources");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.read_source");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.scaffold");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.scaffold_runtime_system");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.write_source");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.build.modules");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.render.vulkan.render_pass.submit_clear");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.render.vulkan.render_pass.read_clear");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.render.vulkan.render_pass.capture_clear");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.shader.list");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.shader.read");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.shader.write");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.shader.validate");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.shader.assign_pipeline");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.scene.apply_blueprint");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.entity.delete");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.solar.import_ksa_system");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.live.status");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.live.reload_scene");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.live.reload_assets");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.live.apply_scene_blueprint");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.live.apply_scene_diff");

        var oneShot = catalog.Tools.Single(tool => tool.Name == "rekall.workflow.create_playable_package_from_template");
        Assert.Equal("workflow", oneShot.Category);
        Assert.True(oneShot.Recommended);
        Assert.Equal(10, oneShot.AgentPriority);

        var audit = catalog.Tools.Single(tool => tool.Name == "rekall.workflow.audit_playable_package");
        Assert.Equal("workflow", audit.Category);
        Assert.True(audit.Recommended);
        Assert.True(audit.AgentPriority > oneShot.AgentPriority);

        var renderTool = catalog.Tools.Single(tool => tool.Name == "rekall.render.vulkan.render_pass.capture_clear");
        Assert.Equal("rendering", renderTool.Category);
        Assert.False(renderTool.Recommended);
        Assert.True(renderTool.AgentPriority > oneShot.AgentPriority);
        var shaderTool = catalog.Tools.Single(tool => tool.Name == "rekall.shader.write");
        Assert.Equal("shaders", shaderTool.Category);
        Assert.True(shaderTool.AgentPriority < renderTool.AgentPriority);
        var transactionTool = catalog.Tools.Single(tool => tool.Name == "rekall.transaction.history");
        Assert.Equal("transactions", transactionTool.Category);
        Assert.True(transactionTool.AgentPriority < renderTool.AgentPriority);
        var blueprintTool = catalog.Tools.Single(tool => tool.Name == "rekall.scene.apply_blueprint");
        Assert.Equal("world", blueprintTool.Category);
        var deleteTool = catalog.Tools.Single(tool => tool.Name == "rekall.entity.delete");
        Assert.Equal("world", deleteTool.Category);
        var solarTool = catalog.Tools.Single(tool => tool.Name == "rekall.solar.import_ksa_system");
        Assert.Equal("world", solarTool.Category);
        Assert.True(solarTool.Recommended);
        var liveTool = catalog.Tools.Single(tool => tool.Name == "rekall.live.status");
        Assert.Equal("live", liveTool.Category);
        Assert.True(liveTool.Recommended);
        Assert.Equal("rekall.context.engine_status", catalog.Tools[0].Name);
        Assert.True(catalog.Tools.Index().All(item => item.Index == 0 || catalog.Tools[item.Index - 1].AgentPriority <= item.Item.AgentPriority));
    }

    private sealed class FakeVulkanRenderPassSubmission : IRekallAgeVulkanRenderPassSubmission
    {
        public ValueTask<RekallAgeVulkanRenderPassSubmissionResult> SubmitClearRenderPassAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            RekallAgeVulkanClearColor clearColor,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new RekallAgeVulkanRenderPassSubmissionResult(
                Submitted: true,
                LoaderName: "fake-vulkan",
                SelectedDevice: new RekallAgeVulkanSelectedDevice(
                    "Fake RTX",
                    "discrete-gpu",
                    "1.4.0",
                    new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)),
                Width: width,
                Height: height,
                Format: format,
                ImageCreated: true,
                ImageViewCreated: true,
                RenderPassCreated: true,
                FramebufferCreated: true,
                CommandPoolCreated: true,
                CommandBufferAllocated: true,
                RenderPassBegan: true,
                RenderPassEnded: true,
                FenceSignaled: true,
                ClearColor: clearColor,
                Errors: []));
        }
    }

    private sealed class FakeVulkanRenderPassReadback : IRekallAgeVulkanRenderPassReadback
    {
        public ValueTask<RekallAgeVulkanRenderPassReadbackResult> ReadClearRenderPassAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            RekallAgeVulkanClearColor clearColor,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new RekallAgeVulkanRenderPassReadbackResult(
                Readback: true,
                LoaderName: "fake-vulkan",
                SelectedDevice: new RekallAgeVulkanSelectedDevice(
                    "Fake RTX",
                    "discrete-gpu",
                    "1.4.0",
                    new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)),
                Width: width,
                Height: height,
                Format: format,
                ClearColor: clearColor,
                Submitted: true,
                BufferCreated: true,
                BufferBound: true,
                BufferMapped: true,
                BytesRead: 4,
                NonZeroBytes: 4,
                FirstPixel: new RekallAgeVulkanReadbackPixel(20, 26, 36, 255),
                ByteChecksum: 337,
                Errors: []));
        }
    }

    private sealed class FakeVulkanRenderPassCapture : IRekallAgeVulkanRenderPassCapture
    {
        public ValueTask<RekallAgeVulkanRenderPassCaptureResult> CaptureClearRenderPassAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            string outputDirectory,
            RekallAgeVulkanClearColor clearColor,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new RekallAgeVulkanRenderPassCaptureResult(
                Captured: true,
                OutputPath: Path.Combine(outputDirectory, "vulkan-clear.png"),
                LoaderName: "fake-vulkan",
                SelectedDevice: new RekallAgeVulkanSelectedDevice(
                    "Fake RTX",
                    "discrete-gpu",
                    "1.4.0",
                    new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)),
                Width: width,
                Height: height,
                Format: format,
                ClearColor: clearColor,
                BytesRead: 4,
                NonZeroBytes: 4,
                FirstPixel: new RekallAgeVulkanReadbackPixel(20, 25, 36, 255),
                ByteChecksum: 336,
                Errors: []));
        }
    }
}
