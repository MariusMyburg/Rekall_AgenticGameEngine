using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering.WebGpu;

internal sealed class RekallAgeWebGpuCommandEncoder(
    RekallAgeWebGpuRenderingDevice device,
    IRekallAgeGraphicsCommandEncoder? conformance,
    string? label,
    RekallAgeGraphicsValidationResult? initialValidation = null) : IRekallAgeGraphicsCommandEncoder
{
    public RekallAgeGraphicsValidationResult BeginRenderPass(RekallAgeRenderPassDescriptor descriptor) => device.ValidateCommandLabel(descriptor.Label) is { Valid: false } invalid ? invalid : Execute(() => conformance!.BeginRenderPass(descriptor));
    public RekallAgeGraphicsValidationResult SetRenderPipeline(RekallAgeGraphicsResourceHandle pipeline) => Execute(() => conformance!.SetRenderPipeline(pipeline));
    public RekallAgeGraphicsValidationResult SetComputePipeline(RekallAgeGraphicsResourceHandle pipeline) => Execute(() => conformance!.SetComputePipeline(pipeline));
    public RekallAgeGraphicsValidationResult SetBindingSet(int index, RekallAgeGraphicsResourceHandle bindingSet) => Execute(() => conformance!.SetBindingSet(index, bindingSet));
    public RekallAgeGraphicsValidationResult SetVertexBuffer(int slot, RekallAgeGraphicsResourceHandle buffer, ulong offset = 0, ulong sizeBytes = 0) => Execute(() => conformance!.SetVertexBuffer(slot, buffer, offset, sizeBytes));
    public RekallAgeGraphicsValidationResult SetIndexBuffer(RekallAgeGraphicsResourceHandle buffer, RekallAgeIndexFormat format, ulong offset = 0, ulong sizeBytes = 0) => Execute(() => conformance!.SetIndexBuffer(buffer, format, offset, sizeBytes));
    public RekallAgeGraphicsValidationResult Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => Execute(() => conformance!.Draw(vertexCount, instanceCount, firstVertex, firstInstance));
    public RekallAgeGraphicsValidationResult DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => Execute(() => conformance!.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance));
    public RekallAgeGraphicsValidationResult DrawIndirect(RekallAgeGraphicsResourceHandle buffer, ulong offset, uint drawCount = 1, uint strideBytes = 16) => Execute(() => conformance!.DrawIndirect(buffer, offset, drawCount, strideBytes));
    public RekallAgeGraphicsValidationResult DrawIndexedIndirect(RekallAgeGraphicsResourceHandle buffer, ulong offset, uint drawCount = 1, uint strideBytes = 20) => Execute(() => conformance!.DrawIndexedIndirect(buffer, offset, drawCount, strideBytes));
    public RekallAgeGraphicsValidationResult EndRenderPass() => Execute(conformance!.EndRenderPass);
    public RekallAgeGraphicsValidationResult BeginComputePass(string? passLabel = null) => device.ValidateCommandLabel(passLabel) is { Valid: false } invalid ? invalid : Execute(() => conformance!.BeginComputePass(passLabel));
    public RekallAgeGraphicsValidationResult Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1) => Execute(() => conformance!.Dispatch(groupCountX, groupCountY, groupCountZ));
    public RekallAgeGraphicsValidationResult DispatchIndirect(RekallAgeGraphicsResourceHandle buffer, ulong offset) => Execute(() => conformance!.DispatchIndirect(buffer, offset));
    public RekallAgeGraphicsValidationResult EndComputePass() => Execute(conformance!.EndComputePass);
    public RekallAgeGraphicsValidationResult CopyBuffer(RekallAgeGraphicsResourceHandle source, ulong sourceOffset, RekallAgeGraphicsResourceHandle destination, ulong destinationOffset, ulong sizeBytes) => Execute(() => conformance!.CopyBuffer(source, sourceOffset, destination, destinationOffset, sizeBytes));
    public RekallAgeGraphicsCommandBuffer Finish() => device.EncoderAvailable().Valid && conformance is not null ? conformance.Finish() : new(device.DeviceId, label, [], false);
    public void Dispose() => conformance?.Dispose();
    private RekallAgeGraphicsValidationResult Execute(Func<RekallAgeGraphicsValidationResult> action)
    {
        var availability = initialValidation ?? device.EncoderAvailable();
        return !availability.Valid || conformance is null ? availability : action();
    }
}
