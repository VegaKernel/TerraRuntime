using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// TerrariaServer 1.4.5.8 pick-power identities used by authoritative packet-17 mining admission.
/// This deliberately stores only source-verified immutable item facts and does not duplicate Item.SetDefaults behavior.
/// </summary>
public static class VanillaPickToolCatalog1458
{
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
            2774 or 2776 => 225,
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
