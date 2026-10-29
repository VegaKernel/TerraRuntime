using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaMaggotDeathBirth1458(int BottomX, int BottomY, float VelocityX, float VelocityY);

/// <summary>Dedicated-server caller effects in NPC.HitEffect, 1.4.5.8. Allocation belongs to the existing NPC store.</summary>
public static class VanillaRangedNpcDeath1458
{
    // NPC379's ritual strike writes the separately allocated controller438.
    // Until that owner is represented, every ingress must stop before hit effects
    // or interaction credit; the ordinary ai3>=0 archer remains admitted.
    public static bool CanOwnStrike(in NpcSnapshot npc) => npc.Type != 379 || npc.Ai.Ai3 >= 0f;

    public static void ConsumeDedicatedLethalChoices(int type, IVanillaNpcRandom random)
    {
        if (type == 606)
            for (int choice = 0; choice < 6; choice++) random.NextInt32(0, 2);
    }

    public static bool TryGetMaggotCount(in NpcSnapshot parent, in VanillaNpcHitboxSize body,
        IVanillaNpcRandom random, out int count)
    {
        count = 0;
        if (parent.Type != 632 || parent.Simulation.Life != 0 || body.Width <= 0 || body.Height < 2 ||
            !float.IsFinite(parent.PositionX) || !float.IsFinite(parent.PositionY) ||
            parent.PositionX + body.Width / 2 < int.MinValue || parent.PositionX + body.Width / 2 >= int.MaxValue ||
            parent.PositionY < int.MinValue || parent.PositionY >= int.MaxValue - body.Height - 10) return false;
        count = random.NextInt32(0, 2) + 2;
        return true;
    }

    public static bool TryGetMaggotBirth(in NpcSnapshot parent, in VanillaNpcHitboxSize body,
        int index, IVanillaNpcRandom random, out VanillaMaggotDeathBirth1458 birth)
    {
        birth = default;
        if (parent.Type != 632 || parent.Simulation.Life != 0 || index is < 0 or > 2 ||
            body.Width <= 0 || body.Height < 2 || !float.IsFinite(parent.PositionX) || !float.IsFinite(parent.PositionY) ||
            parent.PositionX + body.Width / 2 < int.MinValue || parent.PositionX + body.Width / 2 >= int.MaxValue ||
            parent.PositionY < int.MinValue || parent.PositionY >= int.MaxValue - body.Height - 10) return false;
        float x = random.NextInt32(-10, 10) * .2f;
        float y = -3.5f - random.NextInt32(5, 10) * .3f - index * .5f;
        int bottomY = (int)parent.PositionY + random.NextInt32(0, body.Height / 2) + 10;
        birth = new((int)(parent.PositionX + body.Width / 2), bottomY, x, y);
        return true;
    }
}
