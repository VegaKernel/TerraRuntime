using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Deliberately narrow authoritative subset of TerrariaServer 1.4.5.8 WorldGen tile mutation for Dirt.
/// Placement accepts only a completely empty normalized target. Destruction accepts only the canonical Dirt state
/// produced by this class and an inactive eight-neighbor ring, so the source-verified SquareTileFrame call has no
/// active neighbor whose state TerraRuntime would need to reproduce. Replacement, attachments and general drops remain
/// outside this slice instead of being approximated.
/// </summary>
public static class VanillaDirtPlacement
{
    public static bool TryPlaceOnEmpty(WorldTileStore tiles, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        WorldTile current = tiles.Get(x, y);
        if (!IsCompletelyEmpty(in current))
            return false;

        WorldTile placed = default;
        if (!placed.TrySetTileType(VanillaTileIds.Dirt))
            throw new InvalidOperationException("Verified Dirt tile id no longer fits the runtime tile ABI.");

        placed.Flags = WorldTileFlags.Active;
        tiles.Set(x, y, in placed);
        return true;
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
    /// Implements the no-item packet-17 KillTile subset for an isolated canonical Dirt tile.
    /// TerrariaServer 1.4.5.8 reaches the ordinary successful KillTile tail for this state: Dirt is not a
    /// CheckTileBreakability2 survivor, while inactive immediate neighbors avoid the attachment/locked-door
    /// early-return branches of CheckTileBreakability. The runtime canonicalizes the resulting inactive tile to
    /// default storage; vanilla's frame -1 values are not gameplay-visible for an inactive tile.
    /// </summary>
    public static bool TryKillIsolatedWithoutDrop(WorldTileStore tiles, int x, int y)
    {
        if (!CanKillIsolated(tiles, x, y))
            return false;

        WorldTile cleared = default;
        tiles.Set(x, y, in cleared);
        return true;
    }

    private static bool IsCanonicalDirt(in WorldTile tile) =>
        tile.Type == VanillaTileIds.Dirt.Value &&
        tile.Wall == 0 &&
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
        tile.Type == 0 &&
        tile.Wall == 0 &&
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
