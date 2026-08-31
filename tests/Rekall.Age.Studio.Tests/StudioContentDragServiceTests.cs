using System.Numerics;
using System.IO;
using System.Text.Json;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Studio;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.Workflows;
using Rekall.Age.World;
using Rekall.Age.Modeling.Commands;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Assets;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioContentDragServiceTests
{
    [Fact]
    public void CompositeResolverDistinguishesSameIdMeshAndModelAsset()
    {
        var mesh = Payload("shared", "mesh", RekallAgeContentCapability.Place);
        var model = Payload("shared", "model-asset", RekallAgeContentCapability.Place);
        var resolver = new RekallAgeStudioContentDragResolver((id, kind, origin) =>
            new[] { mesh, model }.SingleOrDefault(item => item.ContentId == id
                && item.ContentKind.Equals(kind, StringComparison.OrdinalIgnoreCase)
                && item.ContentOrigin.Equals(origin, StringComparison.OrdinalIgnoreCase)));

        Assert.Same(mesh, resolver.Resolve("shared", "mesh", "Imported"));
        Assert.Same(model, resolver.Resolve("shared", "model-asset", "Imported"));
    }
    [Fact]
    public void LongCommonPrefixContentIdsKeepBoundedDistinctCryptographicSuffixes()
    {
        var prefix = new string('a', 180);
        var first = Item(prefix + "-one", "model", [RekallAgeContentCapability.Place], "first.glb") with { Revision = "hash-one" };
        var second = Item(prefix + "-two", "model", [RekallAgeContentCapability.Place], "second.glb") with { Revision = "hash-two" };

        var firstIds = RekallAgeStudioImportedModelPublisher.GeneratedIds(first, 0);
        var secondIds = RekallAgeStudioImportedModelPublisher.GeneratedIds(second, 0);

        Assert.InRange(firstIds.MeshAssetId.Length, 1, 128);
        Assert.InRange(firstIds.ModelAssetId.Length, 1, 128);
        Assert.NotEqual(firstIds, secondIds);
        Assert.NotEqual(firstIds.SourceIdentity, secondIds.SourceIdentity);
        Assert.Matches("-[0-9a-f]{24}$", firstIds.MeshAssetId);
        Assert.Matches("-[0-9a-f]{24}$", firstIds.ModelAssetId);
    }

    [Fact]
    public async Task ConflictingGeneratedAssetIsNotReusedAndPlacedModelKeepsImportedGeometryProvenance()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-content-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "triangle.glb");
        var secondSource = Path.Combine(root, "triangle-two.glb");
        await File.WriteAllBytesAsync(source, TriangleGlb());
        await File.WriteAllBytesAsync(secondSource, TriangleGlb(2));
        var session = new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create());
        Assert.True((await session.CreateProjectAsync(root, "Collision", "Main", ["world"], ["world", "rendering3d"], "test", default)).Ok);
        var item = Item(new string('x', 180) + "-one", "model", [RekallAgeContentCapability.Place], source)
            with { DisplayName = new string('x', 180), Revision = "exact-source-hash-one" };
        var secondItem = Item(new string('x', 180) + "-two", "model", [RekallAgeContentCapability.Place], secondSource)
            with { DisplayName = new string('x', 180), Revision = "exact-source-hash-two" };
        var conflict = RekallAgeStudioImportedModelPublisher.GeneratedIds(item, 0);
        var topology = new RekallAgeMeshTopology(
            [1, 2, 3], [new(0, 0, 0), new(9, 0, 0), new(0, 9, 0)],
            [11, 12, 13], [new(0, 1), new(1, 2), new(0, 2)], [21], [0, 3],
            [31, 32, 33], [0, 1, 2], [0, 1, 2]);
        var mesh = await session.ExecuteAsync("rekall.mesh.create_asset", JsonSerializer.Serialize(
            new CreateMeshAssetRequest(root, conflict.MeshAssetId, "Foreign", topology)), "foreign mesh", "test", default);
        Assert.True(mesh.Ok, mesh.Summary);
        var model = await session.ExecuteAsync("rekall.asset.model.publish", JsonSerializer.Serialize(
            new PublishModelAssetRequest(root, conflict.ModelAssetId, "Foreign",
                new(RekallAgeModelSourceKind.Mesh, conflict.MeshAssetId, "rekall-content:foreign"), "missing")),
            "foreign model", "test", default);
        Assert.True(model.Ok, model.Summary);

        var modelAssetId = await RekallAgeStudioImportedModelPublisher.EnsurePublishedAsync(session, item, default);
        var secondModelAssetId = await RekallAgeStudioImportedModelPublisher.EnsurePublishedAsync(session, secondItem, default);
        Assert.NotEqual(conflict.ModelAssetId, modelAssetId);
        Assert.NotEqual(modelAssetId, secondModelAssetId);
        var published = await new RekallAgeModelAssetStore().LoadAsync(root, modelAssetId, default);
        var secondPublished = await new RekallAgeModelAssetStore().LoadAsync(root, secondModelAssetId, default);
        var firstMesh = await new Rekall.Age.Modeling.RekallAgeMeshAssetStore().LoadAsync(root, published.Source.AssetId, default);
        var secondMesh = await new Rekall.Age.Modeling.RekallAgeMeshAssetStore().LoadAsync(root, secondPublished.Source.AssetId, default);
        Assert.NotEqual(conflict.MeshAssetId, published.Source.AssetId);
        Assert.NotEqual(published.Source.AssetId, secondPublished.Source.AssetId);
        Assert.Equal(1, firstMesh.Topology.Positions.Max(position => position.X));
        Assert.Equal(2, secondMesh.Topology.Positions.Max(position => position.X));
        Assert.Equal("rekall-content:" + RekallAgeStudioImportedModelPublisher.GeneratedIds(item, 1).SourceIdentity,
            published.Source.OutputName);
        var placed = await session.ExecuteAsync("rekall.scene.instantiate_asset", JsonSerializer.Serialize(new
        {
            projectRoot = root, sceneName = "Main", modelAssetId, name = "Imported",
            position = new { x = 0, y = 0, z = 0 }, rotationDegrees = new { x = 0, y = 0, z = 0 },
            scale = new { x = 1, y = 1, z = 1 }
        }), "place", "test", default);
        Assert.True(placed.Ok, placed.Summary);
        var secondPlaced = await session.ExecuteAsync("rekall.scene.instantiate_asset", JsonSerializer.Serialize(new
        {
            projectRoot = root, sceneName = "Main", modelAssetId = secondModelAssetId, name = "Imported Two",
            position = new { x = 2, y = 0, z = 0 }, rotationDegrees = new { x = 0, y = 0, z = 0 },
            scale = new { x = 1, y = 1, z = 1 }
        }), "place second", "test", default);
        Assert.True(secondPlaced.Ok, secondPlaced.Summary);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", default);
        Assert.Contains(scene.Entities, entity => entity.Components.Any(component =>
            component.Type == "Rekall.ModelAssetReference"
            && component.Properties["assetId"]!.GetValue<string>() == modelAssetId));
        Assert.Contains(scene.Entities, entity => entity.Components.Any(component =>
            component.Type == "Rekall.ModelAssetReference"
            && component.Properties["assetId"]!.GetValue<string>() == secondModelAssetId));
    }
    [Theory]
    [InlineData(".glb")]
    [InlineData(".gltf")]
    public async Task ImportedGltfFamilyPublishesAndPlacesThroughCanonicalModelAssetCommands(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-content-drag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "triangle" + extension);
        await File.WriteAllBytesAsync(source, extension == ".glb" ? TriangleGlb() : TriangleGltf());
        var session = new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create());
        Assert.True((await session.CreateProjectAsync(root, "Drag", "Main", ["world"], ["world", "rendering3d"], "test", default)).Ok);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ImportAssetWithReportCommand());
        var imported = await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report", new(root, source, "model", "Triangle"),
            new("test", RekallAgeTransaction.Begin("import"), default));
        Assert.True(imported.Ok, imported.Summary);
        var report = imported.Value.Report;
        var item = new RekallAgeContentBrowserItem(report.AssetId, "Triangle", "model", "model", "Imported",
            report.ImportedPath, report.SourcePath, "1", "external", [RekallAgeContentCapability.Place], "Healthy", null, new());

        var modelAssetId = await RekallAgeStudioImportedModelPublisher.EnsurePublishedAsync(session, item, default);
        var placed = await session.ExecuteAsync("rekall.scene.instantiate_asset", JsonSerializer.Serialize(new
        {
            projectRoot = root, sceneName = "Main", modelAssetId, name = "Triangle",
            position = new { x = 0, y = 0, z = 0 }, rotationDegrees = new { x = 0, y = 0, z = 0 },
            scale = new { x = 1, y = 1, z = 1 }
        }), "place", "test", default);

        Assert.True(placed.Ok, placed.Summary);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", default);
        Assert.Contains(scene.Entities, entity => entity.Components.Any(component =>
            component.Type == "Rekall.ModelAssetReference"
            && component.Properties["assetId"]!.GetValue<string>() == modelAssetId));
    }
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
        Assert.DoesNotContain("async void OnInspectorPropertyDrop", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("async void OnSceneViewportDrop", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_contentDropCancellation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception) when (IsExpectedContentDropFailure(exception))", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RekallAgeStudioContentDragPayload.TryParse", codeBehind, StringComparison.Ordinal);
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
        Assert.Equal(["contentId", "contentKind", "contentOrigin", "operations"],
            JsonDocument.Parse(json).RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    public void MalformedPayloadIsRejectedWithoutThrowing(string json)
    {
        Assert.False(RekallAgeStudioContentDragPayload.TryParse(json, out _));
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
        var result = await Service(placements: placements,
            resolver: new FixedResolver(Payload("rover", "model", RekallAgeContentCapability.Place))).PlaceAsync(
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
        await Service(placements: placements,
            resolver: new FixedResolver(Payload("rover", "model-asset", RekallAgeContentCapability.Place))).PlaceAsync(
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

    [Fact]
    public async Task ForgedKindOrOperationIsRejectedAgainstCurrentContent()
    {
        var mutations = new RecordingMutationCommand();
        var service = Service(mutations: mutations,
            resolver: new FixedResolver(Payload("asset_texture", "texture", RekallAgeContentCapability.Assign)));

        var result = await service.AssignAsync(
            Payload("asset_texture", "audio", RekallAgeContentCapability.Assign, RekallAgeContentCapability.Place),
            new("entity", "component", "property", "texture", false, false), CancellationToken.None);

        Assert.Equal("REKALL_CONTENT_DROP_PAYLOAD_MISMATCH", result.Code);
        Assert.Empty(mutations.Requests);
    }

    [Fact]
    public async Task StaleContentIdIsRejectedBeforeMutation()
    {
        var mutations = new RecordingMutationCommand();
        var result = await Service(mutations: mutations, resolver: new FixedResolver(null)).AssignAsync(
            Payload("removed", "texture", RekallAgeContentCapability.Assign),
            new("entity", "component", "property", "texture", false, false), CancellationToken.None);

        Assert.Equal("REKALL_CONTENT_DROP_STALE", result.Code);
        Assert.Empty(mutations.Requests);
    }

    [Fact]
    public async Task InFlightAssignmentCancellationPropagates()
    {
        var mutation = new BlockingMutationCommand();
        using var cancellation = new CancellationTokenSource();
        var pending = Service(mutations: mutation).AssignAsync(
            Payload("texture", "texture", RekallAgeContentCapability.Assign),
            new("entity", "component", "property", "texture", false, false), cancellation.Token).AsTask();
        await mutation.Started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task InFlightPlacementCancellationPropagates()
    {
        var placement = new BlockingPlacementCommand();
        using var cancellation = new CancellationTokenSource();
        var pending = new RekallAgeStudioContentDragService(
            new RecordingMutationCommand(), placement,
            new FixedResolver(Payload("model", "model", RekallAgeContentCapability.Place))).PlaceAsync(
            Payload("model", "model", RekallAgeContentCapability.Place),
            new(null, Vector3.Zero, Vector3.UnitZ, 5), cancellation.Token).AsTask();
        await placement.Started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static RekallAgeStudioContentDragService Service(
        IRekallAgeStudioContentPropertyMutationCommand? mutations = null,
        RecordingPlacementCommand? placements = null,
        IRekallAgeStudioContentDragResolver? resolver = null) =>
        new(mutations ?? new RecordingMutationCommand(), placements ?? new(),
            resolver ?? new HeuristicResolver());

    private static RekallAgeStudioContentDragPayload Payload(string id, string kind, params string[] operations) =>
        new(id, kind, "Imported", operations);

    private static RekallAgeContentBrowserItem Item(string id, string kind, IReadOnlyList<string> capabilities, string path) =>
        new(id, id, kind, kind, "Imported", path, path, "1", "external", capabilities, "ready", null, new());

    private static string Source(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Rekall.Age.Studio", fileName));

    private static byte[] TriangleGlb(float extent = 1)
    {
        const string json = """
        {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],
         "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],
         "buffers":[{"byteLength":42}],
         "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":6}],
         "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}]}
        """;
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var jsonLength = (jsonBytes.Length + 3) / 4 * 4;
        var binLength = 44;
        var bytes = new byte[12 + 8 + jsonLength + 8 + binLength];
        static void U32(byte[] target, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(target, offset);
        U32(bytes, 0, 0x46546C67); U32(bytes, 4, 2); U32(bytes, 8, (uint)bytes.Length);
        U32(bytes, 12, (uint)jsonLength); U32(bytes, 16, 0x4E4F534A);
        jsonBytes.CopyTo(bytes, 20); Array.Fill<byte>(bytes, 0x20, 20 + jsonBytes.Length, jsonLength - jsonBytes.Length);
        var binHeader = 20 + jsonLength; U32(bytes, binHeader, (uint)binLength); U32(bytes, binHeader + 4, 0x004E4942);
        var bin = binHeader + 8;
        var positions = new float[] { 0, 0, 0, extent, 0, 0, 0, extent, 0 };
        for (var index = 0; index < positions.Length; index++) BitConverter.GetBytes(positions[index]).CopyTo(bytes, bin + index * 4);
        new ushort[] { 0, 1, 2 }.SelectMany(BitConverter.GetBytes).ToArray().CopyTo(bytes, bin + 36);
        return bytes;
    }

    private static byte[] TriangleGltf()
    {
        var bin = new byte[42];
        var positions = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
        for (var index = 0; index < positions.Length; index++) BitConverter.GetBytes(positions[index]).CopyTo(bin, index * 4);
        new ushort[] { 0, 1, 2 }.SelectMany(BitConverter.GetBytes).ToArray().CopyTo(bin, 36);
        var json = $$"""
        {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],
         "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],
         "buffers":[{"byteLength":42,"uri":"data:application/octet-stream;base64,{{Convert.ToBase64String(bin)}}"}],
         "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":6}],
         "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}]}
        """;
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

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

    private sealed class FixedResolver(RekallAgeStudioContentDragPayload? current) : IRekallAgeStudioContentDragResolver
    {
        public RekallAgeStudioContentDragPayload? Resolve(string contentId, string contentKind, string contentOrigin) =>
            current?.ContentId == contentId ? current : null;
    }

    private sealed class HeuristicResolver : IRekallAgeStudioContentDragResolver
    {
        public RekallAgeStudioContentDragPayload? Resolve(string contentId, string contentKind, string contentOrigin) => contentId switch
        {
            "rover" => Payload(contentId, "model", RekallAgeContentCapability.Place),
            "asset_audio" => Payload(contentId, "audio", RekallAgeContentCapability.Assign),
            _ => Payload(contentId, "texture", RekallAgeContentCapability.Assign)
        };
    }

    private sealed class BlockingMutationCommand : IRekallAgeStudioContentPropertyMutationCommand
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteAsync(
            RekallAgeStudioContentPropertyMutation request, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    private sealed class BlockingPlacementCommand : IRekallAgeStudioContentPlacementCommand
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteAsync(
            RekallAgeStudioContentPlacement request, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
