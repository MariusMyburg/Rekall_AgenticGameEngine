using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Assets;

public sealed class AssetPipelineImportTests
{
    [Fact]
    public async Task ImportWithReportWritesSourceImportedAndCookedRecords()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "player.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5], CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ImportAssetWithReportCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("import"), CancellationToken.None);

        var result = await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report",
            new ImportAssetWithReportRequest(root, source, "sprite", "Player"),
            context);

        Assert.True(result.Ok);
        Assert.True(result.Value.Report.Imported);
        Assert.Equal("sprite", result.Value.Report.Kind);
        Assert.Single(result.Value.Pipeline.Sources);
        Assert.Single(result.Value.Pipeline.Imported);
        Assert.Single(result.Value.Pipeline.CookedArtifacts);
        Assert.Contains(
            "asset-pipeline.age.json",
            context.Transaction.ChangedResources.Single(path => path.EndsWith("asset-pipeline.age.json", StringComparison.Ordinal)));
    }
}
