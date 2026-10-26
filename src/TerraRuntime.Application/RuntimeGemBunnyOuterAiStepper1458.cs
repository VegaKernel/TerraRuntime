using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

// NPC.UpdateNPC's AI007 lower-world strike precedes buff/AI/gravity processing.
internal sealed class RuntimeGemBunnyOuterAiStepper1458(INpcAiStateStepper inner,
    RuntimeGemBunnyLifecycle1458 lifecycle, int worldHeightTiles, TerraRuntime.Core.VanillaUnifiedRandom1458 random)
    : INpcAiStateStepper, INpcAiStateStepperWrapper, INpcAiPrepass1458
{
    private readonly INpcAiPrepass1458? ordinary = NpcAiStateStepperComposition.FindCapability<INpcAiPrepass1458>(inner);
    public INpcAiStateStepper InnerStepper => inner;
    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) => inner.TryStepState(in npc, out next);
    public bool TryRunPrepass(in NpcSnapshot npc, out bool consumed)
    {
        consumed = false;
        if (VanillaGemBunnyAi1458.IsSupported(npc.Type) && !npc.Simulation.DontTakeDamage &&
            npc.Simulation.Immortal == false &&
            VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) &&
            definition.TryResolveHitbox(npc.Simulation, out var body) &&
            npc.PositionY > worldHeightTiles * 16f - 640f + body.Height)
        {
            consumed = true;
            return lifecycle.TryCommitLowerWorldDeath(in npc, random);
        }
        return ordinary is null || ordinary.TryRunPrepass(in npc, out consumed);
    }
}
