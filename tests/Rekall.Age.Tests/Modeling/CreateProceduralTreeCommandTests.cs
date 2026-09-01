using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Commands;
using Rekall.Age.Assets;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Modeling;

public sealed class CreateProceduralTreeCommandTests
{
    [Fact]
    public async Task CreatesPersistentBarkAndFoliageLodsAndSceneEntities()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-tree-command-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new RekallAgeSceneStore().SaveAsync(
                root,
                RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
                default);
            var context = new RekallAgeCommandContext(
                "test",
                RekallAgeTransaction.Begin("create-tree"),
                CancellationToken.None);

            var result = await new CreateProceduralTreeCommand().ExecuteAsync(
                new CreateProceduralTreeRequest(root, "Main", "Ancient Hero Oak", 72841),
                context);

            Assert.True(result.Ok, result.Summary);
            Assert.Equal(6, result.Value.AssetIds.Count);
            Assert.Equal(3, result.Value.LodCount);
            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", default);
            var bark = Assert.Single(scene.Entities, entity => entity.Id == result.Value.BarkEntityId);
            var foliage = Assert.Single(scene.Entities, entity => entity.Id == result.Value.FoliageEntityId);
            Assert.Equal(bark.Id, foliage.ParentId);
            Assert.Contains(bark.Components, component => component.Type == "Rekall.LodGroup");
            Assert.Contains(foliage.Components, component => component.Type == "Rekall.LodGroup");
            var barkMaterial = Assert.Single(bark.Components, component => component.Type == "Rekall.Material");
            var foliageMaterial = Assert.Single(foliage.Components, component => component.Type == "Rekall.Material");
            var barkTextureId = barkMaterial.Properties["baseColorTexture"]!.GetValue<string>();
            var foliageTextureId = foliageMaterial.Properties["baseColorTexture"]!.GetValue<string>();
            Assert.Equal("#FFFFFF", barkMaterial.Properties["baseColor"]!.GetValue<string>());
            Assert.Equal("#FFFFFF", foliageMaterial.Properties["baseColor"]!.GetValue<string>());
            var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(root, default);
            Assert.Contains(catalog.Assets, asset => asset.Id == barkTextureId && asset.TextureMetadata is not null);
            Assert.Contains(catalog.Assets, asset => asset.Id == foliageTextureId && asset.TextureMetadata is not null);
            Assert.All(catalog.Assets.Where(asset => asset.Id == barkTextureId || asset.Id == foliageTextureId),
                asset => Assert.True(File.Exists(asset.ImportedPath), asset.ImportedPath));
            Assert.All(result.Value.AssetIds, assetId =>
                Assert.True(File.Exists(new RekallAgeMeshAssetStore().GetMeshPath(root, assetId)), assetId));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
