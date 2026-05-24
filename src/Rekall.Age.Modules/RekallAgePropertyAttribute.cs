namespace Rekall.Age.Modules;

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class RekallAgePropertyAttribute : Attribute
{
    public string? Kind { get; init; }

    public string? AssetKind { get; init; }

    public double Minimum { get; init; } = double.NaN;

    public double Maximum { get; init; } = double.NaN;
}
