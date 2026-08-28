namespace TerraRuntime.World;

/// <summary>
/// Plans the section windows sent by Terraria 1.4.5.8 when handling the initial packet-8 request.
/// The base spawn window and optional requested/team windows intentionally use different vanilla bounds.
/// </summary>
public static class InitialSectionBootstrapPlanner
{
    public const int MaximumBaseSectionCount = 5 * 3;
    public const int MaximumRequestedSectionCount = 6 * 4;
    public const int MaximumTeamSpawnSectionCount = 6 * 4;

    public static int PlanBaseSpawnSections(
        WorldDimensions dimensions,
        int spawnTileX,
        int spawnTileY,
        Span<WorldSectionId> destination)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if ((uint)spawnTileX >= (uint)dimensions.WidthTiles)
            throw new ArgumentOutOfRangeException(nameof(spawnTileX));
        if ((uint)spawnTileY >= (uint)dimensions.HeightTiles)
            throw new ArgumentOutOfRangeException(nameof(spawnTileY));
        if (destination.Length < MaximumBaseSectionCount)
            throw new ArgumentException($"Destination must hold at least {MaximumBaseSectionCount} sections.", nameof(destination));

        int sectionX = spawnTileX / TerrariaSectionGeometry.WidthTiles;
        int sectionY = spawnTileY / TerrariaSectionGeometry.HeightTiles;

        int rawStartX = sectionX - 2;
        int rawStartY = sectionY - 1;
        int endXExclusive = rawStartX + 5;
        int endYExclusive = rawStartY + 3;

        int startX = Math.Max(0, rawStartX);
        int startY = Math.Max(0, rawStartY);
        endXExclusive = Math.Min(endXExclusive, dimensions.SectionColumns);
        endYExclusive = Math.Min(endYExclusive, dimensions.SectionRows);

        int count = 0;
        for (int x = startX; x < endXExclusive; x++)
        {
            for (int y = startY; y < endYExclusive; y++)
            {
                destination[count++] = new WorldSectionId(x, y);
            }
        }

        return count;
    }

    /// <summary>
    /// Plans the optional packet-8 window around the client-provided spawn position.
    /// Terraria 1.4.5.8 uses inclusive upper bounds here, producing up to a 6x4 window.
    /// </summary>
    public static int PlanRequestedSections(
        WorldDimensions dimensions,
        int tileX,
        int tileY,
        Span<WorldSectionId> destination)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if (destination.Length < MaximumRequestedSectionCount)
            throw new ArgumentException($"Destination must hold at least {MaximumRequestedSectionCount} sections.", nameof(destination));

        if (!HasValidRequestedPosition(dimensions, tileX, tileY))
            return 0;

        return PlanInclusiveAdditionalWindow(dimensions, tileX, tileY, destination);
    }

    public static int PlanTeamSpawnSections(
        WorldDimensions dimensions,
        int tileX,
        int tileY,
        Span<WorldSectionId> destination)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if (destination.Length < MaximumTeamSpawnSectionCount)
            throw new ArgumentException($"Destination must hold at least {MaximumTeamSpawnSectionCount} sections.", nameof(destination));
        if ((uint)tileX >= (uint)dimensions.WidthTiles || (uint)tileY >= (uint)dimensions.HeightTiles)
            return 0;

        return PlanInclusiveAdditionalWindow(dimensions, tileX, tileY, destination);
    }

    public static bool HasValidRequestedPosition(WorldDimensions dimensions, int tileX, int tileY)
    {
        ArgumentNullException.ThrowIfNull(dimensions);

        if (tileX == -1 || tileY == -1)
            return false;

        return tileX >= 10 &&
            tileX <= dimensions.WidthTiles - 10 &&
            tileY >= 10 &&
            tileY <= dimensions.HeightTiles - 10;
    }

    private static int PlanInclusiveAdditionalWindow(
        WorldDimensions dimensions,
        int tileX,
        int tileY,
        Span<WorldSectionId> destination)
    {
        int startX = (tileX / TerrariaSectionGeometry.WidthTiles) - 2;
        int startY = (tileY / TerrariaSectionGeometry.HeightTiles) - 1;
        int endXInclusive = startX + 5;
        int endYInclusive = startY + 3;

        startX = Math.Max(0, startX);
        startY = Math.Max(0, startY);
        endXInclusive = Math.Min(endXInclusive, dimensions.SectionColumns - 1);
        endYInclusive = Math.Min(endYInclusive, dimensions.SectionRows - 1);

        int count = 0;
        for (int x = startX; x <= endXInclusive; x++)
        {
            for (int y = startY; y <= endYInclusive; y++)
            {
                destination[count++] = new WorldSectionId(x, y);
            }
        }

        return count;
    }
}
