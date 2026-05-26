using System.Numerics;
using System.Reflection;
using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class OpenXrPoseConversionTests
{
    [Fact]
    public void OpenXrHeadYawMapsToEngineYawNotPitch()
    {
        var euler = Convert(Quaternion.CreateFromAxisAngle(Vector3.UnitY, ToRadians(30)));

        Assert.Equal(0, euler.X, precision: 3);
        Assert.Equal(30, euler.Y, precision: 3);
        Assert.Equal(0, euler.Z, precision: 3);
    }

    [Fact]
    public void OpenXrHeadPitchMapsToEnginePitchNotYaw()
    {
        var euler = Convert(Quaternion.CreateFromAxisAngle(Vector3.UnitX, ToRadians(20)));

        Assert.Equal(20, euler.X, precision: 3);
        Assert.Equal(0, euler.Y, precision: 3);
        Assert.Equal(0, euler.Z, precision: 3);
    }

    private static Vector3 Convert(Quaternion quaternion)
    {
        var method = typeof(RekallAgeSilkOpenXrHeadsetClearSubmitter).GetMethod(
            "ToEulerDegrees",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (Vector3)method.Invoke(null, [quaternion])!;
    }

    private static float ToRadians(double degrees)
    {
        return (float)(degrees * Math.PI / 180.0);
    }
}
