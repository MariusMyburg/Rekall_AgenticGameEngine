using Rekall.Age.Rendering.Windows;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanPresentationInfrastructureTests
{
    [Fact]
    public void BestEffortCleanupRunsEveryActionInReverseAndAggregatesFailures()
    {
        var calls = new List<string>();
        var logs = new List<string>();
        var registrations = new RekallAgeCleanupRegistration[]
        {
            new("device", () => calls.Add("device")),
            new("pipeline", () =>
            {
                calls.Add("pipeline");
                throw new InvalidOperationException("pipeline dispose failed");
            }),
            new("surface", () => calls.Add("surface"))
        };

        var error = Assert.Throws<AggregateException>(() =>
            RekallAgeBestEffortCleanup.RunInReverse(registrations, logs.Add));

        Assert.Equal(["surface", "pipeline", "device"], calls);
        Assert.Single(error.InnerExceptions);
        Assert.Equal("pipeline dispose failed", error.InnerExceptions[0].Message);
        Assert.Contains(logs, message =>
            message.Contains("pipeline", StringComparison.Ordinal)
            && message.Contains("pipeline dispose failed", StringComparison.Ordinal));
    }

    [Fact]
    public void PaddedMappedRowsAreCopiedToTightlyPackedRgba()
    {
        const int width = 3;
        const int height = 2;
        const int rowPitch = 16;
        byte[] mapped =
        [
            30, 20, 10, 1,
            60, 50, 40, 2,
            90, 80, 70, 3,
            201, 202, 203, 204,
            120, 110, 100, 4,
            150, 140, 130, 5,
            180, 170, 160, 6,
            205, 206, 207, 208
        ];

        var pixels = RekallAgeVulkanRgbaReadback.CopyToTightlyPackedRgba(
            width,
            height,
            rowPitch,
            bgra: true,
            offset => mapped[offset]);

        Assert.Equal(
        [
            10, 20, 30, 255,
            40, 50, 60, 255,
            70, 80, 90, 255,
            100, 110, 120, 255,
            130, 140, 150, 255,
            160, 170, 180, 255
        ],
        pixels);
    }
}
