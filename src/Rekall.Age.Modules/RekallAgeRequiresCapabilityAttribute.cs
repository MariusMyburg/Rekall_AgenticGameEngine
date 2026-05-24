namespace Rekall.Age.Modules;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RekallAgeRequiresCapabilityAttribute : Attribute
{
    public RekallAgeRequiresCapabilityAttribute(string capability)
    {
        Capability = capability;
    }

    public string Capability { get; }
}
