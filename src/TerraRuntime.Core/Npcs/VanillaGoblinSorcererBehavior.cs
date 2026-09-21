using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>Dedicated-server NPC.AI style 8 for the source-verified Goblin Sorcerer, Tim and Rune Wizard variants.</summary>
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
        if (environment is null || !IsSupported(npc.TypeIdentity) ||
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
        bool teleported = ai.Ai2 != 0f && ai.Ai3 != 0f;
        if (teleported)
        {
            x = ai.Ai2 * 16f - definition.Width / 2 + 8f;
            y = ai.Ai3 * 16f - definition.Height;
            vx = vy = 0f;
            ai = ai with { Ai2 = 0f, Ai3 = 0f };
        }

        float timer = ai.Ai0 == 0f ? 501f : ai.Ai0 + 1f;
        float attack = ai.Ai1;
        bool withinFiringRange = VanillaNpcGlobalFiringDistance.Contains(
            x + definition.Width * .5f, y + definition.Height * .5f, target.CenterX, target.CenterY);
        bool armsAttack = npc.TypeIdentity == VanillaNpcIds.RuneWizard
            ? timer is 75f or 150f or 225f or 300f or 375f or 450f && withinFiringRange
            : timer is 100f or 200f or 300f && (npc.TypeIdentity == VanillaNpcIds.GoblinSorcerer || withinFiringRange);
        if (armsAttack)
        {
            attack = 30f;
        }
        if (timer >= 650f) timer = 1f;
        if (attack > 0f) attack--;
        NpcSimulationState simulation = npc.Simulation with
        {
            DirectionX = directionX,
            DirectionY = directionY,
            JustHit = false
        };
        if (npc.TypeIdentity == VanillaNpcIds.RuneWizard)
        {
            int alpha = npc.Simulation.JustHit ? 0 : Math.Min(255, npc.Simulation.Alpha + 1);
            if (teleported) alpha = 255;
            simulation = simulation with { Alpha = alpha };
        }
        next = new(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai with { Ai0 = timer, Ai1 = attack },
            simulation);
        return true;
    }

    public NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot committed, VanillaNpcBehaviorContext context,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (environment is null || !IsSupported(before.TypeIdentity) ||
            committed.TypeIdentity != before.TypeIdentity || AdvanceTimer(before.Ai.Ai0) < 650f ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost ||
            !VanillaNpcDefinitionCatalog.TryGet(committed.TypeIdentity, out VanillaNpcDefinition definition))
            return CompleteRuneBlast(in before, in committed, context, mutations);

        NpcSnapshot current = committed;
        if (AdvanceTimer(before.Ai.Ai0) >= 650f)
        {
            if (!environment.TryFindTeleportSpot(current.PositionX + definition.Width * .5f,
                    current.PositionY + definition.Height * .5f, (int)target.CenterX / 16, (int)target.CenterY / 16,
                    context.Candidates, random, out int x, out int y) ||
                !mutations.TryUpdateAi(in current, current.Ai with { Ai1 = 19f, Ai2 = x, Ai3 = y }, out current))
            {
                return committed;
            }
        }
        return CompleteRuneBlast(in before, in current, context, mutations);
    }

    private static float AdvanceTimer(float timer) => timer == 0f ? 501f : timer + 1f;

    private NpcSnapshot CompleteRuneBlast(in NpcSnapshot before, in NpcSnapshot committed,
        VanillaNpcBehaviorContext context, INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity != VanillaNpcIds.RuneWizard || before.Ai.Ai1 != 26f || committed.Ai.Ai1 != 25f ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost)
            return committed;

        float centerX = committed.PositionX + 9f;
        float centerY = committed.PositionY + 20f;
        float velocityX = target.CenterX - centerX + random.NextInt32(-10, 11);
        float velocityY = target.CenterY - centerY + random.NextInt32(-10, 11);
        float scale = 10f / MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
        var intent = new NpcAiProjectileIntent(VanillaProjectileIds.RuneBlast, centerX - 7f, centerY - 7f,
            velocityX * scale, velocityY * scale, 40, 0f) { TimeLeftOverride = 300 };
        mutations.TrySpawnProjectile(in committed, in intent, out _);
        return committed;
    }

    private static bool IsSupported(NpcTypeId type) =>
        type == VanillaNpcIds.GoblinSorcerer || type == VanillaNpcIds.Tim || type == VanillaNpcIds.RuneWizard;
}
