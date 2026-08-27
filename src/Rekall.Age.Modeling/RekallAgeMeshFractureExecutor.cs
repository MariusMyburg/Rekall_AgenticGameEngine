using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

/// <summary>
/// Wraps the built-in static Voronoi-style fracture algorithm (<see cref="RekallAgeMeshFracture"/>,
/// untouched) as the default, and dispatches to a project-registered
/// <see cref="IRekallAgeFractureAlgorithmPlugin"/> by id otherwise.
/// </summary>
public sealed class RekallAgeMeshFractureExecutor
{
    public const string BuiltInVoronoiAlgorithmId = "rekall.fracture.voronoi";

    private readonly IReadOnlyList<IRekallAgeFractureAlgorithmPlugin> _plugins;

    public RekallAgeMeshFractureExecutor(IReadOnlyList<IRekallAgeFractureAlgorithmPlugin>? plugins = null)
    {
        _plugins = plugins ?? [];
    }

    public IReadOnlyList<RekallAgeMeshAsset> Fracture(
        RekallAgeMeshAsset source,
        int chunkCount,
        long seed,
        string? algorithmId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (algorithmId is null || algorithmId == BuiltInVoronoiAlgorithmId)
        {
            return RekallAgeMeshFracture.Fracture(source, chunkCount, seed);
        }

        var plugin = _plugins.FirstOrDefault(item => item.AlgorithmId == algorithmId)
            ?? throw new ArgumentException(
                $"Unknown fracture algorithm '{algorithmId}'.", nameof(algorithmId));
        return plugin.Fracture(source, chunkCount, seed);
    }
}
