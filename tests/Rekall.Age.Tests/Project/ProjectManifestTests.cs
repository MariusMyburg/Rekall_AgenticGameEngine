using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project;
using Rekall.Age.Project.Commands;

namespace Rekall.Age.Tests.Project;

public sealed class ProjectManifestTests
{
    [Fact]
    public async Task CreateProjectWritesDeterministicManifest()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());

        var transaction = RekallAgeTransaction.Begin("create project");
        var context = new RekallAgeCommandContext("test", transaction, CancellationToken.None);

        var result = await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new CreateProjectRequest(root, "Crystal Mines", ["rendering2d", "world", "rendering2d"]),
            context);

        Assert.True(result.Ok);
        Assert.Equal(["rendering2d", "world"], result.Value.Manifest.Capabilities);
        var manifestPath = Path.Combine(root, "rekall.project.json");
        Assert.True(File.Exists(manifestPath));

        var json = await File.ReadAllTextAsync(manifestPath);
        Assert.Contains("\"name\": \"Crystal Mines\"", json);
        Assert.Contains("\"rendering2d\"", json);
        Assert.Contains("\"world\"", json);
    }

    [Fact]
    public async Task AddCapabilityNormalizesAndPersistsCapability()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeProjectStore();
        await store.SaveAsync(root, RekallAgeProjectManifest.Create("Puzzle Box", ["world"]), CancellationToken.None);
        var command = new AddCapabilityCommand();
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("capability"), CancellationToken.None);

        var result = await command.ExecuteAsync(new AddCapabilityRequest(root, " Rendering3D "), context);

        Assert.True(result.Ok);
        Assert.Equal(["rendering3d", "world"], result.Value.Manifest.Capabilities);
    }
}
