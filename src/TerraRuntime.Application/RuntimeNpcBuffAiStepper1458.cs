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
    bool retainedSlimeStatuses = false, RuntimeSlimeDebuffDeath1458? debuffDeath = null) :
    INpcAiStateStepper, INpcAiStateStepperWrapper, INpcAiPrepass1458
{
    private readonly NpcRuntimeTownCombatRandom1458 visualRandom = new(random);
    public INpcAiStateStepper InnerStepper => inner;
    public bool TryRunPrepass(in NpcSnapshot npc, out bool consumed)
    {
        consumed = false;
        if (!retainedSlimeStatuses || npc.TypeIdentity != VanillaNpcIds.BlueSlime &&
            npc.TypeIdentity != VanillaNpcIds.LavaSlime) return true;
        if (!status.TryPlan(in npc, out var plan, goodWorld, allowLethal: true)) return false;
        if (plan.LifeAfter > 0) return true;
        if (random is not SystemVanillaNpcRandom trusted || debuffDeath is null ||
            !debuffDeath(in npc, in plan, status, trusted.SourceRandom)) return false;
        // Source DOT completes its 9999 strike/death effects before BuffApplyVFX, even for an inactive actor.
        _ = RuntimeNpcBuffStatus1458.PlanVisualOffers(in plan, visualRandom, goodWorld);
        consumed = true;
        return true;
    }
    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition))
        { next = default; return false; }
        if (retainedSlimeStatuses && (npc.TypeIdentity == VanillaNpcIds.BlueSlime ||
            npc.TypeIdentity == VanillaNpcIds.LavaSlime))
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
