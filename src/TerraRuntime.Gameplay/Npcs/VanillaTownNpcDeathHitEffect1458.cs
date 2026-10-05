using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Dedicated-server death choices for the verified town NPC identities.</summary>
public static class VanillaTownNpcDeathHitEffect1458
{
    public static void ConsumeLethalChoices(NpcTypeId type, INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!HasLethalChoices(type)) return;
        // NPC.HitEffect evaluates four Gore.NewGore type arguments before dedicated
        // particle suppression. These are source RNG choices, not particle allocations.
        for (int choice = 0; choice < 4; choice++) random.NextInt32(61, 64);
    }

    private static bool HasLethalChoices(NpcTypeId type) =>
        type == VanillaNpcIds.Angler || type == VanillaNpcIds.Princess ||
        type == VanillaNpcIds.TownCat || type == VanillaNpcIds.TownDog ||
        type == VanillaNpcIds.TownBunny || type == VanillaNpcIds.TownSlimeBlue ||
        type == VanillaNpcIds.TownSlimeGreen || type == VanillaNpcIds.TownSlimeOld ||
        type == VanillaNpcIds.TownSlimePurple || type == VanillaNpcIds.TownSlimeRainbow ||
        type == VanillaNpcIds.TownSlimeRed || type == VanillaNpcIds.TownSlimeYellow ||
        type == VanillaNpcIds.TownSlimeCopper;
}
