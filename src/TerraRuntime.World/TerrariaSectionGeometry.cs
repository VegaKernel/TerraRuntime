namespace TerraRuntime.World;

/// <summary>
/// Terraria's network visibility section geometry. This is deliberately separate from any future
/// persistence or in-memory tile layout.
/// </summary>
public static class TerrariaSectionGeometry
{
    public const int WidthTiles = 200;
    public const int HeightTiles = 150;

    public static WorldSectionId FromTile(WorldDimensions dimensions, int tileX, int tileY)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ValidateTile(dimensions, tileX, tileY);
        return new WorldSectionId(tileX / WidthTiles, tileY / HeightTiles);
    }

    public static WorldTileRegion GetBounds(WorldDimensions dimensions, WorldSectionId section)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ValidateSection(dimensions, section);

        int x = checked(section.X * WidthTiles);
        int y = checked(section.Y * HeightTiles);
        int width = Math.Min(WidthTiles, dimensions.WidthTiles - x);
        int height = Math.Min(HeightTiles, dimensions.HeightTiles - y);
        return new WorldTileRegion(x, y, width, height);
    }

    public static int ToLinearIndex(WorldDimensions dimensions, WorldSectionId section)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ValidateSection(dimensions, section);
        return checked((section.Y * dimensions.SectionColumns) + section.X);
    }

    public static WorldSectionId FromLinearIndex(WorldDimensions dimensions, int index)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if ((uint)index >= (uint)dimensions.SectionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new WorldSectionId(index % dimensions.SectionColumns, index / dimensions.SectionColumns);
    }

    public static void ValidateSection(WorldDimensions dimensions, WorldSectionId section)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if ((uint)section.X >= (uint)dimensions.SectionColumns ||
            (uint)section.Y >= (uint)dimensions.SectionRows)
        {
            throw new ArgumentOutOfRangeException(nameof(section));
        }
    }

    private static void ValidateTile(WorldDimensions dimensions, int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)dimensions.WidthTiles)
        {
            throw new ArgumentOutOfRangeException(nameof(tileX));
        }

        if ((uint)tileY >= (uint)dimensions.HeightTiles)
        {
            throw new ArgumentOutOfRangeException(nameof(tileY));
        }
    }
}
