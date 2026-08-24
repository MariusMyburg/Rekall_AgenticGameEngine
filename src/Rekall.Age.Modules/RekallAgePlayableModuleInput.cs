using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Modules;

public sealed record RekallAgePlayableModuleInput(
    int VerticalAxis = 0,
    bool PrimaryAction = false,
    double DeltaSeconds = 1.0 / 60.0,
    IReadOnlyList<RekallAgeRuntimeInputAction>? InputActions = null)
{
    public double InputActionValue(string name) => FindAction(name)?.Value ?? 0;

    public bool IsInputActionDown(string name) => FindAction(name)?.IsDown ?? false;

    public bool WasInputActionPressed(string name) => FindAction(name)?.WasPressed ?? false;

    public bool WasInputActionReleased(string name) => FindAction(name)?.WasReleased ?? false;

    private RekallAgeRuntimeInputAction? FindAction(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : (InputActions ?? [])
                .FirstOrDefault(action => action.Name.Equals(name.Trim(), StringComparison.Ordinal));
}
