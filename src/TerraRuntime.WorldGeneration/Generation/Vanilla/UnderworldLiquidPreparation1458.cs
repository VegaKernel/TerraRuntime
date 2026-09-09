using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Generation-only surroundings of Liquid.QuickWater(-2) at the ordinary Underworld phase.
/// Dungeon and Aether have not run: no dungeon bounds exist and Reset's shimmer position is still (0,0).</summary>
internal static class UnderworldLiquidPreparation1458
{
    private const ushort BreakableIce = 162;
    private const int ShimmerClearRadius = 150;

    public static void Settle(WorldTileStore store, int waterLine, CancellationToken cancellationToken)
    {
        ClearBeforeAether(store);
        new VanillaWorldLiquidSimulator1458(store).QuickWaterBeforeDungeonGeneration(waterLine, cancellationToken);
        ClearBeforeAether(store);
        CleanupInteractions(store, cancellationToken);
    }

    internal static void ClearBeforeAether(WorldTileStore store)
    {
        for (int y = 0; y <= ShimmerClearRadius / 2 && y < store.Dimensions.HeightTiles; y++)
        for (int x = 0; x < ShimmerClearRadius && x < store.Dimensions.WidthTiles; x++)
        {
            if (x * x + y * y >= ShimmerClearRadius * ShimmerClearRadius) continue;
            ref WorldTile tile = ref UnderworldTerrain1458.At(store, x, y);
            if (tile.LiquidKind != WorldLiquidKind.Shimmer) tile.LiquidAmount = 0;
            if (tile.Type == BreakableIce) tile.Flags &= ~WorldTileFlags.Active;
        }
    }

    internal static void CleanupInteractions(WorldTileStore store, CancellationToken cancellationToken)
    {
        for (int x = 1; x < store.Dimensions.WidthTiles - 2; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 1; y < store.Dimensions.HeightTiles - 2; y++)
            {
                ref WorldTile tile = ref UnderworldTerrain1458.At(store, x, y);
                if (!tile.IsActive || (tile.TileType != VanillaTileIds.Obsidian && tile.TileType != VanillaTileIds.ShimmerBlock)) continue;
                tile.LiquidAmount = 0;
                tile.LiquidKind = WorldLiquidKind.Water;
                int kinds = NeighborBit(store.Get(x - 1, y)) | NeighborBit(store.Get(x + 1, y)) |
                    NeighborBit(store.Get(x, y - 1), above: true) | NeighborBit(store.Get(x, y + 1));
                if ((kinds & (kinds - 1)) != 0) continue;
                WorldLiquidKind kind = kinds switch
                {
                    1 => WorldLiquidKind.Water,
                    2 => WorldLiquidKind.Lava,
                    4 => WorldLiquidKind.Honey,
                    8 => WorldLiquidKind.Shimmer,
                    _ => tile.TileType == VanillaTileIds.Obsidian ? WorldLiquidKind.Lava : WorldLiquidKind.Shimmer
                };
                // WorldGen.LiquidInteractionsCleanup uses ClearEverything, not just active(false).
                tile = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = kind };
            }
        }
    }

    private static int NeighborBit(WorldTile tile, bool above = false)
    {
        if (tile.IsActive || tile.LiquidAmount == 0) return 0;
        // Pinned 1.4.5.8 counts shimmer ABOVE as water. Preserve that asymmetry, do not silently fix vanilla.
        return above && tile.LiquidKind == WorldLiquidKind.Shimmer ? 1 : 1 << (int)tile.LiquidKind;
    }
}
