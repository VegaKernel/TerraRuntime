using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcCombat1458
{
    // AI007 derives live combat values anew from creation baselines and owned world progression.
    internal NpcSnapshot PlanVitals(in NpcSnapshot source)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition)) return source;
        int baseDefense = source.Simulation.BaseDefense ?? definition.Defense;
        if (source.TypeIdentity == VanillaNpcIds.TaxCollector &&
            townNpcs.TryGet((short)source.Handle.Slot, out WorldTownNpc resident) &&
            string.Equals(resident.GivenName, "Andrew", StringComparison.Ordinal)) baseDefense = 200;
        int defense = baseDefense;
        int lifeMax = source.Simulation.BaseLifeMax ?? source.Simulation.LifeMax;
        if (world.CombatBookWasUsed) { lifeMax += 250; defense += 8; }
        if (world.CombatBookVolumeTwoWasUsed) { lifeMax += 250; defense += 8; }
        if (IsComplete(VanillaWorldProgressionId.KingSlime)) defense += 2;
        if (IsComplete(VanillaWorldProgressionId.EyeOfCthulhu)) defense += 2;
        if (IsComplete(VanillaWorldProgressionId.Deerclops)) defense += 3;
        if (IsComplete(VanillaWorldProgressionId.EvilBoss)) defense += 3;
        if (IsComplete(VanillaWorldProgressionId.Skeletron)) defense += 3;
        if (IsComplete(VanillaWorldProgressionId.QueenBee)) defense += 3;
        if (IsComplete(VanillaWorldProgressionId.Hardmode)) defense += 12;
        if (IsComplete(VanillaWorldProgressionId.QueenSlime)) defense += 6;
        if (IsComplete(VanillaWorldProgressionId.Destroyer)) defense += 6;
        if (IsComplete(VanillaWorldProgressionId.Twins)) defense += 6;
        if (IsComplete(VanillaWorldProgressionId.SkeletronPrime)) defense += 6;
        if (IsComplete(VanillaWorldProgressionId.Plantera)) defense += 8;
        if (IsComplete(VanillaWorldProgressionId.EmpressOfLight)) defense += 8;
        if (IsComplete(VanillaWorldProgressionId.DukeFishron)) defense += 8;
        if (IsComplete(VanillaWorldProgressionId.Golem)) defense += 8;
        if (IsComplete(VanillaWorldProgressionId.LunaticCultist)) defense += 20;
        int life = source.Simulation.Life == source.Simulation.LifeMax && lifeMax > source.Simulation.LifeMax
            ? lifeMax : Math.Min(source.Simulation.Life, lifeMax);
        bool infectedDryad = world.InfectedSeed && source.TypeIdentity == VanillaNpcIds.Dryad;
        if (infectedDryad) { life = lifeMax; baseDefense = 99999; }
        return source with { Simulation = source.Simulation with {
            BaseDefense = baseDefense, DefenseOverride = defense, LifeMax = lifeMax, Life = life,
            DontTakeDamage = false,
            Immortal = infectedDryad ? true : source.Simulation.Immortal } };
    }
}
