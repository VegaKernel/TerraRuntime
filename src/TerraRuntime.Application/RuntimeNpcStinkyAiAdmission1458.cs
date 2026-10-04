using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
namespace TerraRuntime.Application;
// Source UpdateNPC_BuffApplyVFX precedes non-town AI. Until that outer visual/RNG stage is owned,
// a current generation's Stinky flag cannot silently enter an otherwise admitted partial AI path.
internal sealed class RuntimeNpcStinkyAiAdmission1458(INpcAiStateStepper inner,
    RuntimeNpcStinkyStatus1458 status) : INpcAiStateStepper, INpcAiStateStepperWrapper
{
    public INpcAiStateStepper InnerStepper => inner;
    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (status.TryGetStinky(npc.Handle, out bool stinky) && stinky &&
            (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) ||
             definition.AiStyle != VanillaNpcAiStyles.Town))
        { next = default; return false; }
        return inner.TryStepState(in npc, out next);
    }
}
