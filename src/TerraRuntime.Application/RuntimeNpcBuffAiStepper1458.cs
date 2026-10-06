using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

// NPC.UpdateNPC_BuffApplyVFX precedes IdleSounds/AI. Retain the genuine dedicated-server
// offer and its source random draws, even though Dust.NewDust returns the nonphysical server slot.
internal sealed class RuntimeNpcBuffAiStepper1458(INpcAiStateStepper inner,
    RuntimeNpcBuffStatus1458 status, IVanillaNpcRandom random, bool goodWorld = false,
    bool retainedSlimeStatuses = false, RuntimeNpcDebuffDeath1458? debuffDeath = null,
    RuntimeNpcBuffPhase1458? buffPhase = null) :
    INpcAiStateStepper, INpcAiStateStepperWrapper, INpcAiPrepass1458
{
    private readonly NpcRuntimeTownCombatRandom1458 visualRandom = new(random);
    public INpcAiStateStepper InnerStepper => inner;
    public bool TryRunPrepass(in NpcSnapshot npc, out bool consumed)
    {
        consumed = false;
        if (retainedSlimeStatuses && npc.TypeIdentity == VanillaNpcIds.Zombie && buffPhase is not null)
        {
            // Shimmer changes the outer NPC phase before this source buff/AI slice can be admitted.
            if (npc.Simulation.ShimmerTransparency != 0f ||
                npc.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer) return false;
            if (random is not SystemVanillaNpcRandom source ||
                !status.TryPlan(in npc, out var zombiePlan, goodWorld, allowLethal: true)) return false;
            if (zombiePlan.LifeAfter > 0)
                return buffPhase(in npc, in zombiePlan, status, source.SourceRandom);
            if (debuffDeath is null || !debuffDeath(in npc, in zombiePlan, status, source.SourceRandom)) return false;
            consumed = true;
            return true;
        }
        if (!retainedSlimeStatuses || npc.TypeIdentity != VanillaNpcIds.BlueSlime &&
            npc.TypeIdentity != VanillaNpcIds.LavaSlime) return true;
        if (!status.TryPlan(in npc, out var plan, goodWorld, allowLethal: true)) return false;
        if (plan.LifeAfter > 0) return true;
        if (random is not SystemVanillaNpcRandom trusted || debuffDeath is null ||
            !debuffDeath(in npc, in plan, status, trusted.SourceRandom)) return false;
        // The retained death plan also owns the following BuffApplyVFX draws, even for an inactive actor.
        consumed = true;
        return true;
    }
    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) || definition.DefinitionOnly)
        { next = default; return false; }
        if (retainedSlimeStatuses && (npc.TypeIdentity == VanillaNpcIds.BlueSlime ||
            npc.TypeIdentity == VanillaNpcIds.LavaSlime ||
            npc.TypeIdentity == VanillaNpcIds.Zombie && buffPhase is not null))
            return inner.TryStepState(in npc, out next);
        // Town's complete phase owns its own visual offer in the same random transaction as AI/contact.
        if (definition.AiStyle != VanillaNpcAiStyles.Town)
        {
            if (!status.TryPlan(in npc, out var plan)) { next = default; return false; }
            var offer = RuntimeNpcBuffStatus1458.PlanVisualOffers(in plan, visualRandom, goodWorld);
            if (!inner.TryStepState(in npc, out next) || !status.IsCurrent(in plan) ||
                !status.Commit(in plan, in npc)) { next = default; return false; }
            status.ObserveVisualOffer(npc.Handle, in offer);
            status.PublishExpired(in plan);
            return true;
        }
        return inner.TryStepState(in npc, out next);
    }
}
