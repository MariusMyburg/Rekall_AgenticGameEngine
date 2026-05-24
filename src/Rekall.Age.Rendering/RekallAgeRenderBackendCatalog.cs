namespace Rekall.Age.Rendering;

public sealed record RekallAgeRenderBackendCatalog(IReadOnlyList<RekallAgeRenderBackendDescriptor> Backends)
{
    public static RekallAgeRenderBackendCatalog CreateDefault()
    {
        return new RekallAgeRenderBackendCatalog(
        [
            new RekallAgeRenderBackendDescriptor(
                "vulkan",
                "Vulkan",
                "preferred",
                [
                    "instance",
                    "physical-device-selection",
                    "logical-device",
                    "queues",
                    "swapchain",
                    "render-passes",
                    "pipeline-layouts",
                    "graphics-pipelines",
                    "descriptor-sets",
                    "buffers",
                    "images",
                    "shaders",
                    "command-pools",
                    "command-buffers",
                    "render-pass-submit-clear",
                    "render-pass-read-clear",
                    "synchronization"
                ]),
            new RekallAgeRenderBackendDescriptor(
                "direct3d12",
                "Direct3D 12",
                "planned",
                [
                    "adapter-selection",
                    "device",
                    "command-queues",
                    "swapchain",
                    "root-signatures",
                    "pipeline-state-objects",
                    "descriptor-heaps",
                    "resources",
                    "command-lists",
                    "fences"
                ]),
            new RekallAgeRenderBackendDescriptor(
                "software",
                "Rekall Software",
                "available",
                [
                    "headless-raster",
                    "deterministic-preview",
                    "pixel-buffers",
                    "png-output"
                ])
        ]);
    }
}

public sealed record RekallAgeRenderBackendDescriptor(
    string Id,
    string DisplayName,
    string Status,
    IReadOnlyList<string> AgentExposedCapabilities);
