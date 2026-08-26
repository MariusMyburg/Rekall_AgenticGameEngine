namespace Rekall.Age.Rendering.Recovery;

public sealed class RekallAgeGraphicsFailureClassifier
{
    private const int MaximumExceptionDepth = 8;

    public RekallAgeGraphicsFailureClassification Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var current = exception;
        for (var depth = 0; depth < MaximumExceptionDepth && current is not null; depth++, current = current.InnerException)
        {
            if (current is RekallAgeGraphicsDeviceLostException typed)
            {
                return Recoverable(typed.Kind, typed);
            }

            if (IsVeldridException(current))
            {
                if (ContainsExactSignature(current.Message, "VK_ERROR_DEVICE_LOST"))
                {
                    return Recoverable(RekallAgeGraphicsFailureKinds.DeviceLost, current);
                }

                if (ContainsExactSignature(current.Message, "VK_ERROR_OUT_OF_DATE_KHR") ||
                    ContainsExactSignature(current.Message, "VK_ERROR_SURFACE_LOST_KHR") ||
                    // Veldrid's own swapchain-recreation path raises this human-readable message
                    // (not the raw Vulkan error code) when the OS reports the surface lost --
                    // observed in production after a live player session ran for a while.
                    ContainsExactSignature(current.Message, "Swapchain's underlying surface has been lost"))
                {
                    return Recoverable(RekallAgeGraphicsFailureKinds.SwapchainInvalid, current);
                }
            }
        }

        return new RekallAgeGraphicsFailureClassification(
            RekallAgeGraphicsFailureKinds.Fatal,
            "REKALL_PLAYER_RUNTIME_FATAL",
            false,
            exception);
    }

    private static bool IsVeldridException(Exception exception) =>
        exception.GetType().Name.Equals("VeldridException", StringComparison.Ordinal) &&
        exception.GetType().Namespace?.Equals("Veldrid", StringComparison.Ordinal) == true;

    private static bool ContainsExactSignature(string message, string signature) =>
        message.Contains(signature, StringComparison.OrdinalIgnoreCase);

    private static RekallAgeGraphicsFailureClassification Recoverable(string kind, Exception exception) =>
        new(
            kind,
            kind == RekallAgeGraphicsFailureKinds.DeviceLost
                ? "REKALL_PLAYER_GRAPHICS_DEVICE_LOST"
                : "REKALL_PLAYER_GRAPHICS_SWAPCHAIN_INVALID",
            true,
            exception);
}
