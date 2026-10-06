using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>NPC.SetDefaults townNPC metadata, independent of housing roles and friendliness.</summary>
public static class VanillaNpcTownFlags1458
{
    public static bool TryGet(NpcTypeId type, out bool townNpc)
    {
        townNpc = false;
        if (type.Value <= 0 || type.Value >= VanillaNpcSourceMetadata1458.PositiveIdentityCount)
            return false;
        townNpc = type.Value is 17 or 18 or 19 or 20 or 22 or 37 or 38 or 54 or 107 or 108 or 124 or
            142 or 160 or 178 or 207 or 208 or 209 or 227 or 228 or 229 or 353 or 368 or 369 or 441 or
            550 or 588 or 633 or 637 or 638 or 656 or 663 or 670 or 678 or 679 or 680 or 681 or 682 or 683 or 684;
        return true;
    }
}
