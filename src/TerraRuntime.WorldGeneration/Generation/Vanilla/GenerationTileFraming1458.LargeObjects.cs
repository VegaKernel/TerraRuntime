using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>TerrariaServer1.4.5.8 Check6x3 for all 41 critter-cage identities.</summary>
    private void CheckCritterCageSixByThree(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        // Cages have no tile-local style bands: the complete frame is the footprint coordinate.
        int left = i - observed.FrameX / 18;
        int top = j - observed.FrameY / 18;
        if (!Contains(left - 1, top - 1) || !Contains(left + 6, top + 3))
            return;
        bool broken = !HasLargeObjectFrames(left, top, 6, 3, type, 0, 0, backingWall: false);
        for (int x = left; x < left + 6; x++)
        {
            WorldTile floor = At(x, top + 3);
            // This second source arm is deliberately independent of shape and platform frameX.
            // An active/non-actuated solid-top with frameY0 can support even a halfbrick cage floor.
            if (!SolidTileAllowBottomSlope(x, top + 3) &&
                (!floor.IsActive || floor.IsActuated ||
                 !VanillaTileCollisionCatalog.IsSolidTop(floor.TileType) || floor.FrameY != 0))
                broken = true;
        }
        if (!broken)
            return;
        RemoveLargeFramedObject(left, top, 6, 3, type);
        // Unlike the painting validators, Check6x3 re-enters neighbour validation after destruction.
        for (int x = left - 1; x < left + 7; x++)
        for (int y = top - 1; y < top + 4; y++)
            TileFrame(x, y);
    }

    /// <summary>Check2x3Wall/3x2Wall/4x3Wall/6x4Wall, with their distinct frame atlas axes.</summary>
    private void CheckRectangularWallObject(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        (int objectWidth, int objectHeight) = type switch
        {
            245 => (2, 3),
            246 => (3, 2),
            241 => (4, 3),
            242 => (6, 4),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        int column = observed.FrameX / 18;
        int row = observed.FrameY / 18;
        int left;
        int top;
        int styleX = 0;
        int styleY = 0;
        if (type == 245)
        {
            styleX = column >= 2 ? column / 2 * 36 : 0;
            left = i - column + styleX / 18;
            top = j - row;
        }
        else if (type is 246 or 241)
        {
            styleY = row >= objectHeight ? row / objectHeight * objectHeight * 18 : 0;
            left = i - column;
            top = j - row + styleY / 18;
        }
        else
        {
            // Check6x4Wall packs exactly 27 vertical style rows into each horizontal atlas column.
            const int StyleRowsPerColumn = 27;
            int style = StyleRowsPerColumn * (column / 6) + (row >> 2);
            left = i - column % 6;
            top = j - row % 4;
            styleX = style / StyleRowsPerColumn * 108;
            styleY = style % StyleRowsPerColumn * 72;
        }
        if (!Contains(left, top) || !Contains(left + objectWidth - 1, top + objectHeight - 1) ||
            HasLargeObjectFrames(left, top, objectWidth, objectHeight, type, styleX, styleY, backingWall: true))
            return;
        // All four wall families lower destroyObject and return; none has a post-removal halo.
        RemoveLargeFramedObject(left, top, objectWidth, objectHeight, type);
    }

    private bool HasLargeObjectFrames(int left, int top, int objectWidth, int objectHeight,
        ushort type, int styleX, int styleY, bool backingWall)
    {
        for (int x = left; x < left + objectWidth; x++)
        for (int y = top; y < top + objectHeight; y++)
        {
            WorldTile part = At(x, y);
            if (!part.IsActive || part.Type != type ||
                part.FrameX != styleX + (x - left) * 18 || part.FrameY != styleY + (y - top) * 18 ||
                backingWall && part.Wall == 0)
                return false;
        }
        return true;
    }

    private void RemoveLargeFramedObject(int left, int top, int objectWidth, int objectHeight, ushort type)
    {
        destroyingObject = true;
        for (int x = left; x < left + objectWidth; x++)
        for (int y = top; y < top + objectHeight; y++)
            if (At(x, y).IsActive && At(x, y).Type == type)
                KillTile(x, y);
        destroyingObject = false;
    }
}
