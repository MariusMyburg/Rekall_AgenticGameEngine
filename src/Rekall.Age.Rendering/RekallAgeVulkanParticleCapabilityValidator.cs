namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanParticleDeviceLimits(
    uint MaxComputeWorkGroupInvocations,
    uint MaxComputeWorkGroupSizeX,
    uint MaxPerStageDescriptorStorageBuffers,
    uint MaxDescriptorSetStorageBuffers,
    bool FragmentStoresAndAtomics = true);

public sealed record RekallAgeVulkanParticleCapabilityDiagnostic(string Code, string Message);

public static class RekallAgeVulkanParticleCapabilityValidator
{
    public const uint RequiredComputeLocalSizeX = 256;
    public const uint RequiredStorageBuffersPerStage = 5;
    public const uint RequiredStorageBuffersPerDescriptorSet = 5;

    public static IReadOnlyList<RekallAgeVulkanParticleCapabilityDiagnostic> Validate(
        RekallAgeVulkanParticleDeviceLimits limits)
    {
        var diagnostics = new List<RekallAgeVulkanParticleCapabilityDiagnostic>();
        if (limits.MaxComputeWorkGroupInvocations < RequiredComputeLocalSizeX)
        {
            diagnostics.Add(new(
                "REKALL_PARTICLE_COMPUTE_INVOCATIONS_UNSUPPORTED",
                $"Particle compute requires {RequiredComputeLocalSizeX} work-group invocations; device limit is {limits.MaxComputeWorkGroupInvocations}."));
        }
        if (limits.MaxComputeWorkGroupSizeX < RequiredComputeLocalSizeX)
        {
            diagnostics.Add(new(
                "REKALL_PARTICLE_COMPUTE_WORKGROUP_SIZE_UNSUPPORTED",
                $"Particle compute requires local_size_x {RequiredComputeLocalSizeX}; device limit is {limits.MaxComputeWorkGroupSizeX}."));
        }
        if (limits.MaxPerStageDescriptorStorageBuffers < RequiredStorageBuffersPerStage)
        {
            diagnostics.Add(new(
                "REKALL_PARTICLE_STORAGE_DESCRIPTORS_PER_STAGE_UNSUPPORTED",
                $"Particle compute requires {RequiredStorageBuffersPerStage} storage-buffer descriptors per stage; device limit is {limits.MaxPerStageDescriptorStorageBuffers}."));
        }
        if (limits.MaxDescriptorSetStorageBuffers < RequiredStorageBuffersPerDescriptorSet)
        {
            diagnostics.Add(new(
                "REKALL_PARTICLE_STORAGE_DESCRIPTORS_PER_SET_UNSUPPORTED",
                $"Particle compute requires {RequiredStorageBuffersPerDescriptorSet} storage-buffer descriptors per set; device limit is {limits.MaxDescriptorSetStorageBuffers}."));
        }
        if (!limits.FragmentStoresAndAtomics)
        {
            diagnostics.Add(new(
                "REKALL_PARTICLE_FRAGMENT_ATOMICS_UNSUPPORTED",
                "Particle overdraw evidence requires fragment-stage storage writes and atomics; the device feature is unavailable."));
        }
        return diagnostics;
    }
}
