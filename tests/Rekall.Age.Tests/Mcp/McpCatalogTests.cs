using Rekall.Age.Core.Commands;
using Rekall.Age.Build.Commands;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.Rendering;
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
        registry.Register(new BuildModulesCommand());
        registry.Register(new SubmitClearVulkanRenderPassCommand(new FakeVulkanRenderPassSubmission()));
        registry.Register(new ReadClearVulkanRenderPassCommand(new FakeVulkanRenderPassReadback()));
        registry.Register(new CaptureClearVulkanRenderPassCommand(new FakeVulkanRenderPassCapture()));

        var catalog = RekallAgeMcpCatalog.FromRegistry(registry);

        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.project.create");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.create_game_from_template");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.run.scene");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.capture.screenshot");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.component_schemas");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.module.scaffold");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.build.modules");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.render.vulkan.render_pass.submit_clear");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.render.vulkan.render_pass.read_clear");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.render.vulkan.render_pass.capture_clear");
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
