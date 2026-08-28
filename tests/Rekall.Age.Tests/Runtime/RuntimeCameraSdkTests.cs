using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeCameraSdkTests
{
    [Fact]
    public void RuntimeModuleSdkExposesCameraBasisFromTransformRotation()
    {
        var identity = RekallAgeRuntimeTransform.Identity;
        var yawRight = identity with { Rotation3D = new RekallAgeRuntimeVector3(0, 90, 0) };
        var pitchDown = identity with { Rotation3D = new RekallAgeRuntimeVector3(30, 0, 0) };

        AssertVector(new RekallAgeRuntimeVector3(0, 0, 1), identity.Forward3D());
        AssertVector(new RekallAgeRuntimeVector3(1, 0, 0), yawRight.Forward3D());
        Assert.True(pitchDown.Forward3D().Y < 0);
        AssertVector(new RekallAgeRuntimeVector3(1, 0, 0), identity.Right3D());
        AssertVector(new RekallAgeRuntimeVector3(0, 1, 0), identity.Up3D());
    }

    [Fact]
    public void RuntimeModuleSdkOffsetsPositionsAlongCameraBasis()
    {
        var transform = RekallAgeRuntimeTransform.Identity with
        {
            Position3D = new RekallAgeRuntimeVector3(10, 2, -4),
            Rotation3D = new RekallAgeRuntimeVector3(0, 90, 0)
        };

        var position = transform.Offset3D(forward: 2, right: 3, up: -1);

        AssertVector(new RekallAgeRuntimeVector3(12, 1, -7), position);
    }

    [Fact]
    public void RuntimeModuleSdkScreenBasisMatchesRendererLookAtHandedness()
    {
        // The renderer takes screen right as cross(forward, up) - see the Vulkan batch
        // builder's screenRight - which is the opposite sign to the body +X axis Right3D
        // reports. Code that projects world points to pixels needs this one; getting it wrong
        // mirrors the image about its vertical centre line.
        var identity = RekallAgeRuntimeTransform.Identity;

        AssertVector(new RekallAgeRuntimeVector3(-1, 0, 0), identity.ScreenRight3D());
        AssertVector(new RekallAgeRuntimeVector3(1, 0, 0), identity.Right3D());
        AssertVector(new RekallAgeRuntimeVector3(0, 1, 0), identity.ScreenUp3D());
    }

    [Fact]
    public void RuntimeModuleSdkScreenBasisStaysOrthonormalUnderCombinedRotation()
    {
        var transform = RekallAgeRuntimeTransform.Identity with
        {
            Rotation3D = new RekallAgeRuntimeVector3(22, 85, 0)
        };

        var forward = transform.Forward3D();
        var right = transform.ScreenRight3D();
        var up = transform.ScreenUp3D();

        Assert.Equal(1.0, Length(right), precision: 6);
        Assert.Equal(1.0, Length(up), precision: 6);
        Assert.Equal(0.0, Dot(forward, right), precision: 6);
        Assert.Equal(0.0, Dot(forward, up), precision: 6);
        Assert.Equal(0.0, Dot(right, up), precision: 6);
    }

    private static double Dot(RekallAgeRuntimeVector3 a, RekallAgeRuntimeVector3 b) =>
        (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static double Length(RekallAgeRuntimeVector3 value) => Math.Sqrt(Dot(value, value));

    private static void AssertVector(RekallAgeRuntimeVector3 expected, RekallAgeRuntimeVector3 actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 6);
        Assert.Equal(expected.Y, actual.Y, precision: 6);
        Assert.Equal(expected.Z, actual.Z, precision: 6);
    }
}
