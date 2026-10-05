using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

// NPC.UpdateNPC_BuffApplyVFX precedes IdleSounds/AI. Retain the genuine dedicated-server
// offer and its source random draws, even though Dust.NewDust returns the nonphysical server slot.
internal sealed class RuntimeNpcBuffAiStepper1458(INpcAiStateStepper inner,
    RuntimeNpcBuffStatus1458 status, IVanillaNpcRandom random, bool goodWorld = false) : INpcAiStateStepper, INpcAiStateStepperWrapper
{
    private readonly NpcRuntimeTownCombatRandom1458 visualRandom = new(random);
    public INpcAiStateStepper InnerStepper => inner;
    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition))
        { next = default; return false; }
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
