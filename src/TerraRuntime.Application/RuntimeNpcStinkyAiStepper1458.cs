using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

// NPC.UpdateNPC_BuffApplyVFX precedes IdleSounds/AI. Retain the genuine dedicated-server
// offer and its source random draws, even though Dust.NewDust returns the nonphysical server slot.
internal sealed class RuntimeNpcStinkyAiStepper1458(INpcAiStateStepper inner,
    RuntimeNpcStinkyStatus1458 status, IVanillaNpcRandom random, bool goodWorld = false) : INpcAiStateStepper, INpcAiStateStepperWrapper
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
            if (!status.TryGetStinky(npc.Handle, out bool stinky)) { next = default; return false; }
            if (!stinky) return inner.TryStepState(in npc, out next);
            bool canDisplay = !goodWorld || npc.TypeIdentity != VanillaNpcIds.Golem &&
                npc.TypeIdentity != VanillaNpcIds.GolemHead && npc.TypeIdentity != VanillaNpcIds.GolemFistLeft &&
                npc.TypeIdentity != VanillaNpcIds.GolemFistRight;
            if (canDisplay)
            {
                var offer = RuntimeNpcStinkyStatus1458.PlanVisualOffer(visualRandom);
                status.ObserveVisualOffer(npc.Handle, in offer);
            }
        }
        return inner.TryStepState(in npc, out next);
    }
}
