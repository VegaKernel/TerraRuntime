using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>The ordinary FinalSeedCleanup epilogue also runs when every secret seed is disabled.</summary>
    internal void FinalSeedCleanup()
    {
        for (int x = 5; x < width - 5; x++)
        for (int y = 5; y < height - 5; y++)
        {
            WorldTile falling = At(x, y);
            if (!falling.IsActive || falling.Type is not (53 or 234 or 112 or 116 or 224 or 123 or 330 or 331 or 332 or 333 or 495))
                continue;
            WorldTile below = At(x, y + 1);
            if (below.IsActive && !below.IsActuated && IsFramingSolid(below.TileType) &&
                !VanillaTileCollisionCatalog.IsSolidTop(below.TileType))
                continue;
            ushort replacement = falling.Type switch
            {
                224 => 147,
                53 or 495 => 397,
                112 => 398,
                234 => 399,
                116 => 402,
                _ => 1
            };
            bool replaceSelf = (!At(x, y - 1).IsActive || CanRemoveTile(x, y - 1)) &&
                below.IsActive && !CanRemoveTile(x, y + 1);
            int targetY = replaceSelf ? y : y + 1;
            ushort wall = At(x, targetY).Wall;
            // Tile.ResetToType preserves only wall identity, clearing all three tile headers.
            At(x, targetY) = new WorldTile { Type = replacement, Wall = wall, Flags = WorldTileFlags.Active };
        }
    }

    /// <summary>Source CanKillTile; deliberately distinct from CheckTileBreakability's dust/failure result.</summary>
    internal bool CanRemoveTile(int x, int y)
    {
        if (!Contains(x, y))
            return false;
        WorldTile target = At(x, y);
        if (!target.IsActive || target.Wall == 350)
            return false;
        if (y > 0 && At(x, y - 1).IsActive)
        {
            WorldTile above = At(x, y - 1);
            if (above.Type != target.Type)
            {
                if (BreakabilityTreeType(above.Type) &&
                    !(above.FrameX == 66 && above.FrameY is >= 0 and <= 44) &&
                    !(above.FrameX == 88 && above.FrameY is >= 66 and <= 110) && above.FrameY < 198)
                    return false;
                if (above.Type == 323 && above.FrameX is 66 or 220)
                    return false;
                if (above.Type is 21 or 26 or 72 or 77 or 88 or 467 or 488)
                    return false;
                if (above.Type == 80 && above.FrameX / 18 is 0 or 1 or 4 or 5)
                    return false;
            }
        }
        if (GenerationObjectSupport1458.IsBoulder(target.Type) && BoulderHasProtectedObject(x, y, target))
            return false;
        if (DoorIsLocked(target))
            return false;
        if (target.Type == 235)
        {
            int left = x - target.FrameX % 54 / 18;
            for (int column = 0; column < 3; column++)
                if (Contains(left + column, y - 1) && At(left + column, y - 1).IsActive &&
                    BlocksRemoval(target.Type, At(left + column, y - 1), scanContainers: true))
                    return false;
        }
        if (workspace is not null && target.Type is 21 or 467 or 88)
        {
            int columns = target.Type == 88 ? 3 : 2;
            return workspace.CanDestroyGeneratedChest(x - target.FrameX / 18 % columns, y - target.FrameY / 18);
        }
        return true;
    }
}
