using System.Runtime.CompilerServices;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

/// <summary>
/// Which component types a world contains, memoized per world instance.
///
/// Runtime systems run every fixed step regardless of whether the scene contains anything they
/// act on, and the usual <c>world.Entities.Select(...).ToArray()</c> shape rebuilds the whole
/// entity array and allocates a new world record each time. On a 5,000-entity scene that is
/// around 350,000 entity visits per frame across all systems, most for components the scene
/// does not contain. A system can guard its pass with one of these checks and return the world
/// untouched.
///
/// The set is keyed on the world instance rather than recomputed per call. Scanning every
/// entity per guard is itself O(entities x components), and with a guard on each system that
/// scan became measurable in its own right - the very cost the guards exist to avoid. Keying on
/// instance identity cannot go stale: any system that changes the world returns a new world
/// record, which misses and rebuilds the set, while the common case of several systems in a row
/// declining to change anything reuses one scan between them.
/// </summary>
public static class RekallAgeRuntimeComponentPresence
{
    private static readonly ConditionalWeakTable<RekallAgeRuntimeWorld, HashSet<string>> Cache = new();

    /// <summary>The distinct component types present anywhere in the world.</summary>
    public static IReadOnlySet<string> ComponentTypes(RekallAgeRuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (Cache.TryGetValue(world, out var cached))
        {
            return cached;
        }

        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in world.Entities)
        {
            foreach (var component in entity.Components)
            {
                types.Add(component.Type);
            }
        }

        Cache.AddOrUpdate(world, types);
        return types;
    }

    /// <summary>True when any entity carries a component of the given type.</summary>
    public static bool AnyEntityHas(RekallAgeRuntimeWorld world, string componentType) =>
        ComponentTypes(world).Contains(componentType);

    /// <summary>True when any entity carries a component of any of the given types.</summary>
    public static bool AnyEntityHasAny(RekallAgeRuntimeWorld world, params string[] componentTypes)
    {
        ArgumentNullException.ThrowIfNull(componentTypes);
        var present = ComponentTypes(world);
        foreach (var candidate in componentTypes)
        {
            if (present.Contains(candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when any entity carries a component whose type contains the given fragment.
    ///
    /// Matching loosely is deliberate for component *families* such as colliders: an unexpected
    /// match only means a system declines to skip its pass, which is merely slower, while a
    /// missed match would wrongly skip real work. Erring toward false positives keeps the guard
    /// safe as new component types are added.
    /// </summary>
    public static bool AnyEntityHasContaining(RekallAgeRuntimeWorld world, string componentTypeFragment)
    {
        foreach (var type in ComponentTypes(world))
        {
            if (type.Contains(componentTypeFragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when any entity carries a collider of any dimension or shape. Shared by the physics,
    /// collision-event, and trigger-event systems, which all derive their bodies from collider
    /// components and have nothing to do without one.
    /// </summary>
    public static bool AnyEntityHasCollider(RekallAgeRuntimeWorld world) =>
        AnyEntityHasContaining(world, "Collider");

    /// <summary>
    /// True when any entity carries a component whose type starts with the given prefix. Useful
    /// for families such as "Rekall.Animation" or "Rekall.Ui".
    /// </summary>
    public static bool AnyEntityHasPrefixed(RekallAgeRuntimeWorld world, string componentTypePrefix)
    {
        foreach (var type in ComponentTypes(world))
        {
            if (type.StartsWith(componentTypePrefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
