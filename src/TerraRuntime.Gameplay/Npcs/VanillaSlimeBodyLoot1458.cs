using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Terraria 1.4.5.8 SlimeBodyItemDropRule, before NPC-specific imported drops.</summary>
public static class VanillaSlimeBodyLoot1458
{
    public static bool CanDrop(NpcTypeId npcType, float retainedContent) =>
        (npcType == VanillaNpcIds.BlueSlime || npcType == VanillaNpcIds.LavaSlime ||
         npcType == VanillaNpcIds.IceSlime || npcType == VanillaNpcIds.SpikedIceSlime ||
         npcType == VanillaNpcIds.SandSlime) &&
        retainedContent > 0f && retainedContent < VanillaItemIds.Count;

    public static bool TryEvaluate(NpcTypeId npcType, float retainedContent, INpcLootRollSource random,
        out bool dropped, out NpcLootDrop drop)
    {
        ArgumentNullException.ThrowIfNull(random);
        dropped = false;
        drop = default;
        if (!CanDrop(npcType, retainedContent)) return true;
        var item = new ItemTypeId((int)retainedContent);
        // The source accepts any retained item ID. Admit already verified materializers as well as the
        // producer's sixty outcomes; unknown raw contents reject the detached whole-death plan.
        if (!VanillaDefinitionCatalog.TryGetWorldDrop(item, out _) ||
            !VanillaNaturalItemPrefixRoller.CanRoll(item)) return false;
        GetStackRange(item, out int minimum, out int maximum);
        // SlimeBodyItemDropRule draws even for a fixed 1..1 stack, unlike mechanical summon drops.
        drop = new(item, checked((short)random.NextInt32(minimum, maximum + 1)));
        dropped = true;
        return true;
    }

    public static void GetStackRange(ItemTypeId item, out int minimum, out int maximum)
    {
        (minimum, maximum) = item.Value switch
        {
            8 => (5, 10),
            166 => (2, 6),
            965 => (20, 45),
            11 or 12 or 13 or 14 or 174 or 364 or 365 or 366 or 699 or 700 or 701 or 702 or
                1104 or 1105 or 1106 or 3347 => (3, 13),
            71 => (50, 99),
            72 => (20, 99),
            73 => (1, 2),
            4343 or 4344 => (2, 5),
            2 or 3 or 9 or 150 or 593 or 751 or 1103 or 3081 or 3086 or 3609 or 3610 or 5395 => (10, 25),
            147 or 314 or 1124 or 1125 or 1345 or 3736 or 3737 or 3738 => (2, 5),
            _ => (1, 1)
        };
    }
}
