using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal static partial class TreeGrower1458
{
    internal static bool TryGrowEpic(WorldTileStore store, int x, int checkedY, IWorldGenerationVanillaRandom random, GenerationTileFraming1458 framing)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        if (x < 1 || x >= width - 1 || checkedY < 1 || checkedY >= height) return false;
        int floor = checkedY;
        while (floor < height && At(x, floor) is { IsActive: true, Type: 20 }) floor++;
        if (floor >= height) return false;
        WorldTile ground = At(x, floor), above = At(x, floor - 1);
        if (!ground.IsActive || ground.IsHalfBrick || ground.SourceSlope != 0 || ground.Type != 2 || above.Wall != 0 || above.LiquidAmount != 0 || (!EpicGround(At(x - 1, floor)) && !EpicGround(At(x + 1, floor)))) return false;
        if (!IsReplaceableRectangle(store.Tiles, width, height, x - 2, x + 2, floor - 55, floor - 1)) return false;
        int length = random.Next(20, 30), top = floor - length; bool previousLeft = false, previousRight = false;
        for (int row = top; row < floor; row++)
        {
            int variant = random.Next(3); var feature = (TreeSegmentFeature1458)random.Next(10);
            if (row == top || row == floor - 1) feature = TreeSegmentFeature1458.Straight;
            while (HasLeftBranch(feature) && previousLeft || HasRightBranch(feature) && previousRight) feature = (TreeSegmentFeature1458)random.Next(10);
            previousLeft = HasLeftBranch(feature); previousRight = HasRightBranch(feature);
            EpicWrite(ref At(x, row), TreeFrameCatalog1458.Trunk(feature, variant));
            if (previousLeft) { variant = random.Next(3); EpicWrite(ref At(x - 1, row), TreeFrameCatalog1458.LeftBranch(random.Next(3) < 2, variant)); }
            if (previousRight) { variant = random.Next(3); EpicWrite(ref At(x + 1, row), TreeFrameCatalog1458.RightBranch(random.Next(3) < 2, variant)); }
        }
        var shape = (TreeRootShape1458)random.Next(3);
        WorldTile left = At(x - 1, floor), right = At(x + 1, floor);
        NormalizeRootShape(ref shape, EpicGround(left) && !left.IsHalfBrick && left.SourceSlope == 0, EpicGround(right) && !right.IsHalfBrick && right.SourceSlope == 0);
        if (shape is TreeRootShape1458.Both or TreeRootShape1458.Right) EpicWrite(ref At(x + 1, floor - 1), TreeFrameCatalog1458.RightRoot(random.Next(3)));
        if (shape is TreeRootShape1458.Both or TreeRootShape1458.Left) EpicWrite(ref At(x - 1, floor - 1), TreeFrameCatalog1458.LeftRoot(random.Next(3)));
        int baseVariant = random.Next(3); if (TreeFrameCatalog1458.TryGetTrunkBase(shape, baseVariant, out var basis)) SetTreeFrame(ref At(x, floor - 1), basis);
        bool leafy = random.Next(13) != 0; SetTreeFrame(ref At(x, top), TreeFrameCatalog1458.Top(leafy, random.Next(3)));
        framing.RangeFrame(x - 2, top - 1, x + 2, floor + 1);
        return true;
        ref WorldTile At(int column, int row) => ref store.Tiles[store.GetUncheckedIndex(column, row)];
    }
    private static bool EpicGround(in WorldTile tile) => tile.IsActive && tile.Type is 2 or 23 or 60 or 109;
    private static void EpicWrite(ref WorldTile tile, TreeFrame1458 frame) { tile.Flags |= WorldTileFlags.Active; tile.Type = 5; SetTreeFrame(ref tile, frame); }
}
