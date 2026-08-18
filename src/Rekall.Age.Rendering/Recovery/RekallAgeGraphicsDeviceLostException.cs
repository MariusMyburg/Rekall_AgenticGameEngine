namespace Rekall.Age.Rendering.Recovery;

public static class RekallAgeGraphicsFailureKinds
{
    public const string DeviceLost = "graphics.device-lost";
    public const string SwapchainInvalid = "graphics.swapchain-invalid";
    public const string Fatal = "fatal";
}

public sealed class RekallAgeGraphicsDeviceLostException : Exception
{
    public RekallAgeGraphicsDeviceLostException(
        string message,
        string kind = RekallAgeGraphicsFailureKinds.DeviceLost,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (kind is not RekallAgeGraphicsFailureKinds.DeviceLost and
            not RekallAgeGraphicsFailureKinds.SwapchainInvalid)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
    }

    public string Kind { get; }
}

public sealed record RekallAgeGraphicsFailureClassification(
    string Kind,
    string Code,
    bool IsRecoverable,
    Exception Exception);
