using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private void PotCheckFraming(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile origin = At(i, j);
        int column = origin.FrameX / 18;
        while (column > 1)
            column -= 2;
        int row = origin.FrameY / 18;
        int style = 0;
        while (row > 1)
        {
            row -= 2;
            style++;
        }
        int left = i - column, top = j - row;
        bool broken = false;
        for (int x = left; x < left + 2; x++)
        {
            for (int y = top; y < top + 2; y++)
            {
                WorldTile member = At(x, y);
                int memberColumn = member.FrameX / 18;
                while (memberColumn > 1)
                    memberColumn -= 2;
                broken |= !member.IsActive || member.Type != type || memberColumn != x - left ||
                    member.FrameY != (y - top) * 18 + style * 36;
            }
            broken |= type == 653 ? !SolidTileAllowBottomSlope(x, top + 2) : !PotHasOrdinaryFloor(x, top + 2);
        }
        if (!broken)
            return;
        destroyingObject = true;
        for (int x = left; x < left + 2; x++)
        for (int y = top; y < top + 2; y++)
            if (At(x, y).IsActive && At(x, y).Type == type)
                KillTile(x, y);

        // Gore and loot owners return during generation. Arguments to NewGore are
        // evaluated first, so these style-specific choices still move genRand.
        if (style is >= 25 and <= 27)
        {
            random.Next(217, 220);
            random.Next(217, 220);
        }
        else if (style is >= 28 and <= 30)
        {
            random.Next(315, 317);
            random.Next(315, 317);
        }
        else if (style is >= 31 and <= 33)
        {
            int pieces = random.Next(2, 5);
            for (int piece = 0; piece < pieces; piece++)
                random.Next(6);
        }
        destroyingObject = false;
    }

    // WorldGen.SolidTile2(int,int), including its platform top-slope exception.
    private bool PotHasOrdinaryFloor(int x, int y)
    {
        WorldTile floor = At(x, y);
        return floor.IsActive && !floor.IsActuated && !floor.IsHalfBrick &&
            IsFramingSolid(floor.TileType) &&
            (floor.Shape == 0 || VanillaTileIds.IsPlatform(floor.TileType) && floor.Shape is 2 or 3);
    }

    private void OasisCheckFraming(int i, int j)
    {
        if (destroyingObject)
            return;
        WorldTile origin = At(i, j);
        int top = j - origin.FrameY % 36 / 18;
        int column = origin.FrameX / 18, style = 0;
        while (column > 2)
        {
            column -= 3;
            style++;
        }
        int left = i - column;
        bool broken = false;
        for (int x = left; x < left + 3; x++)
        {
            for (int y = top; y < top + 2; y++)
            {
                WorldTile member = At(x, y);
                broken |= !member.IsActive || member.Type != 530 ||
                    member.FrameX != (x - left) * 18 + style * 54 || member.FrameY != (y - top) * 18;
            }
            broken |= !SolidTile(x, top + 2) || !WorldSmoothingCatalog1458.IsSandConversion(At(x, top + 2).TileType);
        }
        if (!broken)
            return;
        destroyingObject = true;
        for (int x = left; x < left + 3; x++)
        for (int y = top; y < top + 2; y++)
            if (At(x, y).IsActive && At(x, y).Type == 530)
                KillTile(x, y);
        destroyingObject = false;
        for (int x = left - 1; x < left + 4; x++)
        for (int y = top - 1; y < top + 3; y++)
            TileFrame(x, y);
    }

    private void CoralCheckFraming(int i, int j)
    {
        WorldTile floor = At(i, j + 1);
        if (At(i, j - 1).IsActive || !floor.IsActive ||
            !IsFramingSolid(floor.TileType) || floor.IsHalfBrick || floor.Shape is 2 or 3)
            KillTile(i, j);
    }
}
