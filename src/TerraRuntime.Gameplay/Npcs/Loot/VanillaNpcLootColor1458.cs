using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source identity for CommonCode.ModifyItemDropFromNPC, including SlimeBodyItemDropRule.</summary>
public readonly record struct NpcLootColorContext1458(NpcTypeId Type, NpcNetId NetId, bool? RemixWorld);

/// <summary>1.4.5.8 default NPC tints and CommonCode's two postdrop color arms.</summary>
public static class VanillaNpcLootColor1458
{
    public static bool TryResolve(in NpcLootColorContext1458 npc, ItemTypeId item, out WorldItemColor? color)
    {
        color = null;
        if (item.Value == VanillaItemIds.Gel.Value)
        {
            if (npc.Type.Value == VanillaNpcIds.BlueSlime.Value && npc.NetId.Value is not (-1 or -2 or -5 or -6))
            {
                color = npc.NetId.Value switch
                {
                    1 => new(0, 80, 255, 100),
                    -3 => new(0, 220, 40, 100),
                    -4 => new(250, 30, 90, 90),
                    -7 => new(200, 0, 255, 150),
                    -8 => new(255, 30, 0, 100),
                    -9 => new(255, 255, 0, 100),
                    -10 => new(143, 215, 93, 100),
                    _ => null
                };
                // No owned custom NPC tint exists; do not invent one from the gameplay type alone.
                return color is not null;
            }
            if (npc.Type.Value == VanillaNpcIds.LavaSlime.Value)
            {
                if (npc.RemixWorld is not { } remix) return false;
                if (remix) color = new(255, 127, 0, 255);
            }
        }
        else if (item.Value == 319) // CommonCode's source Leather arm; identity is the NPC net variant.
            color = npc.NetId.Value switch
            {
                542 => new(189, 148, 96, 255),
                543 => new(112, 85, 89, 255),
                544 => new(145, 27, 40, 255),
                545 => new(158, 113, 164, 255),
                _ => null
            };
        return true;
    }
}
