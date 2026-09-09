using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary generation-time GrowTreeWithSettings(Tree_Ash), TerrariaServer 1.4.5.8.
/// Shares the verified atlas, not GrowTree's different clearance/root algorithm.</summary>
internal static class AshTreeGrower1458
{
    internal const ushort AshGrass = 633, AshTree = 634;

    public static bool TryGrow(WorldTileStore store, int x, int checkedY, IWorldGenerationVanillaRandom random)
    {
        int height = store.Dimensions.HeightTiles;
        if (x < 2 || x >= store.Dimensions.WidthTiles - 2 || checkedY < 1 || checkedY >= height) return false;
        int floor = checkedY;
        while (floor < height && At(store, x, checkedY).IsActive && At(store, x, floor).Type == 20) floor++;
        if (floor >= height) return false;
        for (int dx = -1; dx <= 1; dx++) if (At(store, x + dx, floor - 1).LiquidAmount != 0) return false;
        WorldTile ground = At(store, x, floor);
        if (!ground.IsActive || ground.IsActuated || ground.Shape != 0 || ground.Type != AshGrass ||
            !TreeGrowthCatalog1458.AllowsPlantGrowth(At(store, x, floor - 1).WallType) ||
            (!IsAshGround(At(store, x - 1, floor)) && !IsAshGround(At(store, x + 1, floor)))) return false;
        int treeHeight = random.Next(7, 13);
        int top = floor - treeHeight;
        if (top - 4 < 0) return false;
        for (int column = x - 2; column <= x + 2; column++)
        for (int y = top - 4; y < floor; y++)
            if (At(store, column, y) is { IsActive: true } tile && !TreeGrowthCatalog1458.IsReplaceableGrowthTile(tile.TileType)) return false;

        bool previousLeft = false, previousRight = false;
        for (int y = top; y < floor; y++)
        {
            int variant = random.Next(3);
            var feature = (TreeSegmentFeature1458)random.Next(10);
            if (y == top || y == floor - 1) feature = TreeSegmentFeature1458.Straight;
            int retries = 0;
            while ((HasLeft(feature) && previousLeft) || (HasRight(feature) && previousRight))
            {
                if (++retries > 100_000) throw new InvalidOperationException("Ash-tree branch search exhausted its safety budget.");
                feature = (TreeSegmentFeature1458)random.Next(10);
            }
            previousLeft = HasLeft(feature); previousRight = HasRight(feature);
            Write(ref At(store, x, y), ground, TreeFrameCatalog1458.Trunk(feature, variant));
            if (previousLeft)
            {
                variant = random.Next(3);
                Write(ref At(store, x - 1, y), ground, TreeFrameCatalog1458.LeftBranch(random.Next(3) < 2, variant));
            }
            if (previousRight)
            {
                variant = random.Next(3);
                Write(ref At(store, x + 1, y), ground, TreeFrameCatalog1458.RightBranch(random.Next(3) < 2, variant));
            }
        }
        // Settings trees test the generic IsTileTypeFitForTree at roots, not just their profile's ground type.
        // Both independent Next(3) draws occur even when a side has no eligible ground.
        bool left = IsRootGround(At(store, x - 1, floor));
        bool right = IsRootGround(At(store, x + 1, floor));
        if (random.Next(3) == 0) left = false;
        if (random.Next(3) == 0) right = false;
        if (right) Write(ref At(store, x + 1, floor - 1), ground, TreeFrameCatalog1458.RightRoot(random.Next(3)));
        if (left) Write(ref At(store, x - 1, floor - 1), ground, TreeFrameCatalog1458.LeftRoot(random.Next(3)));
        int baseVariant = random.Next(3);
        if (left || right)
            Write(ref At(store, x, floor - 1), ground, TreeFrameCatalog1458.SettingsTrunkBase(left, right, baseVariant));
        bool leafy = random.Next(13) != 0;
        Write(ref At(store, x, top), ground, TreeFrameCatalog1458.Top(leafy, random.Next(3)));
        FrameFreshRoots(store, x, floor, random);
        return true;
    }

    private static void FrameFreshRoots(WorldTileStore store, int x, int floor, IWorldGenerationVanillaRandom random)
    {
        // GrowTreeWithSettings finishes with RangeFrame -> CheckTreeWithSettings. Generic
        // root eligibility above is NOT the final AshTreeGroundTest (exactly633). This bounded
        // slice frames the newly grown roots only, not arbitrary pre-existing tree destruction.
        int row = floor - 1;
        for (int side = -1; side <= 1; side += 2)
        {
            int column = x + side;
            ref WorldTile root = ref At(store, column, row);
            if (!CanFrame(store, column, row) || !root.IsActive || root.Type != AshTree ||
                root.FrameX is not (22 or 44) || root.FrameY is < 132 or > 176 ||
                IsAshGround(At(store, column, floor))) continue;
            // KillTile still evaluates ten dust choices before generation suppresses particles.
            for (int dust = 0; dust < 10; dust++) { _ = random.Next(10); _ = random.Next(12); }
            bool hasTreeAbove = At(store, column, row - 1) is { IsActive: true, Type: AshTree };
            root.Type = 0; root.FrameX = root.FrameY = -1; root.Shape = root.TileColor = 0;
            root.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive |
                WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            // Generation KillTile's neighbouring inactive-cell TileFrame cleanup.
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                ref WorldTile neighbour = ref At(store, column + dx, row + dy);
                if (neighbour.IsActive) continue;
                neighbour.Shape = neighbour.TileColor = 0;
                neighbour.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            }
            // CheckTreeWithSettings continues after KillTile using captured neighbour types;
            // its final crown branch writes these frames even though the root is now inactive.
            if (!hasTreeAbove)
            {
                root.FrameX = 154;
                root.FrameY = (short)(side < 0 ? 65 : -1);
            }
        }
        ref WorldTile basis = ref At(store, x, row);
        if (!CanFrame(store, x, row) || basis.FrameY is < 132 or > 176 ||
            basis.FrameX is not (0 or 66 or 88)) return;
        bool left = At(store, x - 1, row) is { IsActive: true, Type: AshTree };
        bool right = At(store, x + 1, row) is { IsActive: true, Type: AshTree };
        basis.FrameX = (short)(left ? right ? 88 : 66 : 0);
        if (!left && !right) basis.FrameY %= 66;
    }

    private static bool CanFrame(WorldTileStore store, int x, int y) => x > 5 && y > 5 &&
        x < store.Dimensions.WidthTiles - 5 && y < store.Dimensions.HeightTiles - 5;

    private static bool IsAshGround(in WorldTile tile) => tile.IsActive && tile.Type == AshGrass;
    private static bool IsRootGround(in WorldTile tile) => tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
        TreeGrowthCatalog1458.IsTreeGround(tile.TileType);
    private static bool HasLeft(TreeSegmentFeature1458 feature) => feature is TreeSegmentFeature1458.LeftBranch or TreeSegmentFeature1458.BothBranches;
    private static bool HasRight(TreeSegmentFeature1458 feature) => feature is TreeSegmentFeature1458.RightBranch or TreeSegmentFeature1458.BothBranches;
    private static void Write(ref WorldTile tile, in WorldTile ground, TreeFrame1458 frame)
    {
        const WorldTileFlags coating = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
        tile.Flags = (tile.Flags & ~coating) | WorldTileFlags.Active | (ground.Flags & coating);
        tile.Type = AshTree; tile.TileColor = ground.TileColor; tile.FrameX = frame.X; tile.FrameY = frame.Y;
    }
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
