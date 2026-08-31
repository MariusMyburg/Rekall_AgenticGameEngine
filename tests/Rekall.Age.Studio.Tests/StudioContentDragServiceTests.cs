using System.Numerics;
using System.IO;
using System.Text.Json;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioContentDragServiceTests
{
    [Fact]
    public void StudioWiresPrivateThresholdDragAndCopyOnlyInspectorAndViewportTargets()
    {
        var browser = Source("ContentBrowser.xaml.cs");
        var window = Source("MainWindow.xaml");
        var codeBehind = Source("MainWindow.xaml.cs");

        Assert.Contains("SystemParameters.MinimumHorizontalDragDistance", browser, StringComparison.Ordinal);
        Assert.Contains("RekallAgeStudioContentDragService.DataFormat", browser, StringComparison.Ordinal);
        Assert.Contains("DragDropEffects.Copy", browser, StringComparison.Ordinal);
        Assert.Contains("OnInspectorPropertyDragOver", window, StringComparison.Ordinal);
        Assert.Contains("OnInspectorPropertyDrop", window, StringComparison.Ordinal);
        Assert.Contains("OnSceneViewportDragOver", window, StringComparison.Ordinal);
        Assert.Contains("OnSceneViewportDrop", window, StringComparison.Ordinal);
        Assert.Contains("DragDropEffects.Copy", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyContentDropResultAsync", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadRoundTripContainsOnlyStableIdentityKindAndOperations()
    {
        var item = Item("asset_texture", "texture", [RekallAgeContentCapability.Assign], @"C:\sentinel\private.png");

        var json = RekallAgeStudioContentDragPayload.FromItem(item).ToJson();
        var roundTrip = RekallAgeStudioContentDragPayload.FromJson(json);

        Assert.Equal(item.Id, roundTrip.ContentId);
        Assert.Equal(item.Kind, roundTrip.ContentKind);
        Assert.Equal([RekallAgeContentCapability.Assign], roundTrip.Operations);
        Assert.DoesNotContain("sentinel", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["contentId", "contentKind", "operations"],
            JsonDocument.Parse(json).RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    [Fact]
    public async Task CompatibleTextureDropUsesCanonicalPropertyMutationAndReturnsTransactionEvidence()
    {
        var mutations = new RecordingMutationCommand();
        var service = Service(mutations: mutations);

        var result = await service.AssignAsync(
            Payload("asset_texture", "texture", RekallAgeContentCapability.Assign),
            new("entity", "Rekall.Material", "albedo", "Texture", EntityLocked: false, PropertyLocked: false),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("tx-property", result.TransactionId);
        var mutation = Assert.Single(mutations.Requests);
        Assert.Equal("rekall.component.set_property", mutation.Tool);
        Assert.Equal("asset_texture", mutation.PropertyValue);
    }

    [Fact]
    public async Task IncompatibleAssetKindIsRejectedWithoutMutation()
    {
        var mutations = new RecordingMutationCommand();
        var service = Service(mutations: mutations);

        var result = await service.AssignAsync(
            Payload("asset_audio", "audio", RekallAgeContentCapability.Assign),
            new("entity", "Rekall.Material", "albedo", "texture", false, false),
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("REKALL_CONTENT_DROP_INCOMPATIBLE", result.Code);
        Assert.Empty(mutations.Requests);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LockedEntityOrPropertyIsRejected(bool entityLocked, bool propertyLocked)
    {
        var mutations = new RecordingMutationCommand();
        var result = await Service(mutations: mutations).AssignAsync(
            Payload("asset_texture", "texture", RekallAgeContentCapability.Assign),
            new("entity", "Rekall.Material", "albedo", "texture", entityLocked, propertyLocked),
            CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("REKALL_CONTENT_DROP_LOCKED", result.Code);
        Assert.Empty(mutations.Requests);
    }

    [Fact]
    public async Task ModelPlacementUsesRenderDerivedWorldHit()
    {
        var placements = new RecordingPlacementCommand();
        var result = await Service(placements: placements).PlaceAsync(
            Payload("rover", "model", RekallAgeContentCapability.Place),
            new(new Vector3(10, 2, -4), new Vector3(0, 3, -8), Vector3.UnitZ, 5),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("tx-place", result.TransactionId);
        var placement = Assert.Single(placements.Requests);
        Assert.Equal("rekall.model_asset.instantiate", placement.Tool);
        Assert.Equal(new Vector3(10, 2, -4), placement.Position);
    }

    [Fact]
    public async Task ModelPlacementFallsBackDeterministicallyInFrontOfCamera()
    {
        var placements = new RecordingPlacementCommand();
        await Service(placements: placements).PlaceAsync(
            Payload("rover", "model-asset", RekallAgeContentCapability.Place),
            new(null, new Vector3(1, 2, 3), Vector3.UnitZ, 6),
            CancellationToken.None);

        Assert.Equal(new Vector3(1, 2, 9), Assert.Single(placements.Requests).Position);
    }

    [Fact]
    public async Task NonPlaceableContentIsRejected()
    {
        var placements = new RecordingPlacementCommand();
        var result = await Service(placements: placements).PlaceAsync(
            Payload("texture", "texture", RekallAgeContentCapability.Assign),
            new(null, Vector3.Zero, Vector3.UnitZ, 5), CancellationToken.None);

        Assert.Equal("REKALL_CONTENT_DROP_NOT_PLACEABLE", result.Code);
        Assert.Empty(placements.Requests);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutInvokingCommands()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var mutations = new RecordingMutationCommand();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(mutations: mutations).AssignAsync(
            Payload("texture", "texture", RekallAgeContentCapability.Assign),
            new("entity", "component", "property", "texture", false, false), cancellation.Token).AsTask());
        Assert.Empty(mutations.Requests);
    }

    private static RekallAgeStudioContentDragService Service(
        RecordingMutationCommand? mutations = null, RecordingPlacementCommand? placements = null) =>
        new(mutations ?? new(), placements ?? new());

    private static RekallAgeStudioContentDragPayload Payload(string id, string kind, params string[] operations) =>
        new(id, kind, operations);

    private static RekallAgeContentBrowserItem Item(string id, string kind, IReadOnlyList<string> capabilities, string path) =>
        new(id, id, kind, kind, "Imported", path, path, "1", "external", capabilities, "ready", null, new());

    private static string Source(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Rekall.Age.Studio", fileName));

    private sealed class RecordingMutationCommand : IRekallAgeStudioContentPropertyMutationCommand
    {
        public List<RekallAgeStudioContentPropertyMutation> Requests { get; } = [];
        public ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteAsync(RekallAgeStudioContentPropertyMutation request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(new RekallAgeStudioContentCommandEvidence(true, "OK", "assigned", "tx-property"));
        }
    }

    private sealed class RecordingPlacementCommand : IRekallAgeStudioContentPlacementCommand
    {
        public List<RekallAgeStudioContentPlacement> Requests { get; } = [];
        public ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteAsync(RekallAgeStudioContentPlacement request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(new RekallAgeStudioContentCommandEvidence(true, "OK", "placed", "tx-place"));
        }
    }
}
