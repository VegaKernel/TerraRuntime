using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// TerrariaServer 1.4.5.8 Wall of Flesh death brick-box mutation. This is authoritative world state:
/// empty perimeter tiles become Demonite/Crimtane Brick and all cells in the square are drained.
/// Network and persistence consumers observe the mutation through <see cref="WorldTileStore.Set"/> dirty tracking.
/// </summary>
public static class VanillaWallOfFleshDeathWorldMutation
{
    public static int Apply(
        WorldTileStore tiles,
        float npcPositionX,
        float npcPositionY,
        int npcWidth,
        int npcHeight,
        bool crimson)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (!float.IsFinite(npcPositionX) || !float.IsFinite(npcPositionY) || npcWidth <= 0 || npcHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcPositionX));

        int centerX = (int)(npcPositionX + npcWidth * 0.5f) / 16;
        int centerY = (int)(npcPositionY + npcHeight * 0.5f) / 16;
        int radius = npcWidth / 2 / 16 + 1;
        TileTypeId brick = crimson ? VanillaTileIds.CrimtaneBrick : VanillaTileIds.DemoniteBrick;
        int changed = 0;

        int minX = Math.Max(0, centerX - radius);
        int maxX = Math.Min(tiles.Dimensions.WidthTiles - 1, centerX + radius);
        int minY = Math.Max(0, centerY - radius);
        int maxY = Math.Min(tiles.Dimensions.HeightTiles - 1, centerY + radius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                WorldTile before = tiles.Get(x, y);
                WorldTile after = before;
                bool perimeter = x == centerX - radius || x == centerX + radius ||
                                 y == centerY - radius || y == centerY + radius;

                if (perimeter && !after.IsActive)
                {
                    if (!after.TrySetTileType(brick) || !after.TrySetFlags(WorldTileFlags.Active, enabled: true))
                        throw new InvalidOperationException("Wall of Flesh brick-box tile mutation could not encode the source-backed tile identity.");
                }

                after.LiquidAmount = 0;
                if (after.LiquidKind == WorldLiquidKind.Lava)
                    after.LiquidKind = WorldLiquidKind.Water;

                if (!TilesEqual(in before, in after))
                {
                    tiles.Set(x, y, in after);
                    changed++;
                }
            }
        }

        return changed;
    }

    private static bool TilesEqual(in WorldTile left, in WorldTile right) =>
        left.Type == right.Type && left.Wall == right.Wall && left.FrameX == right.FrameX && left.FrameY == right.FrameY &&
        left.Flags == right.Flags && left.LiquidAmount == right.LiquidAmount && left.TileColor == right.TileColor &&
        left.WallColor == right.WallColor && left.Shape == right.Shape && left.LiquidKind == right.LiquidKind;
}
