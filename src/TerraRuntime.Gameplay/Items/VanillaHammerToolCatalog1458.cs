using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// TerrariaServer 1.4.5.8 hammer powers extracted from Item.SetDefaults. This catalog intentionally owns only the
/// source-backed hammer identity/power fact required by packet-17 wall authority; mining hit accumulation remains
/// outside this slice. Item 5283 has a weaker variant (35 instead of 45), so the conservative minimum is exposed.
/// </summary>
public static class VanillaHammerToolCatalog1458
{
    public static bool TryGetHammerPower(ItemTypeId itemType, out short hammerPower)
    {
        hammerPower = itemType.Value switch
        {
            7 => 40,
            104 => 55,
            196 => 25,
            204 => 60,
            217 => 70,
            367 => 80,
            654 => 40,
            657 => 35,
            660 => 55,
            787 => 85,
            797 => 55,
            922 => 40,
            1234 => 90,
            1262 => 90,
            1305 => 100,
            1507 => 90,
            2294 => 70,
            2516 => 35,
            2746 => 35,
            2775 => 100,
            3481 => 59,
            3487 => 50,
            3493 => 43,
            3499 => 38,
            3505 => 35,
            3511 => 45,
            3517 => 55,
            3525 => 100,
            4317 => 80,
            5283 => 35,
            _ => 0
        };
        return hammerPower > 0;
    }
}
