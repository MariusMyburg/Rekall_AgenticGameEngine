namespace Rekall.Age.Rendering;

public static class RekallAgeSceneMaterialShaderAbi
{
    public const int Version = 1;

    public static IReadOnlyList<string> ValidateVertexElements(
        IReadOnlyList<RekallAgeShaderVertexElement> elements)
    {
        var expected = new[] { "Float3", "Float3", "Float4", "Float2" };
        var errors = new List<string>();
        foreach (var element in elements)
        {
            if (element.Location < 0
                || element.Location >= expected.Length
                || !element.Format.Equals(expected[element.Location], StringComparison.Ordinal))
            {
                errors.Add(
                    $"REKALL_SHADER_VERTEX_ABI_MISMATCH: location {element.Location} " +
                    $"uses {element.Format}; ABI {Version} requires " +
                    $"{(element.Location >= 0 && element.Location < expected.Length ? expected[element.Location] : "no attribute")}.");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateResources(
        IReadOnlyList<RekallAgeShaderResourceElement> resources)
    {
        var errors = new List<string>();
        foreach (var resource in resources)
        {
            var expectedKind = ExpectedResourceKind(resource.Set, resource.Binding);
            if (expectedKind is null || !resource.Kind.Equals(expectedKind, StringComparison.Ordinal))
            {
                errors.Add(
                    $"REKALL_SHADER_RESOURCE_ABI_MISMATCH: set {resource.Set} binding {resource.Binding} " +
                    $"'{resource.Name}' uses {resource.Kind}; ABI {Version} requires " +
                    $"{expectedKind ?? "no resource"}.");
            }
        }

        return errors;
    }

    private static string? ExpectedResourceKind(int set, int binding)
    {
        if ((set == 0 || set == 1) && binding == 0)
        {
            return "UniformBuffer";
        }

        if (set == 2 && binding is >= 0 and <= 13)
        {
            return binding % 2 == 0 ? "SampledImage" : "Sampler";
        }

        return null;
    }
}
