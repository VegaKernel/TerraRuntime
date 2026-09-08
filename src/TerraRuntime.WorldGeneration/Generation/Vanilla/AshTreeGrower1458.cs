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
        return true;
    }

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
