using RuntimeWorldTileBounds = TerraRuntime.Contracts.Runtime.WorldTileBounds;

namespace TerraRuntime.World;

/// <summary>
/// Immutable tile dimensions plus derived network-section geometry. No tile storage layout is implied.
/// </summary>
public sealed class WorldDimensions
{
    public WorldDimensions(int widthTiles, int heightTiles)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(widthTiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(heightTiles, 1);

        WidthTiles = widthTiles;
        HeightTiles = heightTiles;
        SectionColumns = ((widthTiles - 1) / TerrariaSectionGeometry.WidthTiles) + 1;
        SectionRows = ((heightTiles - 1) / TerrariaSectionGeometry.HeightTiles) + 1;

        long sectionCount = (long)SectionColumns * SectionRows;
        if (sectionCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(widthTiles), "World dimensions produce too many addressable sections.");
        }

        SectionCount = (int)sectionCount;
    }

    public int WidthTiles { get; }

    public int HeightTiles { get; }

    public int SectionColumns { get; }

    public int SectionRows { get; }

    public int SectionCount { get; }

    public static implicit operator RuntimeWorldTileBounds(WorldDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        return new RuntimeWorldTileBounds(dimensions.WidthTiles, dimensions.HeightTiles);
    }
}
