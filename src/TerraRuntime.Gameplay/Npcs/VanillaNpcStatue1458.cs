using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Bounded NPC-producing Wiring.HitWireSingle(105) and NPC.MechSpawn, Terraria 1.4.5.8.</summary>
public static class VanillaNpcStatue1458
{
    public const int Cooldown = 30;

    public static bool TryResolve(int style, bool party, out int type, out int xOffset, out int yOffset)
    {
        type = style switch
        {
            4 => 1, 5 => 73, 7 => 49, 8 => 55, 9 => party ? 540 : 46, 10 => 21,
            13 => 24, 18 => 67, 27 => 85, 30 => 6, 35 => 2, 42 => 58, _ => 0
        };
        xOffset = style == 7 ? -4 : style == 27 ? -9 : 0;
        yOffset = style == 7 ? -6 : style is 4 or 8 or 9 or 18 or 42 ? -12 : 0;
        return type != 0;
    }

    private static bool IsBunny(NpcTypeId type) => type == VanillaNpcIds.Bunny || type == VanillaNpcIds.PartyBunny ||
        type == VanillaNpcIds.BunnySlimed || type == VanillaNpcIds.BunnyXmas;

    public static bool CanSpawn(ReadOnlySpan<NpcSnapshot> npcs, float x, float y, int type)
    {
        if (npcs.Length > 200 || !float.IsFinite(x) || !float.IsFinite(y) || type is not (1 or 73 or 49 or 55 or 46 or 540 or 21 or 24 or 67 or 85 or 6 or 2 or 58)) return false;
        var requested = new NpcTypeId(type);
        int total = 0, near = 0, middle = 0;
        foreach (var npc in npcs)
        {
            if (!npc.IsActive || !(npc.TypeIdentity == requested ||
                (requested == VanillaNpcIds.Bunny || requested == VanillaNpcIds.PartyBunny) && IsBunny(npc.TypeIdentity) || requested == VanillaNpcIds.Goldfish && npc.TypeIdentity == VanillaNpcIds.GoldfishWalker)) continue;
            total++;
            float dx = npc.PositionX - x, dy = npc.PositionY - y;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (distance < 200f) near++;
            if (distance < 600f) middle++;
        }
        return near < 3 && middle < 6 && total < 10;
    }
}
