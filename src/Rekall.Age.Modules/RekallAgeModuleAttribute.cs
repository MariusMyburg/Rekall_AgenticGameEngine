namespace Rekall.Age.Modules;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RekallAgeModuleAttribute : Attribute
{
    public RekallAgeModuleAttribute(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }
}
