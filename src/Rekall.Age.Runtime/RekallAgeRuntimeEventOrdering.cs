using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

/// <summary>
/// Canonical ordering for the world's event list: frame, then entity name, then event type,
/// then handler.
///
/// This ordering was previously applied as a side effect of the trigger and collision event
/// systems, which re-sorted the whole accumulated list every step even when they had no
/// trigger or collider work of their own. Other systems' events depended on that normalization
/// happening, so gating those systems on component presence silently changed event order for
/// scenes without colliders.
///
/// Extracted here so the ordering guarantee is explicit and survives those systems skipping
/// their pass. It stays deliberately cheap in the common case: an already-ordered list is
/// returned untouched, so no allocation or copy occurs.
/// </summary>
public static class RekallAgeRuntimeEventOrdering
{
    public static IReadOnlyList<RekallAgeRuntimeEvent> Canonical(IEnumerable<RekallAgeRuntimeEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events
            .OrderBy(runtimeEvent => runtimeEvent.Frame)
            .ThenBy(runtimeEvent => runtimeEvent.EntityName, StringComparer.Ordinal)
            .ThenBy(runtimeEvent => runtimeEvent.Type, StringComparer.Ordinal)
            .ThenBy(runtimeEvent => runtimeEvent.Handler, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns the world with its events canonically ordered, or the same world instance when
    /// they already are.
    /// </summary>
    public static RekallAgeRuntimeWorld WithCanonicalEvents(RekallAgeRuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var events = world.Subsystems.Events.Events;
        if (IsCanonical(events))
        {
            return world;
        }

        return world with
        {
            Subsystems = world.Subsystems with
            {
                Events = new RekallAgeRuntimeEventView(Canonical(events))
            }
        };
    }

    private static bool IsCanonical(IReadOnlyList<RekallAgeRuntimeEvent> events)
    {
        for (var index = 1; index < events.Count; index++)
        {
            if (Compare(events[index - 1], events[index]) > 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int Compare(RekallAgeRuntimeEvent left, RekallAgeRuntimeEvent right)
    {
        var frame = left.Frame.CompareTo(right.Frame);
        if (frame != 0) return frame;
        var entityName = string.CompareOrdinal(left.EntityName, right.EntityName);
        if (entityName != 0) return entityName;
        var type = string.CompareOrdinal(left.Type, right.Type);
        if (type != 0) return type;
        return string.CompareOrdinal(left.Handler, right.Handler);
    }
}
