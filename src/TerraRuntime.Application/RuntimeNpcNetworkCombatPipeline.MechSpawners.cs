using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly VanillaMechBossSpawnersContext1458 mechanicalLootBaseline;

    private bool TryExecuteMechSpawnersLoot(in NpcSnapshot npc)
    {
        if (npc.Simulation.MoneyValue is not float value) return false;
        var context = mechanicalLootBaseline with
        {
            NpcValue = value,
            HardMode = mechanicalLootBaseline.HardMode || DeathProgression.IsCompleted(VanillaWorldProgressionId.Hardmode),
            DestroyerDowned = mechanicalLootBaseline.DestroyerDowned || DeathProgression.IsCompleted(VanillaWorldProgressionId.Destroyer),
            TwinsDowned = mechanicalLootBaseline.TwinsDowned || DeathProgression.IsCompleted(VanillaWorldProgressionId.Twins),
            PrimeDowned = mechanicalLootBaseline.PrimeDowned || DeathProgression.IsCompleted(VanillaWorldProgressionId.SkeletronPrime),
            IsInSimulation = false
        };
        if (!VanillaMechBossSpawners1458.TryEvaluate(in context, random, out bool dropped, out var drop)) return false;
        if (!dropped) return true;
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition)) return false;
        var origin = ResolveNpcLootOrigin(in npc, in definition);
        // Materialize inline: Item.NewItem's velocity draws precede the next global and NPC-specific rules.
        return lootDelivery.TryWorld(in origin, in drop, random);
    }
}
