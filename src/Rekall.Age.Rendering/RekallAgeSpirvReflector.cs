using Vortice.SPIRV.Reflect;

namespace Rekall.Age.Rendering;

internal sealed record RekallAgeSpirvReflection(
    IReadOnlyList<RekallAgeShaderVertexElement> VertexElements,
    IReadOnlyList<RekallAgeShaderResourceElement> Resources);

internal static unsafe class RekallAgeSpirvReflector
{
    private const uint MaximumReflectedElements = 256;

    public static RekallAgeSpirvReflection Reflect(byte[] vertexSpirv, byte[] fragmentSpirv)
    {
        var vertex = ReflectModule(vertexSpirv, includeVertexInputs: true, "Vertex");
        var fragment = ReflectModule(fragmentSpirv, includeVertexInputs: false, "Fragment");
        var resources = vertex.Resources
            .Concat(fragment.Resources)
            .GroupBy(resource => (resource.Set, resource.Binding, resource.Name, resource.Kind))
            .Select(group => group.Aggregate((left, right) => left with
            {
                Stages = string.Join('|', new[] { left.Stages, right.Stages }
                    .SelectMany(value => value.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal))
            }))
            .OrderBy(resource => resource.Set)
            .ThenBy(resource => resource.Binding)
            .ToArray();
        return new RekallAgeSpirvReflection(vertex.VertexElements, resources);
    }

    private static RekallAgeSpirvReflection ReflectModule(
        byte[] spirv,
        bool includeVertexInputs,
        string stage)
    {
        SpvReflectShaderModule module = default;
        fixed (byte* code = spirv)
        {
            EnsureSuccess(
                SPIRVReflectApi.spvReflectCreateShaderModule((nuint)spirv.Length, code, &module),
                "create shader module");
        }

        try
        {
            var vertexElements = includeVertexInputs ? ReadVertexInputs(&module) : [];
            var resources = ReadResources(&module, stage);
            return new RekallAgeSpirvReflection(vertexElements, resources);
        }
        finally
        {
            SPIRVReflectApi.spvReflectDestroyShaderModule(&module);
        }
    }

    private static IReadOnlyList<RekallAgeShaderVertexElement> ReadVertexInputs(
        SpvReflectShaderModule* module)
    {
        uint count = 0;
        EnsureSuccess(
            SPIRVReflectApi.spvReflectEnumerateInputVariables(module, &count, null),
            "count vertex inputs");
        EnsureReasonableCount(count, "vertex inputs");
        if (count == 0)
        {
            return [];
        }

        var pointers = stackalloc SpvReflectInterfaceVariable*[(int)count];
        EnsureSuccess(
            SPIRVReflectApi.spvReflectEnumerateInputVariables(module, &count, pointers),
            "enumerate vertex inputs");
        var result = new List<RekallAgeShaderVertexElement>((int)count);
        for (var index = 0; index < count; index++)
        {
            var input = pointers[index];
            if (input is null || (input->decoration_flags & SpvReflectDecorationFlags.BuiltIn) != 0)
            {
                continue;
            }

            result.Add(new RekallAgeShaderVertexElement(
                checked((int)input->location),
                input->Name ?? string.Empty,
                MapFormat(input->format)));
        }

        return result.OrderBy(element => element.Location).ToArray();
    }

    private static IReadOnlyList<RekallAgeShaderResourceElement> ReadResources(
        SpvReflectShaderModule* module,
        string stage)
    {
        uint count = 0;
        EnsureSuccess(
            SPIRVReflectApi.spvReflectEnumerateDescriptorBindings(module, &count, null),
            "count descriptor bindings");
        EnsureReasonableCount(count, "descriptor bindings");
        if (count == 0)
        {
            return [];
        }

        var pointers = stackalloc SpvReflectDescriptorBinding*[(int)count];
        EnsureSuccess(
            SPIRVReflectApi.spvReflectEnumerateDescriptorBindings(module, &count, pointers),
            "enumerate descriptor bindings");
        var result = new List<RekallAgeShaderResourceElement>((int)count);
        for (var index = 0; index < count; index++)
        {
            var binding = pointers[index];
            if (binding is null)
            {
                continue;
            }

            result.Add(new RekallAgeShaderResourceElement(
                checked((int)binding->set),
                checked((int)binding->binding),
                binding->Name ?? string.Empty,
                binding->descriptor_type.ToString(),
                stage));
        }

        return result;
    }

    private static string MapFormat(SpvReflectFormat format) => format switch
    {
        SpvReflectFormat.R32Sfloat => "Float1",
        SpvReflectFormat.R32g32Sfloat => "Float2",
        SpvReflectFormat.R32g32b32Sfloat => "Float3",
        SpvReflectFormat.R32g32b32a32Sfloat => "Float4",
        SpvReflectFormat.R32Uint => "UInt1",
        SpvReflectFormat.R32g32Uint => "UInt2",
        SpvReflectFormat.R32g32b32Uint => "UInt3",
        SpvReflectFormat.R32g32b32a32Uint => "UInt4",
        SpvReflectFormat.R32Sint => "Int1",
        SpvReflectFormat.R32g32Sint => "Int2",
        SpvReflectFormat.R32g32b32Sint => "Int3",
        SpvReflectFormat.R32g32b32a32Sint => "Int4",
        _ => format.ToString()
    };

    private static void EnsureReasonableCount(uint count, string subject)
    {
        if (count > MaximumReflectedElements)
        {
            throw new InvalidOperationException(
                $"SPIR-V reflection reported {count} {subject}; maximum is {MaximumReflectedElements}.");
        }
    }

    private static void EnsureSuccess(SpvReflectResult result, string operation)
    {
        if (result != SpvReflectResult.Success)
        {
            throw new InvalidOperationException($"SPIR-V reflection could not {operation}: {result}.");
        }
    }
}
