using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeControllerInputTracker
{
    private readonly Dictionary<string, HashSet<string>> _heldButtons =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ConnectedDeviceIds => _heldButtons.Keys
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<RekallAgeRuntimeControllerState> Update(
        IReadOnlyList<RekallAgeRuntimeControllerState> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var result = new List<RekallAgeRuntimeControllerState>(current.Count);
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var controller in current
                     .OrderBy(item => item.PlayerIndex)
                     .ThenBy(item => item.DeviceId, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(controller.DeviceId) || !connected.Add(controller.DeviceId))
            {
                continue;
            }

            var held = controller.PressedButtons
                .Where(button => !string.IsNullOrWhiteSpace(button))
                .Select(button => button.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _heldButtons.TryGetValue(controller.DeviceId, out var previous);
            previous ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            result.Add(controller with
            {
                PressedButtons = held.OrderBy(button => button, StringComparer.Ordinal).ToArray(),
                PressedButtonsThisFrame = held.Except(previous, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(button => button, StringComparer.Ordinal)
                    .ToArray(),
                ReleasedButtonsThisFrame = previous.Except(held, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(button => button, StringComparer.Ordinal)
                    .ToArray()
            });
            _heldButtons[controller.DeviceId] = held;
        }

        foreach (var disconnected in _heldButtons.Keys.Where(id => !connected.Contains(id)).ToArray())
        {
            _heldButtons.Remove(disconnected);
        }

        return result;
    }
}
