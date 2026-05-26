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

    private static void AssertVector(RekallAgeRuntimeVector3 expected, RekallAgeRuntimeVector3 actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 6);
        Assert.Equal(expected.Y, actual.Y, precision: 6);
        Assert.Equal(expected.Z, actual.Z, precision: 6);
    }
}
