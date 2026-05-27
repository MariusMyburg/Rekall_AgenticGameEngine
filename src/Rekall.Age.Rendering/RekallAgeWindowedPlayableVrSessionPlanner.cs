namespace Rekall.Age.Rendering;

public sealed record RekallAgeWindowedPlayableVrSessionPlan(
    bool ShouldStartHeadsetSubmit,
    bool UsesWindowedInputBridge,
    string Reason);

public static class RekallAgeWindowedPlayableVrSessionPlanner
{
    public static RekallAgeWindowedPlayableVrSessionPlan Plan(
        bool openXrRequested,
        bool headsetSessionReady,
        bool playableMode)
    {
        if (!openXrRequested)
        {
            return new RekallAgeWindowedPlayableVrSessionPlan(
                ShouldStartHeadsetSubmit: false,
                UsesWindowedInputBridge: false,
                "OpenXR was not requested for this windowed player session.");
        }

        if (!headsetSessionReady)
        {
            return new RekallAgeWindowedPlayableVrSessionPlan(
                ShouldStartHeadsetSubmit: false,
                UsesWindowedInputBridge: true,
                "OpenXR was requested, but the headset session is not ready.");
        }

        var reason = playableMode
            ? "Windowed playable VR starts headset submission and bridges SDL keyboard/mouse input into the generic runtime input stream."
            : "Windowed VR starts headset submission and bridges desktop input into the generic runtime input stream.";
        return new RekallAgeWindowedPlayableVrSessionPlan(
            ShouldStartHeadsetSubmit: true,
            UsesWindowedInputBridge: true,
            reason);
    }
}
