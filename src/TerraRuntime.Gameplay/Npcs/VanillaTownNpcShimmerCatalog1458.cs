using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// TerrariaServer 1.4.5.8 NPCID.Sets.ShimmerTownTransform membership for persistent town residents.
/// Transient Old Man / Traveling Merchant / Skeleton Merchant identities are deliberately excluded here; the runtime persistent-town roster owns the .wld lifecycle instead.
/// </summary>
public static class VanillaTownNpcShimmerCatalog1458
{
    public static bool CanTogglePersistentTownVariant(NpcTypeId type) => type.Value is
        22 or 17 or 18 or 227 or 207 or 633 or 588 or 208 or 369 or 353 or 38 or 20 or 550 or 19 or 107 or
        228 or 54 or 124 or 441 or 229 or 160 or 108 or 178 or 209 or 142 or 663;
}
