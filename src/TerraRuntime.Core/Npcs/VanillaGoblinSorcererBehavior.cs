using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>Dedicated-server NPC.AI style 8 for Goblin Sorcerer (NPC 29), TerrariaServer 1.4.5.8.</summary>
internal sealed class VanillaGoblinSorcererBehavior(IVanillaNpcRandom random) : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom random = random ?? throw new ArgumentNullException(nameof(random));
    private IVanillaWallOfFleshEnvironment? environment;

    public void SetEnvironment(IVanillaWallOfFleshEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (environment is null || (npc.TypeIdentity != VanillaNpcIds.GoblinSorcerer && npc.TypeIdentity != VanillaNpcIds.Tim) ||
            definition.AiStyle != VanillaNpcAiStyles.Caster)
        {
            next = default;
            return false;
        }

        ushort targetSlot = context.TrySelectClosestTarget(in npc, in definition, out var selected) ? selected.Target : (ushort)0;
        context.TryFindCandidate((byte)targetSlot, out VanillaNpcTargetCandidate target);
        int directionX = npc.Simulation.DirectionX, directionY = npc.Simulation.DirectionY;
        if (!target.Dead && !(target.NoAggro && directionX != 0))
        {
            directionX = target.CenterX < npc.PositionX + definition.Width / 2 ? -1 : 1;
            directionY = target.CenterY < npc.PositionY + definition.Height / 2 ? -1 : 1;
        }

        float x = npc.PositionX, y = npc.PositionY, vx = npc.VelocityX * .93f, vy = npc.VelocityY;
        if (vx is > -.1f and < .1f) vx = 0f;
        NpcAiState ai = npc.Ai;
        if (ai.Ai2 != 0f && ai.Ai3 != 0f)
        {
            x = ai.Ai2 * 16f - definition.Width / 2 + 8f;
            y = ai.Ai3 * 16f - definition.Height;
            vx = vy = 0f;
            ai = ai with { Ai2 = 0f, Ai3 = 0f };
        }

        float timer = ai.Ai0 == 0f ? 501f : ai.Ai0 + 1f;
        float attack = ai.Ai1;
        if (timer is 100f or 200f or 300f) attack = 30f;
        if (timer >= 650f) timer = 1f;
        if (attack > 0f) attack--;
        next = new(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai with { Ai0 = timer, Ai1 = attack },
            npc.Simulation with { DirectionX = directionX, DirectionY = directionY, JustHit = false });
        return true;
    }

    public NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot committed, VanillaNpcBehaviorContext context,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (environment is null || (before.TypeIdentity != VanillaNpcIds.GoblinSorcerer && before.TypeIdentity != VanillaNpcIds.Tim) ||
            committed.TypeIdentity != before.TypeIdentity || AdvanceTimer(before.Ai.Ai0) < 650f ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost ||
            !VanillaNpcDefinitionCatalog.TryGet(committed.TypeIdentity, out VanillaNpcDefinition definition))
            return committed;
        if (!environment.TryFindTeleportSpot(committed.PositionX + definition.Width * .5f,
                committed.PositionY + definition.Height * .5f, (int)target.CenterX / 16, (int)target.CenterY / 16,
                context.Candidates, random, out int x, out int y))
            return committed;
        return mutations.TryUpdateAi(in committed, committed.Ai with { Ai1 = 19f, Ai2 = x, Ai3 = y }, out NpcSnapshot completed)
            ? completed : committed;
    }

    private static float AdvanceTimer(float timer) => timer == 0f ? 501f : timer + 1f;
}
