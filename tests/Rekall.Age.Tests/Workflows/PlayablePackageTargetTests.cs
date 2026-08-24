using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class PlayablePackageTargetTests
{
    [Theory]
    [InlineData(null, false, "headless")]
    [InlineData(null, true, "windows")]
    [InlineData("", false, "headless")]
    [InlineData("WINDOWS", false, "windows")]
    [InlineData(" headless ", false, "headless")]
    public void TargetResolutionSupportsExplicitAndLegacyRequests(string? target, bool graphics, string expected)
    {
        var resolution = RekallAgePlayablePackageTargets.Resolve(target, graphics);

        Assert.True(resolution.Ok);
        Assert.Equal(expected, resolution.Target);
        Assert.Null(resolution.Error);
    }

    [Theory]
    [InlineData("web", false, "REKALL_PLAYABLE_PACKAGE_TARGET_UNSUPPORTED")]
    [InlineData("headless", true, "REKALL_PLAYABLE_PACKAGE_TARGET_CONFLICT")]
    public void TargetResolutionRejectsUnknownAndConflictingRequests(
        string target,
        bool graphics,
        string expectedCode)
    {
        var resolution = RekallAgePlayablePackageTargets.Resolve(target, graphics);

        Assert.False(resolution.Ok);
        Assert.Equal(expectedCode, resolution.Error?.Code);
    }

    [Fact]
    public async Task InvalidTargetReturnsStructuredFailureBeforeProjectVerification()
    {
        var command = new PackagePlayableGameCommand();
        var context = new RekallAgeCommandContext(
            "invalid-package-target",
            RekallAgeTransaction.Begin("reject invalid package target"),
            CancellationToken.None);

        var result = await command.ExecuteAsync(
            new PackagePlayableGameRequest("missing-project", Target: "console"),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_PLAYABLE_PACKAGE_TARGET_UNSUPPORTED");
        Assert.Equal("console", result.Value.Target);
    }
}
