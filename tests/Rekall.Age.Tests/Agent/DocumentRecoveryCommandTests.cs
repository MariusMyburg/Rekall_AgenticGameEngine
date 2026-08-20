using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Agent;

public sealed class DocumentRecoveryCommandTests
{
    [Fact]
    public async Task InspectionReturnsExecutableSceneRestoreActionWithoutMutatingDamage()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]), CancellationToken.None);
        var first = await store.LoadVersionedAsync(root, "Main", CancellationToken.None);
        await store.SaveIfRevisionAsync(root, first.Value with { Id = "replacement" }, first.Revision, CancellationToken.None);
        const string corrupt = "{ broken";
        await File.WriteAllTextAsync(store.GetScenePath(root, "Main"), corrupt);

        var result = await new InspectDocumentRecoveryCommand().ExecuteAsync(
            new InspectDocumentRecoveryRequest(root, "scene", "Main"),
            Context("inspect"));

        Assert.True(result.Ok);
        Assert.True(result.Value.Recoverable);
        Assert.Equal("REKALL_DOCUMENT_JSON_MALFORMED", result.Value.Primary.Code);
        var action = Assert.Single(result.Value.NextActions);
        Assert.Equal("rekall.recovery.restore_document", action.Tool);
        Assert.Equal(result.Value.Primary.Revision, action.Arguments["expectedRevision"]);
        Assert.Equal(corrupt, await File.ReadAllTextAsync(store.GetScenePath(root, "Main")));
    }

    [Fact]
    public async Task RestoreCommandReturnsValidationActionAndAllowsOrdinaryMutation()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]), CancellationToken.None);
        var first = await store.LoadVersionedAsync(root, "Main", CancellationToken.None);
        await store.SaveIfRevisionAsync(root, first.Value with { Id = "replacement" }, first.Revision, CancellationToken.None);
        var corrupt = System.Text.Encoding.UTF8.GetBytes("[]");
        await File.WriteAllBytesAsync(store.GetScenePath(root, "Main"), corrupt);
        var corruptRevision = RekallAgeDocumentRevision.Compute(corrupt);

        var restored = await new RestoreDocumentRecoveryCommand().ExecuteAsync(
            new RestoreDocumentRecoveryRequest(root, "scene", corruptRevision, "Main"),
            Context("restore"));

        Assert.True(restored.Ok);
        Assert.Equal(first.Revision, restored.Value.RestoredRevision);
        Assert.Equal("rekall.validation.scene", Assert.Single(restored.Value.NextActions).Tool);
        var mutation = await new CreateEntityCommand().ExecuteAsync(
            new CreateEntityRequest(root, "Main", "After Restore", ["proof"]),
            Context("mutate"));
        Assert.True(mutation.Ok);
        Assert.Equal("After Restore", Assert.Single((await store.LoadAsync(root, "Main", CancellationToken.None)).Entities).Name);
    }

    [Fact]
    public async Task CommandsRouteProjectManifestRecoveryWithoutASceneName()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeProjectStore();
        await store.SaveAsync(root, RekallAgeProjectManifest.Create("Original", ["world"]), CancellationToken.None);
        var first = await store.LoadVersionedAsync(root, CancellationToken.None);
        await store.SaveIfRevisionAsync(root, first.Value with { Name = "Replacement" }, first.Revision, CancellationToken.None);
        var corrupt = System.Text.Encoding.UTF8.GetBytes("{ invalid");
        await File.WriteAllBytesAsync(Path.Combine(root, RekallAgeProjectStore.ManifestFileName), corrupt);

        var inspection = await new InspectDocumentRecoveryCommand().ExecuteAsync(
            new InspectDocumentRecoveryRequest(root, "project"),
            Context("inspect project"));
        var restored = await new RestoreDocumentRecoveryCommand().ExecuteAsync(
            new RestoreDocumentRecoveryRequest(root, "project", inspection.Value.Primary.Revision),
            Context("restore project"));

        Assert.True(inspection.Ok);
        Assert.True(restored.Ok);
        Assert.Equal("Original", (await store.LoadAsync(root, CancellationToken.None)).Name);
        Assert.Equal("rekall.validation.project", Assert.Single(restored.Value.NextActions).Tool);
    }

    [Fact]
    public async Task RestoreCommandRejectsRevisionThatBecameStaleAfterInspection()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]), CancellationToken.None);
        var first = await store.LoadVersionedAsync(root, "Main", CancellationToken.None);
        await store.SaveIfRevisionAsync(root, first.Value with { Id = "replacement" }, first.Revision, CancellationToken.None);
        await File.WriteAllTextAsync(store.GetScenePath(root, "Main"), "{ damaged once");
        var inspection = await new InspectDocumentRecoveryCommand().ExecuteAsync(
            new InspectDocumentRecoveryRequest(root, "scene", "Main"),
            Context("inspect stale"));
        const string changedAgain = "{ damaged twice";
        await File.WriteAllTextAsync(store.GetScenePath(root, "Main"), changedAgain);

        var result = await new RestoreDocumentRecoveryCommand().ExecuteAsync(
            new RestoreDocumentRecoveryRequest(root, "scene", inspection.Value.Primary.Revision, "Main"),
            Context("restore stale"));

        Assert.False(result.Ok);
        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", Assert.Single(result.Errors).Code);
        Assert.Equal(changedAgain, await File.ReadAllTextAsync(store.GetScenePath(root, "Main")));
        Assert.Equal("rekall.recovery.inspect_document", Assert.Single(Assert.Single(result.Errors).SuggestedCommands!).Tool);
    }

    [Theory]
    [InlineData("project", "Main")]
    [InlineData("scene", null)]
    [InlineData("asset", null)]
    public async Task InspectionRejectsAmbiguousOrUnsupportedDocumentTargets(string kind, string? sceneName)
    {
        var result = await new InspectDocumentRecoveryCommand().ExecuteAsync(
            new InspectDocumentRecoveryRequest(TestPaths.CreateTempDirectory(), kind, sceneName),
            Context("invalid"));

        Assert.False(result.Ok);
        Assert.Equal("REKALL_DOCUMENT_RECOVERY_REQUEST_INVALID", Assert.Single(result.Errors).Code);
    }

    private static RekallAgeCommandContext Context(string purpose) =>
        new("test", RekallAgeTransaction.Begin(purpose), CancellationToken.None);
}
