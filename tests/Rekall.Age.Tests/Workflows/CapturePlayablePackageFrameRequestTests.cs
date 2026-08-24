using Rekall.Age.Playback;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class CapturePlayablePackageFrameRequestTests
{
    [Fact]
    public void RequestAdaptsLegacyInputListVariable()
    {
        IReadOnlyList<RekallAgePlaybackInput> legacyInputs =
        [
            new RekallAgePlaybackInput(-1, PrimaryAction: true, DeltaSeconds: 0.25)
        ];

        var request = new CapturePlayablePackageFrameRequest(
            "Package",
            "Output",
            1,
            320,
            180,
            legacyInputs);

        var input = Assert.Single(request.Inputs!);
        Assert.Equal(-1, input.VerticalAxis);
        Assert.True(input.PrimaryAction);
        Assert.Equal(0.25, input.DeltaSeconds);
    }
}
