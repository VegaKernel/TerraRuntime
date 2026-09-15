using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Npcs;

/// <summary>Dedicated-server NPC.AI style8 for Dark Caster only, TerrariaServer 1.4.5.8.</summary>
internal sealed class VanillaDarkCasterBehavior : IVanillaNpcBehaviorStrategy
{
    public IVanillaCasterEnvironment? Environment { get; set; }

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        next = default;
        if (npc.TypeIdentity != VanillaNpcIds.DarkCaster || !definition.TryResolveHitbox(npc.Simulation, out var hitbox))
            return false;
        var local = npc.Simulation.LocalAi;
        Span<NpcSnapshot> head = stackalloc NpcSnapshot[1];
        if (context.CopyNpcPeers(VanillaNpcIds.SkeletronHead, head) != 0 && head[0].Ai.Ai3 == 1f)
            local = local with { Ai3 = 1f };
        ushort targetSlot = context.TrySelectClosestTarget(in npc, in definition, out var selected) ? selected.Target : (ushort)0;
        if (!context.TryFindCandidate((byte)targetSlot, out var target))
            target = new((byte)targetSlot, 10, 21, 0, false, false, false, false);
        int direction = npc.Simulation.DirectionX, directionY = npc.Simulation.DirectionY;
        if (!target.Dead && !(target.NoAggro && direction != 0))
        {
            int px = (int)(target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * .5f) + (int)VanillaPlayerHitboxFacts.BaseWidth / 2;
            int py = (int)(target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * .5f) + (int)VanillaPlayerHitboxFacts.BaseHeight / 2;
            direction = px < npc.PositionX + hitbox.Width / 2 ? -1 : 1;
            directionY = py < npc.PositionY + hitbox.Height / 2 ? -1 : 1;
        }
        float vx = npc.VelocityX * .93f, vy = npc.VelocityY;
        if (vx > -.1f && vx < .1f) vx = 0;
        PositionBeforeMotion(in npc, in hitbox, out float x, out float y);
        var ai = npc.Ai;
        if (ai.Ai2 != 0f && ai.Ai3 != 0f) { vx = vy = 0; ai = ai with { Ai2 = 0, Ai3 = 0 }; }
        float timer = AdvanceTimer(ai.Ai0, local.Ai3 == 1f);
        float attack = ai.Ai1;
        if (timer is 100f or 200f or 300f &&
            VanillaNpcGlobalFiringDistance.Contains(x + hitbox.Width * .5f, y + hitbox.Height * .5f, target.CenterX, target.CenterY))
            attack = 30;
        if (timer >= 650) timer = 1;
        if (attack > 0) attack--;
        next = new(npc.Type, npc.NetId, x, y, vx, vy, targetSlot,
            ai with { Ai0 = timer, Ai1 = attack },
            npc.Simulation with { DirectionX = direction, DirectionY = directionY, LocalAi = local });
        return true;
    }

    public NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot committed,
        VanillaNpcBehaviorContext context, IVanillaNpcRandom random, INpcAiCommittedNpcMutationSink mutations)
    {
        if (Environment is null || AdvanceTimer(before.Ai.Ai0, committed.Simulation.LocalAi.Ai3 == 1f) < 650 ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, out var definition) ||
            !definition.TryResolveHitbox(before.Simulation, out var hitbox)) return committed;
        if (!context.TryFindCandidate((byte)committed.Target, out var target))
            target = new((byte)committed.Target, 10, 21, 0, false, false, false, false);
        PositionBeforeMotion(in before, in hitbox, out float x, out float y);
        if (!Environment.TryFindTeleportSpot(x + hitbox.Width * .5f, y + hitbox.Height * .5f,
            (int)target.CenterX / 16, (int)target.CenterY / 16,
            context.CountNpcPeers(VanillaNpcIds.SkeletronHead) > 0, context.Candidates, random, out int tx, out int ty))
            return committed;
        return mutations.TryUpdateAi(in committed, committed.Ai with { Ai1 = 19, Ai2 = tx, Ai3 = ty }, out var completed)
            ? completed : committed;
    }

    public static void SpawnSphere(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations)
    {
        if (committed.Ai.Ai1 != 25f || !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, out var definition) ||
            !definition.TryResolveHitbox(before.Simulation, out var hitbox)) return;
        PositionBeforeMotion(in before, in hitbox, out float x, out float y);
        mutations.TrySpawn(in committed, new(VanillaNpcIds.WaterSphere, (int)x + hitbox.Width / 2, (int)y - 8, 0, 0, 255)
            { InitialAi = new(0, 0, 0, committed.Simulation.LocalAi.Ai3 == 1f ? 1 : 0) }, out _);
    }

    private static float AdvanceTimer(float timer, bool redHat)
    {
        if (timer == 0) timer = 500;
        timer++;
        if (redHat) { timer++; if (timer % 2f == 1f) timer--; }
        return timer;
    }

    private static void PositionBeforeMotion(in NpcSnapshot npc, in VanillaNpcHitboxSize hitbox, out float x, out float y)
    {
        bool teleport = npc.Ai.Ai2 != 0 && npc.Ai.Ai3 != 0;
        x = teleport ? npc.Ai.Ai2 * 16f - hitbox.Width / 2 + 8f : npc.PositionX;
        y = teleport ? npc.Ai.Ai3 * 16f - hitbox.Height : npc.PositionY;
    }
}
