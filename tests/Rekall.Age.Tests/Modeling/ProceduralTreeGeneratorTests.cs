using System.Text.Json;
using System.Runtime.CompilerServices;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;

namespace Rekall.Age.Tests.Modeling;

public sealed class ProceduralTreeGeneratorTests
{
    [Fact]
    public void TemperateOakDefaultHasHeroTreeFoliageDensity()
    {
        var settings = RekallAgeProceduralTreeSettings.TemperateOak(17);

        Assert.True(settings.NearLeafBudget >= 900);
        Assert.True(settings.MidLeafBudget >= 300);
        Assert.True(settings.FarLeafBudget >= 80);
    }

    [Fact]
    public void MaterialSchemaAuthorsAlphaMaskForFoliageCards()
    {
        var property = typeof(RekallAgeMaterialComponent).GetProperty(nameof(RekallAgeMaterialComponent.AlphaMode))!;
        var metadata = Assert.Single(property.GetCustomAttributes(typeof(RekallAgePropertyAttribute), inherit: true)
            .Cast<RekallAgePropertyAttribute>());

        Assert.Contains("mask", metadata.AllowedValues!);
    }

    [Fact]
    public void OakPresetProducesDeterministicValidSeparatedLods()
    {
        var settings = RekallAgeProceduralTreeSettings.TemperateOak(seed: 72841);
        var first = RekallAgeProceduralTreeGenerator.Generate("oak", "Oak", settings);
        var second = RekallAgeProceduralTreeGenerator.Generate("oak", "Oak", settings);

        Assert.Equal(3, first.Lods.Count);
        Assert.Equal(JsonSerializer.Serialize(first, RekallAgeModelingJson.Options),
            JsonSerializer.Serialize(second, RekallAgeModelingJson.Options));

        var validator = new RekallAgeMeshValidator();
        foreach (var lod in first.Lods)
        {
            Assert.True(validator.Validate(lod.Bark).IsValid);
            Assert.True(validator.Validate(lod.Foliage).IsValid);
            Assert.Equal("Bark", Assert.Single(lod.Bark.MaterialSlots).Name);
            Assert.Equal("Foliage", Assert.Single(lod.Foliage.MaterialSlots).Name);
            Assert.NotEmpty(lod.Bark.Topology.FaceIds);
            Assert.NotEmpty(lod.Foliage.Topology.FaceIds);
        }
    }

    [Fact]
    public void OakHasBroadAsymmetricCrownFlaredBaseAndTaperedUpperWood()
    {
        var tree = RekallAgeProceduralTreeGenerator.Generate(
            "oak-shape", "Oak shape", RekallAgeProceduralTreeSettings.TemperateOak(17));
        var bark = tree.Lods[0].Bark;
        var positions = bark.Topology.Positions;
        var minY = positions.Min(point => point.Y);
        var maxY = positions.Max(point => point.Y);
        var height = maxY - minY;
        var widthX = positions.Max(point => point.X) - positions.Min(point => point.X);
        var widthZ = positions.Max(point => point.Z) - positions.Min(point => point.Z);
        var baseRadius = MaxRadius(positions.Where(point => point.Y <= minY + height * 0.03));
        var middleRadius = MaxRadius(positions.Where(point => point.Y >= minY + height * 0.18 && point.Y <= minY + height * 0.25));

        Assert.InRange(height, 8.0, 18.0);
        Assert.True(widthX / height >= 0.48, $"Crown was too narrow: {widthX / height:F3}");
        Assert.True(widthZ / height >= 0.42, $"Crown was too narrow: {widthZ / height:F3}");
        Assert.True(Math.Abs(widthX - widthZ) > height * 0.015, "Crown silhouette was implausibly symmetric.");
        Assert.True(baseRadius > middleRadius * 1.15, "The trunk base must visibly flare.");
    }

    [Fact]
    public void BarkHasLongitudinalUvsAndFoliageUsesAtlasReadyLeafCards()
    {
        var tree = RekallAgeProceduralTreeGenerator.Generate(
            "oak-uv", "Oak UV", RekallAgeProceduralTreeSettings.TemperateOak(92));
        var lod = tree.Lods[0];
        var barkUvs = Attribute(lod.Bark, "uv0");
        var foliageUvs = Attribute(lod.Foliage, "uv0");

        Assert.True(barkUvs.Select(ReadFloat2).Select(uv => uv.Y).Distinct().Count() > 8);
        Assert.All(foliageUvs.Select(ReadFloat2), uv =>
        {
            Assert.InRange(uv.X, 0, 1);
            Assert.InRange(uv.Y, 0, 1);
        });
        // Two crossed rectangular planes preserve the complete alpha-authored texture instead
        // of cropping and warping it through a second hard-coded leaf silhouette.
        Assert.Equal(lod.LeafCardCount * 4, lod.Foliage.Topology.FaceIds.Count);
        Assert.True(lod.LeafCardCount >= 180);
    }

    [Fact]
    public void OakFoliageOccupiesAnIrregularThreeDimensionalCrownVolume()
    {
        var tree = RekallAgeProceduralTreeGenerator.Generate(
            "oak-volume", "Oak volume", RekallAgeProceduralTreeSettings.TemperateOak(930176));
        var positions = tree.Lods[0].Foliage.Topology.Positions;
        var occupiedCells = positions
            .Select(point => (
                X: (int)Math.Floor(point.X / 0.75),
                Y: (int)Math.Floor(point.Y / 0.75),
                Z: (int)Math.Floor(point.Z / 0.75)))
            .Distinct()
            .Count();
        var zBands = positions.GroupBy(point => (int)Math.Floor(point.Z / 0.75)).Count();

        Assert.True(occupiedCells >= 2_000, $"Foliage occupied only {occupiedCells} crown cells.");
        Assert.True(zBands >= 15, $"Foliage occupied only {zBands} depth bands.");
    }

    [Fact]
    public void LodComplexityAndLeafBudgetsDecreaseMonotonically()
    {
        var tree = RekallAgeProceduralTreeGenerator.Generate(
            "oak-lod", "Oak LOD", RekallAgeProceduralTreeSettings.TemperateOak(5));

        Assert.Collection(tree.Lods,
            lod => { Assert.Equal(0, lod.Level); Assert.InRange(lod.LeafCardCount, 180, 900); },
            lod => { Assert.Equal(1, lod.Level); Assert.InRange(lod.LeafCardCount, 70, 360); },
            lod => { Assert.Equal(2, lod.Level); Assert.InRange(lod.LeafCardCount, 20, 140); });
        Assert.True(Triangles(tree.Lods[0]) > Triangles(tree.Lods[1]));
        Assert.True(Triangles(tree.Lods[1]) > Triangles(tree.Lods[2]));
        Assert.True(tree.Lods[0].LeafCardCount > tree.Lods[1].LeafCardCount);
        Assert.True(tree.Lods[1].LeafCardCount > tree.Lods[2].LeafCardCount);
    }

    [Fact]
    public void GenerateSingleLodMatchesFullTreeWithoutBuildingUnusedGeometry()
    {
        var settings = RekallAgeProceduralTreeSettings.TemperateOak(41);
        var full = RekallAgeProceduralTreeGenerator.Generate("single", "Single", settings);
        var middle = RekallAgeProceduralTreeGenerator.GenerateLod("single", "Single", settings, 1);

        Assert.Equal(JsonSerializer.Serialize(full.Lods[1], RekallAgeModelingJson.Options),
            JsonSerializer.Serialize(middle, RekallAgeModelingJson.Options));
    }

    [Fact]
    public void MidnightRiderConsumesTheGenericGeneratorInsteadOfItsCartoonPrivateGenerator()
    {
        var root = FindRepositoryRoot();
        var module = File.ReadAllText(Path.Combine(root, "Examples", "MidnightRider", "Modules", "MidnightRiderRules", "MidnightRiderRulesModule.cs"));

        Assert.Contains("RekallAgeProceduralTreeGenerator.Generate", module);
        Assert.False(File.Exists(Path.Combine(root, "Examples", "MidnightRider", "Modules", "MidnightRiderRules", "ProceduralTreeGenerator.cs")));
    }

    private static int Triangles(RekallAgeGeneratedTreeLod lod) =>
        lod.Bark.Topology.FaceIds.Count * 2 + lod.Foliage.Topology.FaceIds.Count * 2;

    private static IReadOnlyList<JsonElement> Attribute(RekallAgeMeshAsset mesh, string name) =>
        Assert.Single(mesh.Attributes, attribute => attribute.Name == name).Values;

    private static (double X, double Y) ReadFloat2(JsonElement element) =>
        (element[0].GetDouble(), element[1].GetDouble());

    private static double MaxRadius(IEnumerable<RekallAgeGeometryVector3> points) =>
        points.Select(point => Math.Sqrt(point.X * point.X + point.Z * point.Z)).DefaultIfEmpty().Max();

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", ".."));
}
