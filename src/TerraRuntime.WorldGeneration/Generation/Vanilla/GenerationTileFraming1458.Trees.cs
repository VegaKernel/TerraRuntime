using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private int WoodyActiveType(int x, int y) =>
        Contains(x, y) && At(x, y).IsActive ? At(x, y).Type : -1;

    /// <summary>The mushroom trunk's independent support and cap selection arm.</summary>
    private void FrameMushroomTrunk(int x, int y)
    {
        int below = WoodyActiveType(x, y + 1);
        int above = WoodyActiveType(x, y - 1);
        ref WorldTile tile = ref At(x, y);
        if (below is not (72 or 70)) KillTile(x, y);
        else if (above != 72 && tile.FrameX == 0)
        {
            tile.FrameX = 18;
            tile.FrameY = (short)(random.Next(3) * 18);
        }
    }

    /// <summary>Palm validation includes the source's cap draw even after a failed support kills the tile.</summary>
    private void FramePalmTrunk(int x, int y)
    {
        ref WorldTile tile = ref At(x, y);
        int identity = tile.Type, oldX = tile.FrameX, oldY = tile.FrameY;
        int above = WoodyActiveType(x, y - 1), below = WoodyActiveType(x, y + 1);
        bool sand = below is 53 or 234 or 116 or 112;
        if (!sand && below != identity) KillTile(x, y);
        if (tile.FrameX is 66 or 220 && !sand) KillTile(x, y);
        if (above != identity)
        {
            if (tile.FrameX <= 44) tile.FrameX = (short)(random.Next(7, 10) * 22);
            else if (tile.FrameX == 66) tile.FrameX = 220;
        }
        WoodyPropagate(x, y, oldX, oldY);
    }

    /// <summary>
    /// Ordinary and settings trees share their 22-pixel atlas. Ground classification and the
    /// ordinary validator's writes after killing a root remain distinct source decisions.
    /// </summary>
    private void FrameWoodyTrunk(int x, int y)
    {
        ref WorldTile tile = ref At(x, y);
        int identity = tile.Type, oldX = tile.FrameX, oldY = tile.FrameY;
        int below = WoodyActiveType(x, y + 1), above = WoodyActiveType(x, y - 1);
        bool left = WoodyActiveType(x - 1, y) == identity;
        bool right = WoodyActiveType(x + 1, y) == identity;
        bool ordinary = identity == 5;
        bool ground = identity switch
        {
            5 => below is 2 or 23 or 661 or 477 or 60 or 70 or 109 or 147 or 199 or 662 or 492,
            >= 583 and <= 589 => below >= 0 && SettingsTreeGrower1458.IsGemTreeGround(new TileTypeId(below)),
            596 or 616 => below is 2 or 109 or 477 or 492,
            634 => below == 633,
            _ => false
        };

        // Neighbours are captured before any recursive kill halo. Re-reading them after a kill
        // changes the source's continuation, including frames on inactive cells.
        if (!ground && below != identity && tile.FrameX is 0 or 22 or 44 && tile.FrameY <= 130)
            KillTile(x, y);

        if (tile.FrameX is >= 22 and <= 44 && tile.FrameY is >= 132 and <= 176)
        {
            if (!ground || !(tile.FrameX == 22 && left || tile.FrameX == 44 && right)) KillTile(x, y);
        }
        else if (WoodyJunction(tile.FrameX, tile.FrameY))
        {
            int variation = tile.FrameY % 66;
            tile.FrameX = (short)(left ? right ? 110 : 88 : right ? 66 : 0);
            tile.FrameY = (short)(variation + (right ? 66 : 0));
        }

        if (tile.FrameY is >= 132 and <= 176 && tile.FrameX is 0 or 66 or 88)
        {
            int variation = tile.FrameY % 66;
            if (!ground) KillTile(x, y);
            // CheckTree continues this atlas rewrite after KillTile. The settings variant
            // puts it in the successful-ground arm instead.
            if (ground || ordinary)
            {
                tile.FrameX = (short)(left ? right ? 88 : 66 : 0);
                tile.FrameY = (short)(variation + (left || right ? 132 : 0));
            }
        }

        bool leftBranch = tile.FrameX == 66 && tile.FrameY is 0 or 22 or 44 ||
                          tile.FrameX == 44 && tile.FrameY is 198 or 220 or 242;
        bool rightBranch = tile.FrameX == 88 && tile.FrameY is 66 or 88 or 110 ||
                           tile.FrameX == 66 && tile.FrameY is 198 or 220 or 242;
        if (leftBranch)
        {
            if (!right) KillTile(x, y);
        }
        else if (rightBranch)
        {
            if (!left) KillTile(x, y);
        }
        else if (below == -1) KillTile(x, y);
        else if (above != identity && tile.FrameY < 198 &&
                 (tile.FrameX is not (22 or 44) || tile.FrameY < 132))
        {
            int variation = tile.FrameY % 66;
            tile.FrameX = (short)(left || right ? below == identity ? 132 : 154 : 110);
            tile.FrameY = (short)(variation + (left ? right ? 132 : 0 : right ? 66 : 0));
        }
        WoodyPropagate(x, y, oldX, oldY);
    }

    private static bool WoodyJunction(int x, int y) =>
        x == 88 && y is >= 0 and <= 44 || x == 66 && y is >= 66 and <= 130 ||
        x == 110 && y is >= 66 and <= 110 || x == 132 && y is >= 0 and <= 176;

    private void WoodyPropagate(int x, int y, int oldX, int oldY)
    {
        ref WorldTile tile = ref At(x, y);
        if (tile.FrameX == oldX || tile.FrameY == oldY || oldX < 0 || oldY < 0) return;
        TileFrame(x - 1, y);
        TileFrame(x + 1, y);
        TileFrame(x, y - 1);
        TileFrame(x, y + 1);
    }
}
