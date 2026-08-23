using Rekall.Age.Runtime;

namespace Rekall.Age.Player.Web;

internal static partial class RekallAgePublishedModules
{
    internal static IReadOnlyList<RekallAgeRuntimeModuleRegistration> Registrations { get; } = Create();

    private static IReadOnlyList<RekallAgeRuntimeModuleRegistration> Create()
    {
        var registrations = new List<RekallAgeRuntimeModuleRegistration>();
        Add(registrations);
        return registrations;
    }

    static partial void Add(List<RekallAgeRuntimeModuleRegistration> registrations);
}
