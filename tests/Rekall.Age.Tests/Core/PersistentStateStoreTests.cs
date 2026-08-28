using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Tests.Core;

/// <summary>
/// The engine had no persistence primitive, so an authored game could not remember settings or
/// campaign progress across a restart. This store is that contract.
///
/// A slot name arrives from authored content, so the tests that matter most here are the ones
/// pinning that a slot is an identifier and never a path.
/// </summary>
public sealed class PersistentStateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "rekall-state-tests",
        Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task WrittenStateReadsBack()
    {
        var document = new JsonObject
        {
            ["masterVolume"] = 0.7,
            ["vsync"] = true,
            ["lastMission"] = "m01-standing-watch",
        };

        await RekallAgePersistentStateStore.WriteAsync(_root, "settings", document, CancellationToken.None);
        var restored = await RekallAgePersistentStateStore.ReadAsync(_root, "settings", CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(0.7, restored!["masterVolume"]!.GetValue<double>());
        Assert.True(restored["vsync"]!.GetValue<bool>());
        Assert.Equal("m01-standing-watch", restored["lastMission"]!.GetValue<string>());
    }

    [Fact]
    public async Task MissingSlotReadsAsNullRatherThanThrowing()
    {
        // A first run has no saved state, and that is not an error condition.
        Assert.Null(await RekallAgePersistentStateStore.ReadAsync(_root, "never-written", CancellationToken.None));
    }

    [Fact]
    public async Task WritingASlotTwiceReplacesIt()
    {
        await RekallAgePersistentStateStore.WriteAsync(
            _root, "progress", new JsonObject { ["mission"] = 1 }, CancellationToken.None);
        await RekallAgePersistentStateStore.WriteAsync(
            _root, "progress", new JsonObject { ["mission"] = 2 }, CancellationToken.None);

        var restored = await RekallAgePersistentStateStore.ReadAsync(_root, "progress", CancellationToken.None);
        Assert.Equal(2, restored!["mission"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("sub/dir")]
    [InlineData("sub\\dir")]
    [InlineData("C:/absolute")]
    [InlineData("has space")]
    [InlineData("")]
    public void SlotNamesThatCouldEscapeTheStateDirectoryAreRejected(string slot)
    {
        Assert.ThrowsAny<ArgumentException>(() => RekallAgePersistentStateStore.ResolveSlotPath(_root, slot));
    }

    [Fact]
    public void ResolvedSlotsStayInsideTheProjectStateDirectory()
    {
        var path = RekallAgePersistentStateStore.ResolveSlotPath(_root, "campaign.slot-1");
        var stateRoot = Path.GetFullPath(Path.Combine(_root, "State"));

        Assert.StartsWith(stateRoot + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
        Assert.EndsWith("campaign.slot-1.json", path, StringComparison.Ordinal);
    }
}
