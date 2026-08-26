using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public static class RekallAgeUvProjection
{
    public static RekallAgeGeometryVector2 Project(
        RekallAgeGeometryVector3 point,
        string projection,
        string axis,
        RekallAgeGeometryVector3 faceNormal)
    {
        return projection switch
        {
            "planar" => Planar(point, axis),
            "box" => Planar(point, DominantPlane(faceNormal)),
            "cylindrical" => new(Math.Atan2(point.Z, point.X) / (2 * Math.PI) + 0.5, point.Y),
            "spherical" => Spherical(point),
            _ => throw new ArgumentOutOfRangeException(nameof(projection), projection, "Unsupported UV projection.")
        };
    }

    public static RekallAgeGeometryVector2 Planar(RekallAgeGeometryVector3 point, string axis) => axis switch
    {
        "xy" => new(point.X, point.Y),
        "xz" => new(point.X, point.Z),
        "yz" => new(point.Y, point.Z),
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unsupported planar UV axis.")
    };

    public static string DominantPlane(RekallAgeGeometryVector3 normal)
    {
        var x = Math.Abs(normal.X); var y = Math.Abs(normal.Y); var z = Math.Abs(normal.Z);
        return z >= x && z >= y ? "xy" : y >= x ? "xz" : "yz";
    }

    private static RekallAgeGeometryVector2 Spherical(RekallAgeGeometryVector3 point)
    {
        var length = Math.Sqrt(point.X * point.X + point.Y * point.Y + point.Z * point.Z);
        if (length <= 1e-12) return new(0.5, 0.5);
        return new(Math.Atan2(point.Z, point.X) / (2 * Math.PI) + 0.5, Math.Asin(Math.Clamp(point.Y / length, -1, 1)) / Math.PI + 0.5);
    }
}
