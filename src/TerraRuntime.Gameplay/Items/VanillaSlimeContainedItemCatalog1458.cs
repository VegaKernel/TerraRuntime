using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Original 1.4.5.8 AI001 content SetDefaults and Prefix(-1) facts; no item-use capability.</summary>
public static class VanillaSlimeContainedItemCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        (int Width, int Height)? size = type.Value switch
        {
            2 => (12, 12),
            3 => (12, 12),
            8 => (10, 12),
            9 => (8, 10),
            11 => (12, 12),
            12 => (12, 12),
            13 => (12, 12),
            14 => (12, 12),
            27 => (18, 18),
            29 => (18, 18),
            58 => (12, 12),
            62 => (14, 14),
            71 => (10, 10),
            72 => (10, 12),
            73 => (10, 14),
            75 => (18, 18),
            147 => (12, 12),
            150 => (20, 24),
            166 => (20, 20),
            174 => (12, 12),
            195 => (14, 14),
            290 => (14, 24),
            292 => (14, 24),
            296 => (14, 24),
            314 => (12, 14),
            364 => (12, 12),
            365 => (12, 12),
            366 => (12, 12),
            539 => (12, 12),
            699 => (12, 12),
            700 => (12, 12),
            701 => (12, 12),
            702 => (12, 12),
            751 => (12, 12),
            965 => (12, 12),
            1104 => (12, 12),
            1105 => (12, 12),
            1106 => (12, 12),
            1124 => (12, 12),
            1125 => (12, 12),
            1345 => (12, 20),
            2322 => (14, 24),
            2350 => (14, 24),
            2997 => (14, 24),
            3081 => (12, 12),
            3086 => (12, 12),
            3609 => (16, 16),
            3736 => (16, 16),
            3737 => (16, 16),
            3738 => (16, 16),
            4343 => (22, 16),
            4344 => (22, 16),
            4367 => (20, 28),
            4368 => (20, 28),
            4369 => (20, 28),
            4370 => (20, 28),
            4371 => (20, 28),
            4612 => (20, 28),
            4674 => (20, 28),
            5395 => (14, 14),
            3347 => (12, 12),
            3610 => (16, 16),
            4026 => (22, 22),
            _ => null
        };
        definition = default;
        if (size is not { } body) return false;
        definition = new(type, new(body.Width, body.Height, 9999), null, null, null,
            new(body.Width, body.Height, false, VanillaItemPrefixFamily.None));
        return true;
    }
}
