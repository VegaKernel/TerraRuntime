using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public static class VanillaNpcDebuffRegeneration1458
{
    // NPC.UpdateNPC_BuffApplyDOTs 1.4.5.8 groups Venom into 15-life pulses.
    // Its deeper retained counter must survive expiry and the subsequent ordinary phase.
    public static bool TryStep(in NpcSnapshot npc, bool goodWorld, bool poison, bool fire,
        bool venom, out (int Life, int Counter, int Damage) result, bool onFire3 = false)
    {
        result = default;
        if (npc.Type is not (1 or 3 or 59) || npc.Simulation.LifeRegenCounter is not { } counter ||
            counter is < -1799 or > 119 || npc.Simulation.Immortal is not { } immortal) return false;
        int life = npc.Simulation.Life;
        if (npc.Simulation.DontTakeDamage) { result = new(life, counter, 0); return true; }
        int natural = 0;
        if (npc.Type == VanillaNpcIds.LavaSlime.Value && npc.Ai.Ai1 == VanillaItemIds.Hellstone.Value &&
            npc.Simulation.LiquidContact == NpcLiquidContactKind.Lava) natural = 32;
        if (npc.Type == VanillaNpcIds.BlueSlime.Value)
        {
            if (npc.Ai.Ai1 == VanillaItemIds.LifeCrystal.Value) natural = 16;
            else if (npc.Ai.Ai1 is 364f or 1104f or 365f or 1105f or 366f or 1106f) natural = 24;
        }
        bool torch = npc.Type == VanillaNpcIds.BlueSlime.Value && npc.Ai.Ai1 == VanillaItemIds.Torch.Value && goodWorld;
        counter += natural - (poison ? 12 : 0) - (fire && !torch ? 8 : 0) - (venom ? 60 : 0) - (onFire3 ? 30 : 0);
        while (counter >= 120)
        {
            counter -= 120;
            if (!immortal) life = (int)Math.Min((long)life + 1, npc.Simulation.LifeMax);
        }
        int pulse = venom ? 15 : onFire3 ? 5 : Math.Max(1, counter / -120);
        int damage = 0;
        while (counter <= -120 * pulse) { counter += 120 * pulse; damage += pulse; }
        if (!immortal) life -= damage;
        result = new(life, counter, damage);
        return true;
    }
}
