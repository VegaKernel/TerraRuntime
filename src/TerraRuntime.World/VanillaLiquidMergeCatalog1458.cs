using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 liquid merge material selection from <c>Liquid.GetLiquidMergeTypes</c>.
/// This catalog only chooses the resulting ordinary block and merge partner. Placement eligibility and liquid
/// consumption remain owned by the authoritative runtime simulator.
/// </summary>
internal static class VanillaLiquidMergeCatalog1458
{
    public static VanillaTileChangeType1458 ResolveTileChangeType(
        WorldLiquidKind first,
        WorldLiquidKind second) =>
        (first, second) switch
        {
            (WorldLiquidKind.Water, WorldLiquidKind.Lava) or
            (WorldLiquidKind.Lava, WorldLiquidKind.Water) => VanillaTileChangeType1458.LavaWater,
            (WorldLiquidKind.Water, WorldLiquidKind.Honey) or
            (WorldLiquidKind.Honey, WorldLiquidKind.Water) => VanillaTileChangeType1458.HoneyWater,
            (WorldLiquidKind.Lava, WorldLiquidKind.Honey) or
            (WorldLiquidKind.Honey, WorldLiquidKind.Lava) => VanillaTileChangeType1458.HoneyLava,
            (WorldLiquidKind.Water, WorldLiquidKind.Shimmer) or
            (WorldLiquidKind.Shimmer, WorldLiquidKind.Water) => VanillaTileChangeType1458.ShimmerWater,
            (WorldLiquidKind.Lava, WorldLiquidKind.Shimmer) or
            (WorldLiquidKind.Shimmer, WorldLiquidKind.Lava) => VanillaTileChangeType1458.ShimmerLava,
            (WorldLiquidKind.Honey, WorldLiquidKind.Shimmer) or
            (WorldLiquidKind.Shimmer, WorldLiquidKind.Honey) => VanillaTileChangeType1458.ShimmerHoney,
            _ => VanillaTileChangeType1458.None
        };

    public static bool TryResolve(
        WorldLiquidKind sourceKind,
        bool waterNearby,
        bool lavaNearby,
        bool honeyNearby,
        bool shimmerNearby,
        out TileTypeId mergeTile,
        out WorldLiquidKind mergeKind)
    {
        mergeTile = VanillaTileIds.Obsidian;
        mergeKind = sourceKind;

        if (sourceKind != WorldLiquidKind.Water && waterNearby)
        {
            mergeTile = sourceKind switch
            {
                WorldLiquidKind.Lava => VanillaTileIds.Obsidian,
                WorldLiquidKind.Honey => VanillaTileIds.HoneyBlock,
                WorldLiquidKind.Shimmer => VanillaTileIds.ShimmerBlock,
                _ => mergeTile
            };
            mergeKind = WorldLiquidKind.Water;
        }

        if (sourceKind != WorldLiquidKind.Lava && lavaNearby)
        {
            mergeTile = sourceKind switch
            {
                WorldLiquidKind.Water => VanillaTileIds.Obsidian,
                WorldLiquidKind.Honey => VanillaTileIds.CrispyHoneyBlock,
                WorldLiquidKind.Shimmer => VanillaTileIds.ShimmerBlock,
                _ => mergeTile
            };
            mergeKind = WorldLiquidKind.Lava;
        }

        if (sourceKind != WorldLiquidKind.Honey && honeyNearby)
        {
            mergeTile = sourceKind switch
            {
                WorldLiquidKind.Water => VanillaTileIds.HoneyBlock,
                WorldLiquidKind.Lava => VanillaTileIds.CrispyHoneyBlock,
                WorldLiquidKind.Shimmer => VanillaTileIds.ShimmerBlock,
                _ => mergeTile
            };
            mergeKind = WorldLiquidKind.Honey;
        }

        if (sourceKind != WorldLiquidKind.Shimmer && shimmerNearby)
        {
            mergeTile = VanillaTileIds.ShimmerBlock;
            mergeKind = WorldLiquidKind.Shimmer;
        }

        return mergeKind != sourceKind;
    }
}
