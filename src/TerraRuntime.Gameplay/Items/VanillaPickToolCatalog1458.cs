using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// TerrariaServer 1.4.5.8 pick-power identities used by authoritative packet-17 mining admission.
/// This deliberately stores only source-verified immutable item facts and does not duplicate Item.SetDefaults behavior.
/// </summary>
public static class VanillaPickToolCatalog1458
{
    // Item.SetDefaults / Player.ItemCheck_UseMiningTools: this special tool has pick=0.
    public static bool IsShovel(ItemTypeId type) => type.Value == 4711;

    // TileID.Sets.CanBeDugByShovel, TerrariaServer 1.4.5.8. Never grant ordinary pick authority.
    public static bool CanBeDugByShovel(TileTypeId tile) => tile.Value is
        0 or 668 or 59 or 57 or 123 or 224 or 147 or 2 or 109 or 23 or 661 or 199 or 662 or 60 or 70 or
        477 or 492 or 53 or 116 or 112 or 234 or 40 or 495 or 633 or 189 or 196 or 460 or 717 or 718 or 719;

    public static bool TryGetTilePickPower(ItemTypeId type, TileTypeId tile, out short pickPower)
    {
        if (IsShovel(type))
        {
            // DamageTileWithShovel uses 30; its extra grass stripping hit still follows the existing
            // failed-pick transformation path, whose tile profiles require no higher minimum power.
            pickPower = CanBeDugByShovel(tile) ? (short)30 : (short)0;
            return pickPower != 0;
        }
        return TryGetPickPower(type, out pickPower);
    }

    public static bool TryGetPickPower(ItemTypeId type, out short pickPower)
    {
        pickPower = type.Value switch
        {
            1 => 40,
            103 => 65,
            122 => 100,
            385 => 110,
            386 => 150,
            388 => 180,
            579 => 200,
            776 => 110,
            777 => 150,
            778 => 180,
            798 => 70,
            882 => 35,
            990 => 200,
            1188 or 1189 => 130,
            1195 or 1196 => 165,
            1202 or 1203 => 190,
            1230 or 1231 => 200,
            1294 => 210,
            1320 => 55,
            1506 => 200,
            1917 => 55,
            2176 => 200,
            2341 => 59,
            2774 or 2776 or 2779 or 2781 or 2784 or 2786 or 3464 or 3466 => 225,
            2798 => 230,
            3485 => 59,
            3491 => 50,
            3497 => 43,
            3503 => 35,
            3509 => 35,
            3515 => 45,
            3521 => 55,
            4059 => 55,
            _ => 0
        };
        return pickPower > 0;
    }
}
