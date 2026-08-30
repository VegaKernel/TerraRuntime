using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 packet-17 Dirt compatibility facade. The strict isolated-Dirt acceptance
/// rules remain here because they are packet/gameplay proof, while the actual storage mutation is delegated to the
/// shared semantic <see cref="VanillaWorldTileMutationService"/> boundary used by future tile/wall operations.
/// </summary>
public static class VanillaDirtPlacement
{
    public static bool TryPlaceOnEmpty(WorldTileStore tiles, int x, int y)
    {
        if (!CanPlaceOnEmpty(tiles, x, y))
            return false;

        var service = new VanillaWorldTileMutationService(tiles);
        var request = new WorldTileMutationRequest(
            WorldTileMutationKind.PlaceTile,
            x,
            y,
            TileType: VanillaTileIds.Dirt);
        return service.Apply(in request).Applied;
    }

    /// <summary>
    /// Preflights the strict canonical-empty state accepted by Dirt placement. Production packet handling uses this
    /// check before committing through its long-lived <see cref="VanillaWorldTileMutationService"/> instance.
    /// </summary>
    public static bool CanPlaceOnEmpty(WorldTileStore tiles, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (!Contains(tiles, x, y))
            return false;

        WorldTile current = tiles.Get(x, y);
        return IsCompletelyEmpty(in current);
    }

    /// <summary>
    /// Preflights the same isolated canonical Dirt subset accepted by <see cref="TryKillIsolatedWithoutDrop"/>
    /// without mutating the world. The authoritative game thread may use this before reserving cross-subsystem
    /// resources; single-writer ownership keeps the preflight stable until the following commit attempt.
    /// </summary>
    public static bool CanKillIsolated(WorldTileStore tiles, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        if (x <= 0 || y <= 0 || x >= tiles.Dimensions.WidthTiles - 1 || y >= tiles.Dimensions.HeightTiles - 1)
            return false;

        WorldTile current = tiles.Get(x, y);
        if (!IsCanonicalDirt(in current))
            return false;

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                    continue;

                if (tiles.Get(x + offsetX, y + offsetY).IsActive)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Implements the no-item packet-17 KillTile subset for an isolated canonical Dirt tile. Cross-subsystem drop
    /// reservation remains outside this helper; the committed storage transition goes through the shared mutation
    /// service so section revisions, dirtiness and simple-frame canonicalization follow one path.
    /// </summary>
    public static bool TryKillIsolatedWithoutDrop(WorldTileStore tiles, int x, int y)
    {
        if (!CanKillIsolated(tiles, x, y))
            return false;

        var service = new VanillaWorldTileMutationService(tiles);
        var request = new WorldTileMutationRequest(WorldTileMutationKind.KillTile, x, y);
        return service.Apply(in request).Applied;
    }

    private static bool Contains(WorldTileStore tiles, int x, int y) =>
        (uint)x < (uint)tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)tiles.Dimensions.HeightTiles;

    private static bool IsCanonicalDirt(in WorldTile tile) =>
        tile.Type == VanillaTileIds.Dirt.Value &&
        tile.WallType == VanillaWallIds.None &&
        tile.FrameX == 0 &&
        tile.FrameY == 0 &&
        tile.Flags == WorldTileFlags.Active &&
        tile.LiquidAmount == 0 &&
        tile.TileColor == 0 &&
        tile.WallColor == 0 &&
        tile.Shape == 0 &&
        tile.LiquidKind == WorldLiquidKind.Water &&
        tile.Reserved == 0;

    private static bool IsCompletelyEmpty(in WorldTile tile) =>
        tile.TileType == VanillaTileIds.Dirt &&
        tile.WallType == VanillaWallIds.None &&
        IsEmptyFramePair(tile.FrameX, tile.FrameY) &&
        tile.Flags == WorldTileFlags.None &&
        tile.LiquidAmount == 0 &&
        tile.TileColor == 0 &&
        tile.WallColor == 0 &&
        tile.Shape == 0 &&
        tile.LiquidKind == WorldLiquidKind.Water &&
        tile.Reserved == 0;

    private static bool IsEmptyFramePair(short frameX, short frameY) =>
        (frameX == 0 && frameY == 0) ||
        (frameX == -1 && frameY == -1);
}
