using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Deliberately narrow authoritative subset of TerrariaServer 1.4.5.8 WorldGen.PlaceTile for Dirt.
/// The runtime accepts only a completely empty normalized target. Replacement, liquids, tile-cut handling,
/// breakability and other WorldGen branches remain outside this slice instead of being approximated.
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

    private static bool IsCompletelyEmpty(in WorldTile tile) =>
        tile.Type == 0 &&
        tile.Wall == 0 &&
        tile.FrameX == 0 &&
        tile.FrameY == 0 &&
        tile.Flags == WorldTileFlags.None &&
        tile.LiquidAmount == 0 &&
        tile.TileColor == 0 &&
        tile.WallColor == 0 &&
        tile.Shape == 0 &&
        tile.LiquidKind == WorldLiquidKind.Water &&
        tile.Reserved == 0;
}
