namespace Rekall.Age.Modules;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RekallAgeComponentAttribute : Attribute
{
    public RekallAgeComponentAttribute(string displayName)
    {
        DisplayName = displayName;
    }

    public string DisplayName { get; }
}
