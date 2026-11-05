using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Frame-validated vanity-tree trunk, branch and root part of TerrariaServer.exe 1.4.5.8
/// <c>WorldGen.TryConvertingOrKillingTreesAboveIfTheyWouldBecomeInvalid</c> and
/// <c>WorldGen.ConvertTreeAndGround</c>.
/// </summary>
internal sealed class GenerationWorldTreeConversion1458(WorldTileStore store)
{
    internal readonly record struct Plan(int Height, bool ConvertToOrdinary, ushort TreeType);

    internal Plan Prepare(int floorX, int floorY, ushort newFloorType)
    {
        if (floorX < 5 || floorY < 5 || floorX >= store.Dimensions.WidthTiles - 5 ||
            floorY >= store.Dimensions.HeightTiles - 5)
            throw new NotSupportedException("Tree conversion needs the source five-cell interior.");
        WorldTile floor = At(floorX, floorY);
        WorldTile root = At(floorX, floorY - 1);
        if (!floor.IsActive || floor.Type != 2 || !root.IsActive || root.Type is not (596 or 616) ||
            newFloorType is not (23 or 109 or 199))
            throw new NotSupportedException("This tree/floor pair needs another ConvertTreeAndGround profile.");
        if (IsCentralBranchOrRoot(root) || root.FrameX < 0 || root.FrameY < 0 ||
            root.FrameX % 22 != 0 || root.FrameY % 22 != 0)
            throw new NotSupportedException("This tree origin is not an admitted source trunk frame.");
        ushort treeType = root.Type;
        int height = 0;
        for (int y = floorY - 1; y >= 5 && height < 100; y--)
        {
            WorldTile trunk = At(floorX, y);
            if (!trunk.IsActive || trunk.Type != treeType)
                break;
            if (trunk.FrameX < 0 || trunk.FrameY < 0 ||
                trunk.FrameX % 22 != 0 || trunk.FrameY % 22 != 0 ||
                (y == floorY - 2 && IsCentralBranch(trunk)) ||
                !IsLeftOwnedArm(At(floorX - 1, y), treeType) ||
                !IsRightOwnedArm(At(floorX + 1, y), treeType))
                throw new NotSupportedException("Branch/root frame conversion needs the full source tree owner.");
            height++;
        }
        if (height == 0 || floorY - height < 5 ||
            At(floorX, floorY - height - 1).IsActive)
            throw new NotSupportedException("The bounded vanity trunk has an unqualified top or height.");

        // VanityTreeGroundTest accepts Hallow grass109 but rejects corruption23
        // and crimson199. Source ConvertTreeAndGround changes those trunks to5.
        return new Plan(height, newFloorType is 23 or 199, treeType);
    }

    internal void Apply(int floorX, int floorY, ushort newFloorType, Plan plan)
    {
        if (!plan.ConvertToOrdinary)
            return;
        for (int y = floorY - 1; y >= floorY - plan.Height; y--)
        {
            At(floorX, y).Type = 5;
            if (At(floorX - 1, y).IsActive && At(floorX - 1, y).Type == plan.TreeType)
                At(floorX - 1, y).Type = 5;
            if (At(floorX + 1, y).IsActive && At(floorX + 1, y).Type == plan.TreeType)
                At(floorX + 1, y).Type = 5;
        }
        // ConvertTreeAndGround publishes the new ground before the caller's
        // Convert_ActuallyConvertTile writes that same type and frames its halo.
        At(floorX, floorY).Type = newFloorType;
    }

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

    private static bool IsLeftOwnedArm(in WorldTile tile, ushort treeType) =>
        !tile.IsActive || tile.Type == treeType &&
        (tile.FrameX == 44 && (tile.FrameY is 132 or 154 or 176 or 198 or 220 or 242) ||
         tile.FrameX == 66 && (tile.FrameY is 0 or 22 or 44));

    private static bool IsRightOwnedArm(in WorldTile tile, ushort treeType) =>
        !tile.IsActive || tile.Type == treeType &&
        (tile.FrameX == 22 && (tile.FrameY is 132 or 154 or 176) ||
         tile.FrameX == 66 && (tile.FrameY is 198 or 220 or 242) ||
         tile.FrameX == 88 && (tile.FrameY is 66 or 88 or 110));

    private static bool IsCentralBranch(in WorldTile tile) =>
        tile.FrameX == 44 && (tile.FrameY is 198 or 220 or 242) ||
        tile.FrameX == 66 && (tile.FrameY is 0 or 22 or 44 or 198 or 220 or 242) ||
        tile.FrameX == 88 && (tile.FrameY is 66 or 88 or 110);

    private static bool IsCentralBranchOrRoot(in WorldTile tile) =>
        IsCentralBranch(tile) ||
        (tile.FrameX is 22 or 44) && (tile.FrameY is 132 or 154 or 176);
}
