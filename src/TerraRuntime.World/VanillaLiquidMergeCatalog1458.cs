using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 liquid merge material selection from <c>Liquid.GetLiquidMergeTypes</c>.
/// This catalog only chooses the resulting ordinary block and merge partner. Placement eligibility and liquid
/// consumption remain owned by the authoritative runtime simulator.
/// </summary>
internal static class VanillaLiquidMergeCatalog1458
{
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
