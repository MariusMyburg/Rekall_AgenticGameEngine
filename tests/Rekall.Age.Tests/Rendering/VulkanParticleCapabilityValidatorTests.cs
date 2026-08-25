using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanParticleCapabilityValidatorTests
{
    [Fact]
    public void ExactComputeAndStorageDescriptorBoundariesAreSupported()
    {
        var diagnostics = RekallAgeVulkanParticleCapabilityValidator.Validate(new(
            MaxComputeWorkGroupInvocations: 256,
            MaxComputeWorkGroupSizeX: 256,
            MaxPerStageDescriptorStorageBuffers: 5,
            MaxDescriptorSetStorageBuffers: 5));

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(255, 256, 5, 5, "REKALL_PARTICLE_COMPUTE_INVOCATIONS_UNSUPPORTED")]
    [InlineData(256, 255, 5, 5, "REKALL_PARTICLE_COMPUTE_WORKGROUP_SIZE_UNSUPPORTED")]
    [InlineData(256, 256, 4, 5, "REKALL_PARTICLE_STORAGE_DESCRIPTORS_PER_STAGE_UNSUPPORTED")]
    [InlineData(256, 256, 5, 4, "REKALL_PARTICLE_STORAGE_DESCRIPTORS_PER_SET_UNSUPPORTED")]
    public void InsufficientFixedComputeRequirementsDegradeBeforeAllocation(
        uint invocations,
        uint sizeX,
        uint perStageStorage,
        uint perSetStorage,
        string expectedCode)
    {
        var diagnostic = Assert.Single(RekallAgeVulkanParticleCapabilityValidator.Validate(new(
            invocations,
            sizeX,
            perStageStorage,
            perSetStorage)));

        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Contains("requires", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingFragmentStoresAndAtomicsDegradesGpuOverdrawBeforeAllocation()
    {
        var diagnostic = Assert.Single(RekallAgeVulkanParticleCapabilityValidator.Validate(new(
            256,
            256,
            5,
            5,
            FragmentStoresAndAtomics: false)));

        Assert.Equal("REKALL_PARTICLE_FRAGMENT_ATOMICS_UNSUPPORTED", diagnostic.Code);
    }
}
