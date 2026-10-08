using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct SelectedConsumableDefinition1458(
    ItemTypeId Type, int HealLife, int HealMana, bool HealingDelay, int UseTime,
    int UseAnimation, int DrinkColourCount);

public static class VanillaSelectedConsumableCatalog1458
{
    public static bool TryGet(ItemTypeId type, out SelectedConsumableDefinition1458 definition)
    {
        if (type == VanillaItemIds.None)
        {
            definition = new(type, 0, 0, false, 0, 0, 0);
            return true;
        }
        // These ordinary SetDefaults families share useStyle9, neutral prefix, no mana cost and three drink colours.
        (int life, int mana) = type.Value switch
        {
            28 => (50, 0),
            188 => (100, 0),
            499 => (150, 0),
            3544 => (200, 0),
            110 => (0, 50),
            189 => (0, 100),
            500 => (0, 200),
            2209 => (0, 400),
            _ => (0, 0)
        };
        definition = new(type, life, mana, life > 0, 17, 17, 3);
        return life > 0 || mana > 0;
    }
}
