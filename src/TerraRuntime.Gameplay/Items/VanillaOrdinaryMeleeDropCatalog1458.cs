using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Original ordinary melee reward world-drop facts; no item-use or equipment admission.</summary>
public static class VanillaOrdinaryMeleeDropCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        (int Width, int Height)? size = type.Value switch
        {
            88 => (22, 16),
            118 => (18, 18),
            166 => (20, 20),
            243 => (18, 18),
            393 => (24, 28),
            410 => (18, 18),
            411 => (18, 18),
            485 => (24, 28),
            522 => (12, 14),
            527 => (14, 14),
            528 => (14, 14),
            885 => (16, 24),
            1306 => (24, 28),
            1320 => (24, 28),
            1330 => (18, 20),
            1332 => (12, 14),
            2801 => (28, 20),
            2802 => (28, 20),
            3102 => (24, 18),
            3212 => (22, 22),
            3213 => (26, 24),
            3478 => (18, 18),
            3479 => (18, 18),
            3757 => (18, 18),
            3758 => (28, 20),
            3759 => (18, 14),
            3784 => (18, 18),
            3785 => (18, 18),
            3786 => (18, 18),
            3794 => (18, 18),
            4037 => (22, 22),
            4271 => (22, 14),
            4349 => (14, 14),
            4350 => (14, 14),
            4351 => (14, 14),
            4352 => (14, 14),
            4353 => (14, 14),
            4354 => (14, 14),
            4377 => (14, 14),
            4378 => (14, 14),
            4389 => (14, 14),
            4671 => (20, 28),
            5091 => (16, 30),
            5094 => (24, 28),
            5127 => (14, 14),
            5488 => (30, 30),
            5489 => (30, 30),
            6157 => (24, 24),
            _ => null
        };
        definition = default;
        if (size is not { } body) return false;
        definition = new(type, new(body.Width, body.Height, VanillaDefinitionCatalog.CommonMaximumStack),
            null, null, null, new(body.Width, body.Height, false, GetPrefixFamily(type)));
        return true;
    }

    public static VanillaItemPrefixFamily GetPrefixFamily(ItemTypeId type) => type.Value switch
    {
        1306 or 1320 or 5094 => VanillaItemPrefixFamily.Sword,
        393 or 485 or 885 or 3102 or 3212 or 6157 => VanillaItemPrefixFamily.Accessory,
        _ => VanillaItemPrefixFamily.None
    };

    public static bool AcceptsNaturalPrefix(ItemTypeId type, PrefixId prefix) =>
        prefix == VanillaPrefixIds.None ||
        (GetPrefixFamily(type) != VanillaItemPrefixFamily.None &&
         VanillaItemPrefixTable1458.TryGet(type, out var record) &&
         VanillaItemPrefixTable1458.Accepts(in record, prefix.Value));
}
