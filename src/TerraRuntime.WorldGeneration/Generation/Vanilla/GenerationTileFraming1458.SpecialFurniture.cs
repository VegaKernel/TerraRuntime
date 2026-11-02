using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>
    /// TerrariaServer1.4.5.8 CheckSuper for fishing crates, geysers and antlion larvae.
    /// Their TileObjectData records are statically known: width2, horizontal styles,
    /// height1 for geysers and2 otherwise. BeeHive444 has an unowned projectile effect.
    /// </summary>
    private void CheckSuperFurniture(int i, int j, ushort type)
    {
        if (destroyingObject || type is not (376 or 443 or 485))
            return;
        WorldTile observed = At(i, j);
        int height = type == 443 ? 1 : 2;
        int style = observed.FrameX / 36;
        int left = i - observed.FrameX / 18 % 2;
        int top = j - observed.FrameY / 18 % height;
        if (!Contains(left - 1, top - 1) || !Contains(left + 2, top + height))
            return;

        bool broken = false;
        bool floor = true;
        bool ceiling = true;
        for (int column = 0; column < 2; column++)
        {
            for (int row = 0; row < height; row++)
            {
                WorldTile member = At(left + column, top + row);
                broken |= !member.IsActive || member.Type != type ||
                    member.FrameX != style * 36 + column * 18 || member.FrameY != row * 18;
            }
            WorldTile below = At(left + column, top + height);
            if (type == 376)
            {
                // CheckSuper's crate arm reads active, not nactive, and rejects halfbrick only.
                broken |= !below.IsActive || below.IsHalfBrick ||
                    !VanillaTileCollisionCatalog.IsSolid(below.TileType) && !GenerationFurnitureTiles1458.IsTable(below.Type);
            }
            else if (type == 485)
                broken |= !SpecialFlatAnchor(below);
            else
            {
                floor &= SpecialGeyserFloor(below);
                ceiling &= SpecialSolidBottom(At(left + column, top - 1));
            }
        }
        if (type == 443)
        {
            broken |= !floor && !ceiling;
            if (!broken)
            {
                // Re-evaluated on each TileFrame invocation, preserving the source's integer rounding.
                int normalized = floor ? style / 2 * 36 : (style - 2) / 2 * 36 + 72;
                for (int column = 0; column < 2; column++)
                    At(left + column, top).FrameX = (short)(normalized + column * 18);
            }
        }
        if (!broken)
            return;

        destroyingObject = true;
        for (int column = 0; column < 2; column++)
        for (int row = 0; row < height; row++)
            if (At(left + column, top + row).IsActive && At(left + column, top + row).Type == type)
                KillTile(left + column, top + row);
        // Item.NewItem and the type485 NPC creation are suppressed during generation.
        destroyingObject = false;
        for (int x = left - 1; x <= left + 2; x++)
        for (int y = top - 1; y <= top + height; y++)
            TileFrame(x, y);
    }

    private static bool SpecialFlatAnchor(WorldTile tile) => tile.IsActive && !tile.IsActuated &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) &&
        !GenerationObjectSupport1458.DisallowsAttachment(tile.Type) && tile.Shape == 0 && !tile.IsHalfBrick;

    private static bool SpecialSolidBottom(WorldTile tile)
    {
        if (!tile.IsActive || tile.IsActuated || tile.Type is 10 or 387 or 388 || tile.Shape is 4 or 5)
            return false;
        bool topSlope = tile.Shape is 2 or 3;
        return tile.IsHalfBrick || topSlope || VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            (!VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) ||
             VanillaTileIds.IsPlatform(tile.TileType) && (tile.IsHalfBrick || topSlope));
    }

    private static bool SpecialGeyserFloor(WorldTile tile)
    {
        if (!tile.IsActive || tile.IsActuated)
            return false;
        if (SpecialFlatAnchor(tile))
            return true;
        if (VanillaTileIds.IsPlatform(tile.TileType))
        {
            int frame = tile.FrameX / 18;
            // AnchorValid checks halfbrick only for its first platform frame band.
            if ((!tile.IsHalfBrick && frame is >= 0 and <= 7) || frame is >= 12 and <= 16 or >= 25 and <= 26)
                return true;
        }
        else if (VanillaTileCollisionCatalog.IsSolid(tile.TileType) && VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))
            return true;
        return !tile.IsHalfBrick && tile.Shape is 4 or 5 && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }

    /// <summary>Source CheckBanner91: exact three-row style and SolidBottom/platform/planter ceiling.</summary>
    private void CheckBanner(int x, int y)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(x, y);
        int styleY = observed.FrameY / 54 * 54;
        int top = y - observed.FrameY / 18 % 3;
        if (!Contains(x, top - 1) || !Contains(x, top + 2))
            return;
        bool broken = false;
        for (int row = 0; row < 3; row++)
        {
            WorldTile member = At(x, top + row);
            broken |= !member.IsActive || member.Type != 91 || member.FrameX != observed.FrameX ||
                member.FrameY != styleY + row * 18;
        }
        WorldTile ceiling = At(x, top - 1);
        bool shape = ceiling.IsHalfBrick || ceiling.Shape is 2 or 3;
        // CheckBanner differs from AnchorValid(SolidBottom): solid-top filtering
        // also applies to the independent halfbrick/top-slope acceptance here.
        bool normalAnchor = (shape || VanillaTileCollisionCatalog.IsSolid(ceiling.TileType)) &&
            (!VanillaTileCollisionCatalog.IsSolidTop(ceiling.TileType) || VanillaTileIds.IsPlatform(ceiling.TileType) && shape) &&
            ceiling.Type is not (10 or 387 or 388) && ceiling.Shape is not (4 or 5);
        bool anchored = ceiling.IsActive && !ceiling.IsActuated &&
            (normalAnchor || VanillaTileIds.IsPlatform(ceiling.TileType) || ceiling.Type == 380);
        if (!broken && anchored)
            return;
        destroyingObject = true;
        for (int row = 0; row < 3; row++)
            if (At(x, top + row).Type == 91)
                KillTile(x, top + row);
        destroyingObject = false;
    }

    /// <summary>Source CheckChand454; suppressed item creation still spends its reward-selector RNG.</summary>
    private void CheckPigronata(int i, int j)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        int left = i - observed.FrameX / 18 % 4;
        int top = j - observed.FrameY / 18 % 3;
        int styleX = observed.FrameX / 72 * 72;
        int styleY = observed.FrameY / 54 * 54;
        if (!Contains(left - 1, top - 1) || !Contains(left + 4, top + 3))
            return;
        bool broken = false;
        for (int column = 0; column < 4; column++)
        for (int row = 0; row < 3; row++)
        {
            WorldTile member = At(left + column, top + row);
            broken |= !member.IsActive || member.Type != 454 || member.FrameX != styleX + column * 18 ||
                member.FrameY != styleY + row * 18;
        }
        WorldTile ceiling = At(left + 1, top - 1);
        broken |= !ceiling.IsActive || ceiling.IsActuated || !VanillaTileCollisionCatalog.IsSolid(ceiling.TileType) ||
            VanillaTileCollisionCatalog.IsSolidTop(ceiling.TileType);
        if (!broken)
            return;
        destroyingObject = true;
        for (int column = 0; column < 4; column++)
        for (int row = 0; row < 3; row++)
            if (At(left + column, top + row).IsActive && At(left + column, top + row).Type == 454)
                KillTile(left + column, top + row);
        SpecialPigronataRewardRandom();
        destroyingObject = false;
        for (int x = left - 1; x <= left + 4; x++)
        for (int y = top - 1; y <= top + 3; y++)
            TileFrame(x, y);
    }

    private void SpecialPigronataRewardRandom()
    {
        int reward = random.Next(9);
        if (reward is >= 2 and <= 4)
            SpecialSpendCoinStacks(random.Next(10, 31), 2, 11);
        else if (reward is 5 or 6)
            SpecialSpendCoinStacks(random.Next(60, 80), 3, 16);
        else if (reward == 7)
            SpecialSpendCoinStacks(random.Next(10, 31), 2, 9);
        else if (reward == 8)
        {
            SpecialSpendCoinStacks(100, 3, 16);
            SpecialSpendCoinStacks(random.Next(30, 91), 7, 14);
        }
    }

    private void SpecialSpendCoinStacks(int remaining, int minimum, int maximum)
    {
        while (remaining > 0)
            remaining -= Math.Min(remaining, random.Next(minimum, maximum));
    }
}
