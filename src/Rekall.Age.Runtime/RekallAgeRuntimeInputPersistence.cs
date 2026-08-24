using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public static class RekallAgeRuntimeInputPersistence
{
    public static RekallAgeRuntimeInputState ForSimulationStep(
        RekallAgeRuntimeInputState capturedInput,
        int stepIndex)
    {
        ArgumentNullException.ThrowIfNull(capturedInput);
        if (stepIndex <= 0)
        {
            return capturedInput;
        }

        return capturedInput with
        {
            MouseDeltaX = 0,
            MouseDeltaY = 0,
            MouseWheelDelta = 0,
            PressedKeysThisFrame = null,
            ReleasedKeysThisFrame = null,
            PressedButtonsThisFrame = null,
            ReleasedButtonsThisFrame = null,
            XrActions = capturedInput.XrActions?.Select(action => action with
            {
                WasPressed = false,
                WasReleased = false
            }).ToArray(),
            SemanticActions = capturedInput.SemanticActions?.Select(action => action with
            {
                WasPressed = false,
                WasReleased = false
            }).ToArray(),
            Controllers = capturedInput.Controllers?.Select(controller => controller with
            {
                PressedButtonsThisFrame = [],
                ReleasedButtonsThisFrame = []
            }).ToArray()
        };
    }
}
