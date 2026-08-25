using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeUvPacker
{
    public IReadOnlyDictionary<int, RekallAgeGeometryVector2> Pack(
        IReadOnlyDictionary<int, RekallAgeGeometryVector2> source,
        IReadOnlyList<IReadOnlyList<int>> islands,
        double margin)
    {
        if (!double.IsFinite(margin) || margin < 0 || margin >= 0.25) throw new ArgumentOutOfRangeException(nameof(margin));
        if (islands.Count == 0) return new Dictionary<int, RekallAgeGeometryVector2>();
        var columns = (int)Math.Ceiling(Math.Sqrt(islands.Count));
        var rows = (int)Math.Ceiling(islands.Count / (double)columns);
        var output = new Dictionary<int, RekallAgeGeometryVector2>();
        for (var islandIndex = 0; islandIndex < islands.Count; islandIndex++)
        {
            var corners = islands[islandIndex];
            var minU = corners.Min(corner => source[corner].X); var maxU = corners.Max(corner => source[corner].X);
            var minV = corners.Min(corner => source[corner].Y); var maxV = corners.Max(corner => source[corner].Y);
            var width = maxU - minU; var height = maxV - minV;
            if (width <= 1e-12 && height <= 1e-12) throw new InvalidDataException($"UV island {islandIndex} is degenerate.");
            var cellX = islandIndex % columns; var cellY = islandIndex / columns;
            var cellWidth = 1d / columns; var cellHeight = 1d / rows;
            var innerWidth = cellWidth - 2 * margin; var innerHeight = cellHeight - 2 * margin;
            if (innerWidth <= 0 || innerHeight <= 0) throw new InvalidDataException($"UV margin {margin} leaves no usable space for {islands.Count} islands.");
            var normalization = Math.Max(width, height);
            var scale = Math.Min(innerWidth, innerHeight) / normalization;
            var usedWidth = width * scale; var usedHeight = height * scale;
            var originU = cellX * cellWidth + margin + (innerWidth - usedWidth) * 0.5;
            var originV = cellY * cellHeight + margin + (innerHeight - usedHeight) * 0.5;
            foreach (var corner in corners)
                output[corner] = new(originU + (source[corner].X - minU) * scale, originV + (source[corner].Y - minV) * scale);
        }
        return output;
    }
}
