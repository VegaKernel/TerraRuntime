using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Initial server-player mining admission: Item.SetDefaults and Player.GetPickaxeDamage, 1.4.5.8.</summary>
public static class VanillaBotMiningMaterials1458
{
    // TileID: eight ordinary ore variants. Their pick225 damage exceeds the HitTile100 threshold.
    public static bool IsOre(TileTypeId type) => type.Value is 6 or 7 or 8 or 9 or 166 or 167 or 168 or 169;
    public static bool IsSingleHitMaterial(TileTypeId type) => type == VanillaTileIds.Dirt || type == VanillaTileIds.Stone || IsOre(type);
    public static bool IsOreItem(ItemTypeId type) => type.Value is 11 or 12 or 13 or 14 or 699 or 700 or 701 or 702;
}
