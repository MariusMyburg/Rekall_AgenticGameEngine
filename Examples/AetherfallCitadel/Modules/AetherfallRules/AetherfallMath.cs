namespace Game.Modules.AetherfallRules;

internal static class AetherfallMath
{
    public static (double X, double Z) NormalizePlanar(double x, double z)
    {
        var lengthSquared = x * x + z * z;
        if (lengthSquared <= 1)
        {
            return (x, z);
        }

        var inverseLength = 1 / Math.Sqrt(lengthSquared);
        return (x * inverseLength, z * inverseLength);
    }
}
