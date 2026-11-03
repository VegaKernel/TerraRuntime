using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>WorldGen.CheckMan/CheckWoman1.4.5.8 retain packed equipment hundreds and orientation36.</summary>
    private void LegacyCheckMannequin(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        int frame = observed.FrameX >= 100 ? observed.FrameX % 100 : observed.FrameX;
        int column = frame >= 36 ? frame % 36 : frame;
        int left = i - column / 18;
        int top = j - observed.FrameY / 18;
        if (!Contains(left, top) || !Contains(left + 1, top + 3))
            return;
        bool broken = false;
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 3; y++)
        {
            WorldTile part = At(left + x, top + y);
            int local = part.FrameX >= 100 ? part.FrameX % 100 : part.FrameX;
            // The source strips all hundreds, but only ONE orientation36 band here.
            if (local >= 36) local -= 36;
            broken |= !part.IsActive || part.Type != type || part.FrameY != y * 18 || local != x * 18;
        }
        broken |= !SolidTileAllowBottomSlope(left, top + 3) || !SolidTileAllowBottomSlope(left + 1, top + 3);
        if (!broken)
            return;
        destroyingObject = true;
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 3; y++)
            if (At(left + x, top + y).IsActive && At(left + x, top + y).Type == type)
                KillTile(left + x, top + y);
        destroyingObject = false;
    }

    /// <summary>CheckXmasTree171 uses cell-index frames, decorated origin metadata and a conjunctive member check.</summary>
    private void LegacyCheckChristmasTree(int i, int j)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        int left = observed.FrameX < 10 ? i - observed.FrameX : i;
        int top = observed.FrameX < 10 ? j - observed.FrameY : j;
        if (!Contains(left, top) || !Contains(left + 3, top + 8))
            return;
        bool broken = false;
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                WorldTile part = At(left + x, top + y);
                broken |= !part.IsActive || part.Type != 171 ||
                    x != 0 && y != 0 && part.FrameX != x && part.FrameY != y;
            }
            if (x is 1 or 2 && !LegacyChristmasFloor(At(left + x, top + 8)))
                broken = true;
        }
        if (!broken)
            return;
        destroyingObject = true;
        for (int x = 0; x < 4; x++)
        for (int y = 0; y < 8; y++)
            if (At(left + x, top + y).Type == 171)
                KillTile(left + x, top + y);
        destroyingObject = false;
    }

    private bool LegacyChristmasFloor(WorldTile tile) => tile.IsActive && !tile.IsActuated &&
        IsFramingSolid(tile.TileType) && !tile.IsHalfBrick &&
        (tile.SourceSlope == 0 || VanillaTileIds.IsPlatform(tile.TileType) && tile.Shape is 2 or 3);

    /// <summary>CheckGnome567 validates types and frameY0/20, deliberately not active or matching frameX.</summary>
    private void LegacyCheckGnome(int x, int j)
    {
        if (destroyingObject)
            return;
        int top = At(x, j).FrameY > 0 ? j - 1 : j;
        if (!Contains(x, top) || !Contains(x, top + 2))
            return;
        WorldTile upper = At(x, top), lower = At(x, top + 1);
        bool broken = upper.Type != 567 || lower.Type != 567 || upper.FrameY != 0 || lower.FrameY != 20 ||
            !SolidTileAllowBottomSlope(x, top + 2) && !VanillaTileIds.IsPlatform(At(x, top + 2).TileType);
        if (!broken)
            return;
        destroyingObject = true;
        if (upper.Type == 567) KillTile(x, top);
        if (lower.Type == 567) KillTile(x, top + 1);
        destroyingObject = false;
    }

    /// <summary>CheckAnchor723/724 and CheckStinkbugBlocker630/631 share source reorientation, not failure policy.</summary>
    private void LegacyCheckReorientingAnchor(int x, int y, ushort type)
    {
        // Neither original caller checks destroyObject: neighbouring break framing still reaches these.
        WorldTile observed = At(x, y);
        int prior = observed.FrameX / 18;
        int style = prior;
        bool forceWall = type is 723 or 724;
        if (!LegacyTryAnchorStyle(x, y, observed.Wall, ref style, forceWall))
        {
            if (!forceWall) KillTile(x, y);
            return;
        }
        // Stinkbug comparison flips2/3 but retains the computed style, including pixel normalization.
        int comparison = forceWall ? prior : prior switch { 2 => 3, 3 => 2, _ => prior };
        if (style != comparison)
            At(x, y).FrameX = (short)(style * 18);
    }

    private bool LegacyTryAnchorStyle(int x, int y, int wall, ref int style, bool forceWall)
    {
        bool Fits(int direction) => direction switch
        {
            0 => SolidTileAllowBottomSlope(x, y + 1) || LegacyTreeAnchor(x, y + 1),
            1 => SolidTileAllowTopSlope(x, y - 1) || LegacyTreeAnchor(x, y - 1),
            2 => LegacySideAnchor(x - 1, y, leftSupport: true) || LegacyTreeAnchor(x - 1, y),
            3 => LegacySideAnchor(x + 1, y, leftSupport: false) || LegacyTreeAnchor(x + 1, y),
            4 => wall > 0,
            _ => true // Original validation only handles styles0..4; other values stay unchanged.
        };
        if (style is >= 0 and <= 4 && !Fits(style))
            style = -1;
        if (style == -1)
        {
            for (int direction = 0; direction < 4; direction++)
                if (Fits(direction)) { style = direction; break; }
            if (style == -1 && (wall > 0 || forceWall)) style = 4;
        }
        return style > -1;
    }

    private bool LegacySideAnchor(int x, int y, bool leftSupport)
    {
        if (!Contains(x, y))
            return false;
        WorldTile tile = At(x, y);
        bool forbiddenSlope = leftSupport ? tile.Shape is 2 or 4 : tile.Shape is 3 or 5;
        return tile.IsActive && !tile.IsActuated && !tile.IsHalfBrick &&
            IsFramingSolid(tile.TileType) && !VanillaTileIds.IsPlatform(tile.TileType) && !forbiddenSlope;
    }

    private bool LegacyTreeAnchor(int x, int y)
    {
        if (!Contains(x, y))
            return true;
        WorldTile tile = At(x, y);
        if (!tile.IsActive || tile.Type is not (5 or 72 or 323 or 583 or 584 or 585 or 586 or 587 or 588 or 589 or 596 or 616 or 634))
            return false;
        bool leafy = tile.Type == 323 && tile.FrameX >= 88 || tile.FrameX == 22 && tile.FrameY is >= 198 and <= 242;
        if (leafy)
            return false;
        if (tile.Type == 323)
            return true;
        bool branch = tile.FrameX == 44 && tile.FrameY is 198 or 220 or 242 ||
            tile.FrameX == 66 && tile.FrameY is 0 or 22 or 44 or 198 or 220 or 242 ||
            tile.FrameX == 88 && tile.FrameY is 66 or 88 or 110;
        return !branch;
    }
}
