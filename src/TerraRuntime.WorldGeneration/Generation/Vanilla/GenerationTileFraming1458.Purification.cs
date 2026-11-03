using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal bool UsesPostShimmerSolidity { get; set; }
    private GenerationWallFraming1458? purificationWalls;

    /// <summary>Ordinary purification, the type-zero Convert brush used by ShimmerCleanUp.</summary>
    internal void Purify(int x, int y, double surface, double rock)
    {
        for (int column = x - 3; column <= x + 3; column++)
        for (int row = y - 3; row <= y + 3; row++)
            if (column >= 1 && row >= 1 && column < width - 1 && row < height - 1 &&
                Math.Abs(column - x) + Math.Abs(row - y) < 6)
                PurifyCell(column, row, surface, rock);
    }

    private void PurifyCell(int x, int y, double surface, double rock)
    {
        ref WorldTile tile = ref At(x, y);
        ushort identity = tile.Type, wall = tile.Wall;
        if (!tile.IsActive && wall == 0) return;
        ushort nextWall = wall switch
        {
            69 or 70 or 81 => y < surface ? random.Next(10) == 0 ? (ushort)65 : (ushort)63 : (ushort)64,
            262 => 61, 274 => 185, 292 => 204, 293 => 205, 294 => 206, 295 => 207,
            61 or 185 or 204 or 205 or 206 or 207 => wall,
            1 or 3 or 28 or 83 or 246 or 248 or 269 => 349,
            188 or 192 or 200 or 212 or 276 or 280 or 288 or 300 => 212,
            189 or 193 or 201 or 213 or 277 or 281 or 289 or 301 => 213,
            190 or 194 or 202 or 214 or 278 or 282 or 290 or 302 => 214,
            191 or 195 or 203 or 215 or 279 or 283 or 291 or 303 => 215,
            80 => y < surface + 4 + random.Next(3) || y > (height + rock) / 2 - 3 + random.Next(3) ? (ushort)15 : (ushort)64,
            217 or 219 or 218 or 304 or 305 or 307 or 306 => 216,
            220 or 222 or 221 or 275 or 308 or 310 or 309 => 187,
            _ => wall
        };
        if (wall != 0 && wall != nextWall)
        {
            tile.Wall = nextWall;
            (purificationWalls ??= new GenerationWallFraming1458(store, random)).SquareWallFrame(x, y);
        }
        if (identity == 4 && tile.IsActive && tile.FrameY / 22 is 18 or 19 or 20 or 22)
        {
            tile.FrameY = (short)(tile.FrameY / 22 == 22 ? 21 * 22 : 0);
            SquareTileFrame(x, y);
            return;
        }
        ushort nextType = identity switch
        {
            492 => 477, 661 or 662 or 70 => 60, 23 or 109 or 199 => 2,
            25 or 117 or 203 => 1, 112 or 116 or 234 => 53,
            398 or 402 or 399 => 397, 400 or 403 or 401 => 396, 163 or 164 or 200 => 161,
            _ => identity
        };
        if (nextType != identity && tile.IsActive)
        {
            PurifyTreeAbove(x, y, nextType);
            tile.Type = nextType;
            SquareTileFrame(x, y);
        }
        else if (identity is 32 or 352) KillTile(x, y);
    }

    private static bool PurifyForestGround(int type) =>
        type is 2 or 23 or 199 or 109 or 477 or 492 or 60 or 661 or 662 or 147 or 70;

    private void PurifyTreeAbove(int x, int floor, ushort newGround)
    {
        if (x < 2 || floor < 2 || x >= width - 2 || floor >= height - 2 || !At(x, floor).IsActive)
            return;
        WorldTile tree = At(x, floor - 1);
        if (!tree.IsActive || !(BreakabilityTreeType(tree.Type) || tree.Type == 323)) return;
        bool valid = tree.Type switch
        {
            5 => PurifyForestGround(newGround), 323 => newGround is 53 or 112 or 116 or 234,
            >= 583 and <= 589 => SettingsTreeGrower1458.IsGemTreeGround(new TileTypeId(newGround)),
            596 or 616 => newGround is 2 or 109 or 477 or 492,
            634 => newGround == 633,
            _ => true
        };
        if (valid) return;
        ushort replacement = 0;
        if (tree.Type is 596 or 616 or 323 && PurifyForestGround(newGround)) replacement = 5;
        else if (tree.Type is 596 or 616 or 5 && newGround is 53 or 112 or 116 or 234) replacement = 323;
        if (replacement != 0) PurifyConvertTree(x, floor - 1, newGround, tree.Type, replacement);
        else KillTile(x, floor - 1);
    }

    private int PurifyBranchOffset(int x, int y, bool includeRoots = true)
    {
        if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2) return 0;
        WorldTile t = At(x, y);
        if (!t.IsActive || !BreakabilityTreeType(t.Type)) return 0;
        if (t.FrameX == 44 && t.FrameY is 198 or 220 or 242 || t.FrameX == 66 && t.FrameY is 0 or 22 or 44) return 1;
        if (t.FrameX == 66 && t.FrameY is 198 or 220 or 242 || t.FrameX == 88 && t.FrameY is 66 or 88 or 110) return -1;
        if (includeRoots && t.FrameY is 132 or 154 or 176)
            return t.FrameX == 44 ? 1 : t.FrameX == 22 ? -1 : 0;
        return 0;
    }

    private void PurifyConvertTree(int x, int y, ushort floor, ushort oldTree, ushort newTree)
    {
        if (x < 5 || y < 5 || x >= width - 5 || y >= height - 5 || !At(x, y + 1).IsActive) return;
        int heightAbove = 1;
        for (int budget = 100; budget > 0; budget--)
        {
            int row = y - 1 - heightAbove;
            if (row < 5 || row >= height - 5) break;
            WorldTile next = At(x, row);
            if (!next.IsActive || !(BreakabilityTreeType(next.Type) || next.Type == 323) ||
                PurifyBranchOffset(x, y - 1, includeRoots: false) != 0) break;
            heightAbove++;
        }
        if (PurifyBranchOffset(x, y) != 0) { KillTile(x, y); return; }
        void Branches(int row, bool remove)
        {
            for (int side = -1; side <= 1; side += 2)
                if (PurifyBranchOffset(x + side, row) == -side)
                {
                    if (remove) KillTile(x + side, row);
                    else At(x + side, row).Type = newTree;
                }
        }
        if (newTree == 323)
            for (int offset = 0; offset <= heightAbove; offset++)
                if (At(x, y - offset).IsActive) Branches(y - offset, remove: true);
        for (int offset = 0; offset <= heightAbove; offset++)
        {
            ref WorldTile t = ref At(x, y - offset);
            if (!t.IsActive) continue;
            t.Type = newTree;
            if (newTree == 323)
            {
                t.FrameX = (short)(offset == 0 ? 66 : 22 * (offset == heightAbove ? random.Next(4, 7) : random.Next(0, 3)));
                t.FrameY = 0;
            }
            else if (oldTree != 323) Branches(y - offset, remove: false);
            else if (offset == 0) { t.FrameX = 0; t.FrameY = (short)(22 * random.Next(0, 3)); }
            else if (offset == heightAbove) { t.FrameX = 22; t.FrameY = (short)(22 * random.Next(9, 12)); }
            else
            {
                if (random.Next(2) == 0) { t.FrameX = 44; t.FrameY = (short)(22 * random.Next(3, 6)); }
                else { t.FrameX = (short)(random.Next(2) == 0 ? 22 : 0); t.FrameY = (short)(22 * random.Next(0, 6)); }
            }
        }
        At(x, y + 1).Type = floor;
    }
}
