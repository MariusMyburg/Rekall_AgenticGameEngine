using Rekall.Age.Modeling;

namespace Rekall.Age.Tests.Modeling;

public sealed class ComprehensiveModelingCatalogTests
{
    [Fact]
    public void WaveOnePublishesTheRequiredHardSurfaceCurveNormalAndPrimitiveDescriptors()
    {
        var operations = new RekallAgeMeshOperationExecutor().Descriptors
            .Select(item => item.OperationId)
            .ToHashSet(StringComparer.Ordinal);
        var nodes = RekallAgeModelingNodeCatalog.CreateDefault().Descriptors
            .Select(item => item.TypeId)
            .ToHashSet(StringComparer.Ordinal);
        var modifiers = RekallAgeModifierCatalog.CreateDefault().Descriptors
            .Select(item => item.TypeId)
            .ToHashSet(StringComparer.Ordinal);

        AssertContainsAll(operations,
            "bevel_edges",
            "inset_faces",
            "solidify",
            "weighted_normals");
        AssertContainsAll(nodes,
            "rekall.modeling.bevel",
            "rekall.modeling.inset",
            "rekall.modeling.mirror",
            "rekall.modeling.array",
            "rekall.modeling.curve.profile_sweep",
            "rekall.modeling.primitive.plane",
            "rekall.modeling.primitive.disc",
            "rekall.modeling.primitive.cylinder",
            "rekall.modeling.primitive.cone",
            "rekall.modeling.primitive.ico_sphere",
            "rekall.modeling.primitive.capsule");
        AssertContainsAll(modifiers,
            "rekall.modifier.bevel",
            "rekall.modifier.solidify",
            "rekall.modifier.mirror",
            "rekall.modifier.array",
            "rekall.modifier.weighted_normals");
    }

    private static void AssertContainsAll(IReadOnlySet<string> actual, params string[] expected)
    {
        var missing = expected.Where(item => !actual.Contains(item)).ToArray();
        Assert.True(missing.Length == 0,
            $"Missing comprehensive modeling descriptors: {string.Join(", ", missing)}");
    }
}
