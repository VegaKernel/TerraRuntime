using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal static partial class TreeGrower1458
{
    internal static bool TryGrowUnderground(WorldTileStore store, int x, int groundY, IWorldGenerationVanillaRandom random,
        GenerationTileFraming1458 framing, GenerationWallFraming1458 walls, int heightAddon = 0)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        if (x < 1 || x >= width - 1 || groundY < 1 || groundY >= height) return false;
        ref WorldTile ground = ref store.Tiles[store.GetUncheckedIndex(x, groundY)];
        if (!ground.IsActive || ground.IsActuated || ground.IsHalfBrick || ground.SourceSlope != 0 || ground.Type != 60) return false;
        WorldTile left = store.Get(x - 1, groundY), right = store.Get(x + 1, groundY);
        if ((!left.IsActive || left.Type != 60) && (!right.IsActive || right.Type != 60)) return false;
        int size = random.Next(5, 15) + heightAddon;
        if (groundY - size - 7 < 0) return false;
        for (int column = x - 1; column <= x + 1; column++)
            for (int row = groundY - size - 7; row < groundY; row++)
            { WorldTile tile = store.Get(column, row); if (tile.IsActive && !TreeGrowthCatalog1458.IsReplaceableGrowthTile(tile.TileType)) return false; }
        bool previousLeft = false, previousRight = false;
        for (int row = groundY - size; row < groundY; row++)
        {
            int variant = random.Next(3);
            var feature = (TreeSegmentFeature1458)random.Next(10);
            if (row == groundY - 1 || row == groundY - size) feature = TreeSegmentFeature1458.Straight;
            while (HasLeftBranch(feature) && previousLeft || HasRightBranch(feature) && previousRight)
                feature = (TreeSegmentFeature1458)random.Next(10);
            previousLeft = HasLeftBranch(feature); previousRight = HasRightBranch(feature);
            WriteUnderground(store, x, row, TreeFrameCatalog1458.Trunk(feature, variant));
            if (previousLeft) { variant = random.Next(3); bool leafy = random.Next(3) < 2; WriteUnderground(store, x - 1, row, TreeFrameCatalog1458.LeftBranch(leafy, variant)); }
            if (previousRight) { variant = random.Next(3); bool leafy = random.Next(3) < 2; WriteUnderground(store, x + 1, row, TreeFrameCatalog1458.RightBranch(leafy, variant)); }
        }
        var root = (TreeRootShape1458)random.Next(3);
        NormalizeRootShape(ref root, UndergroundRootGround(left), UndergroundRootGround(right));
        if (root is TreeRootShape1458.Both or TreeRootShape1458.Right) WriteUnderground(store, x + 1, groundY - 1, TreeFrameCatalog1458.RightRoot(random.Next(3)));
        if (root is TreeRootShape1458.Both or TreeRootShape1458.Left) WriteUnderground(store, x - 1, groundY - 1, TreeFrameCatalog1458.LeftRoot(random.Next(3)));
        int baseVariant = random.Next(3);
        if (TreeFrameCatalog1458.TryGetTrunkBase(root, baseVariant, out TreeFrame1458 baseFrame))
            SetTreeFrame(ref store.Tiles[store.GetUncheckedIndex(x, groundY - 1)], baseFrame);
        bool top = random.Next(8) != 0; int topVariant = random.Next(3);
        SetTreeFrame(ref store.Tiles[store.GetUncheckedIndex(x, groundY - size)], TreeFrameCatalog1458.Top(top, topVariant));
        for (int column = x - 3; column <= x + 3; column++)
            for (int row = groundY - size - 2; row <= groundY + 2; row++)
            { framing.TileFrame(column, row); walls.WallFrame(column, row, false); }
        return true;
    }
    private static bool UndergroundRootGround(in WorldTile tile) => tile.IsActive && !tile.IsActuated && !tile.IsHalfBrick && tile.SourceSlope == 0 && tile.Type is 2 or 23 or 60 or 109 or 147;
    private static void WriteUnderground(WorldTileStore store, int x, int y, TreeFrame1458 frame)
    { ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)]; tile.Flags |= WorldTileFlags.Active; tile.Type = 5; SetTreeFrame(ref tile, frame); }
}
