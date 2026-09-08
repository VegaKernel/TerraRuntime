using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary generation-time WorldGen.GrowPalmTree, TerrariaServer 1.4.5.8.
/// Placement density belongs to the caller; special seeds and runtime growth are not admitted here.</summary>
internal static class PalmTreeGrower1458
{
    internal const ushort PalmTile = 323;
    private const ushort SaplingTile = 20;
    private const int MaximumHeight = 20;

    public static bool TryGrow(WorldTileStore store, int x, int checkedY, IWorldGenerationVanillaRandom random)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random);
        int height = store.Dimensions.HeightTiles;
        if (x < 1 || x >= store.Dimensions.WidthTiles - 1 || checkedY < 1 || checkedY >= height)
            return false;

        int floor = checkedY;
        while (floor < height && store.Get(x, floor) is { IsActive: true, Type: SaplingTile })
            floor++;
        if (floor < MaximumHeight || floor >= height)
            return false;

        WorldTile ground = store.Get(x, floor);
        WorldTile above = store.Get(x, floor - 1);
        // GrowPalmTree admits all four sand types, but not slopes/half bricks or a wet base.
        if (!ground.IsActive || ground.Shape != 0 || ground.Type is not (53 or 234 or 116 or 112) ||
            above.LiquidAmount != 0 || !TreeGrowthCatalog1458.AllowsPlantGrowth(above.WallType) ||
            !IsClear(store, x, x, floor - 2, floor - 1) ||
            !IsClear(store, x - 1, x + 1, floor - MaximumHeight, floor - 3))
            return false;

        int trunkHeight = random.Next(10, 21);
        int desiredBend = random.Next(-8, 9) * 2;
        short bend = 0;
        const WorldTileFlags coatingMask = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
        for (int segment = 0; segment < trunkHeight; segment++)
        {
            short frameX;
            if (segment == 0)
                frameX = 66;
            else if (segment == trunkHeight - 1)
                frameX = checked((short)(22 * random.Next(4, 7)));
            else
            {
                double fraction = (double)segment / trunkHeight;
                if (bend != desiredBend && fraction >= .25)
                {
                    ConsumeBendRolls(random, fraction);
                    bend += checked((short)(Math.Sign(desiredBend) * 2));
                }
                frameX = checked((short)(22 * random.Next(0, 3)));
            }

            ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, floor - 1 - segment)];
            tile.Type = PalmTile;
            tile.Flags = (tile.Flags & ~coatingMask) | WorldTileFlags.Active | (ground.Flags & coatingMask);
            tile.TileColor = ground.TileColor;
            tile.FrameX = frameX;
            tile.FrameY = bend;
        }
        return true;
    }

    private static void ConsumeBendRolls(IWorldGenerationVanillaRandom random, double fraction)
    {
        // The source condition ends in "|| true": bending is unconditional after the first quarter,
        // but preceding short-circuit RNG calls still advance genRand and affect all later passes.
        if (fraction < .5 && random.Next(13) == 0)
            return;
        if (fraction < .7 && random.Next(9) == 0)
            return;
        if (fraction < .95)
            _ = random.Next(5);
    }

    private static bool IsClear(WorldTileStore store, int left, int right, int top, int bottom)
    {
        for (int x = left; x <= right; x++)
        for (int y = top; y <= bottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive && !TreeGrowthCatalog1458.IsReplaceableGrowthTile(tile.TileType))
                return false;
        }
        return true;
    }
}
