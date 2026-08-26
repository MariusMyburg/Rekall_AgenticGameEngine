using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class RuntimeGeometrySignatureTests
{
    [Fact]
    public void EquivalentRebuiltGeometryAndLinesHaveStableContentSignatures()
    {
        static RekallAgeRuntimeViewportGeometryMesh Mesh(double x) => new(
            [new(x, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            [0, 1, 2]);
        static RekallAgeRuntimeViewportLineSegments Lines(double x) => new(
            [new(x, 0, 0, 1, 1, 1)],
            0.04);

        Assert.Equal(
            RekallAgeRuntimeGeometrySignature.For(Mesh(0)),
            RekallAgeRuntimeGeometrySignature.For(Mesh(0)));
        Assert.NotEqual(
            RekallAgeRuntimeGeometrySignature.For(Mesh(0)),
            RekallAgeRuntimeGeometrySignature.For(Mesh(0.5)));
        Assert.Equal(
            RekallAgeRuntimeGeometrySignature.For(Lines(0)),
            RekallAgeRuntimeGeometrySignature.For(Lines(0)));
        Assert.NotEqual(
            RekallAgeRuntimeGeometrySignature.For(Lines(0)),
            RekallAgeRuntimeGeometrySignature.For(Lines(0.5)));
    }
}
