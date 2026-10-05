using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Official town reward defaults grant world-drop capability, never weapon/item use.</summary>
public static class VanillaTownNpcDropCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        (int Width, int Height)? size = type.Value switch
        {
            4372 => (24, 24), 5290 => (28, 20), 3352 or 3349 => (32, 32), 3351 => (28, 28),
            3350 => (24, 14), 3821 or 3548 => (20, 20), 4818 => (14, 28), 5065 => (40, 40),
            260 => (18, 14), 2222 => (18, 18), _ => null
        };
        definition = default;
        if (size is not { } body) return false;
        definition = new(type, new(body.Width, body.Height, VanillaDefinitionCatalog.CommonMaximumStack),
            null, null, null, new(body.Width, body.Height, false, GetPrefixFamily(type)));
        return true;
    }

    public static VanillaItemPrefixFamily GetPrefixFamily(ItemTypeId type) => type.Value switch
    {
        3352 or 3351 or 3349 => VanillaItemPrefixFamily.Sword,
        3350 or 3821 => VanillaItemPrefixFamily.Ranged,
        4818 => VanillaItemPrefixFamily.Spear, // Existing generic 14-prefix family, no size/speed modifiers.
        5065 => VanillaItemPrefixFamily.Magic,
        _ => VanillaItemPrefixFamily.None
    };
}
