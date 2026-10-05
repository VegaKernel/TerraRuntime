using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaSlimeRegenerationResult1458(int Life, int Counter, int Damage);

/// <summary>Source NPC.BuffApplyDOTs for the represented ordinary contained-slime states.</summary>
public static class VanillaSlimeRegeneration1458
{
    public static bool TryStep(in NpcSnapshot npc, bool goodWorld, bool poison, bool fire,
        out VanillaSlimeRegenerationResult1458 result)
    {
        result = default;
        if (npc.TypeIdentity != VanillaNpcIds.BlueSlime && npc.TypeIdentity != VanillaNpcIds.LavaSlime ||
            npc.Simulation.LifeRegenCounter is not { } counter || counter is < -119 or > 119)
            return false;
        int life = npc.Simulation.Life;
        if (npc.Simulation.DontTakeDamage)
        {
            result = new(life, counter, 0);
            return true;
        }
        if (npc.Simulation.Immortal is not { } immortal) return false;
        bool torch = npc.TypeIdentity == VanillaNpcIds.BlueSlime &&
            npc.Ai.Ai1 == VanillaItemIds.Torch.Value && goodWorld;
        int natural = npc.TypeIdentity == VanillaNpcIds.LavaSlime && npc.Ai.Ai1 == VanillaItemIds.Hellstone.Value &&
            npc.Simulation.LiquidContact == NpcLiquidContactKind.Lava ? 32 : 0;
        if (npc.TypeIdentity == VanillaNpcIds.BlueSlime)
        {
            if (npc.Ai.Ai1 == VanillaItemIds.LifeCrystal.Value) natural = 16;
            else if (npc.Ai.Ai1 == VanillaItemIds.CobaltOre.Value || npc.Ai.Ai1 == VanillaItemIds.PalladiumOre.Value ||
                npc.Ai.Ai1 == VanillaItemIds.MythrilOre.Value || npc.Ai.Ai1 == VanillaItemIds.OrichalcumOre.Value ||
                npc.Ai.Ai1 == VanillaItemIds.AdamantiteOre.Value || npc.Ai.Ai1 == VanillaItemIds.TitaniumOre.Value) natural = 24;
        }
        counter += natural - (poison ? 12 : 0) - (fire && !torch ? 8 : 0);
        if (counter >= 120)
        {
            counter -= 120;
            if (!immortal) life = (int)Math.Min((long)life + 1, npc.Simulation.LifeMax);
        }
        int damage = 0;
        if (counter <= -120)
        {
            counter += 120;
            damage = 1;
            if (!immortal)
            {
                if (life == int.MinValue) return false;
                life--;
            }
        }
        result = new(life, counter, damage);
        return true;
    }
}
