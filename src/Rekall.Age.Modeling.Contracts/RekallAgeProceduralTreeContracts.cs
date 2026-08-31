namespace Rekall.Age.Modeling.Contracts;

public sealed record RekallAgeProceduralTreeSettings(
    int Seed,
    double Height,
    double TrunkRadius,
    double CrownStart,
    double CrownRadius,
    int PrimaryBranchCount,
    double ApicalDominance,
    double Tropism,
    double Droop,
    double Irregularity,
    int NearLeafBudget,
    int MidLeafBudget,
    int FarLeafBudget)
{
    public static RekallAgeProceduralTreeSettings TemperateOak(int seed) => new(
        seed, Height: 11.5, TrunkRadius: 0.48, CrownStart: 0.28,
        CrownRadius: 5.0, PrimaryBranchCount: 15, ApicalDominance: 0.42,
        Tropism: 0.22, Droop: 0.16, Irregularity: 0.24,
        NearLeafBudget: 420, MidLeafBudget: 160, FarLeafBudget: 56);
}

public sealed record RekallAgeGeneratedTreeLod(
    int Level,
    double MaximumDistance,
    RekallAgeMeshAsset Bark,
    RekallAgeMeshAsset Foliage,
    int BranchCount,
    int LeafCardCount);

public sealed record RekallAgeGeneratedTree(
    string AssetId,
    string Name,
    RekallAgeProceduralTreeSettings Settings,
    IReadOnlyList<RekallAgeGeneratedTreeLod> Lods);
