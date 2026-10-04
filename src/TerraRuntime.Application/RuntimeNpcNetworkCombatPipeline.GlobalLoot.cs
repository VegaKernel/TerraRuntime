using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeNpcGlobalLootWorldFacts1458(
    int Width, int Height, float? Difficulty, bool HardMode, bool RemixWorld,
    bool? Halloween, bool? Christmas, double? RockLayer, double? WorldSurface,
    bool? SkeletronDowned, bool? AnyMechDowned);

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly RuntimeNpcGlobalLootWorldFacts1458? globalLootWorld;
    private readonly Func<RuntimeNpcGlobalLootWorldFacts1458>? globalLootWorldSource;
    private RuntimeNpcGlobalLootWorldFacts1458? plannedGlobalLootWorld;

    private RuntimeNpcGlobalLootWorldFacts1458? CaptureGlobalLootWorld() =>
        globalLootWorldSource is null ? globalLootWorld : globalLootWorldSource();

    private bool TryExecuteGlobalLoot(in NpcSnapshot npc)
    {
        // Detached callers without a represented world retain their explicitly partial loot lane.
        // Live world composition supplies the full source context; missing conditional facts fail admission.
        if (plannedGlobalLootWorld is not { } world) return true;
        if (npc.Simulation.MoneyValue is not { } value ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(npc.Simulation, out var body) ||
            !VanillaNpcSourceMetadata1458.TryGet(npc.TypeIdentity, out bool boss, out _)) return false;
        bool closestKnown = TryFindClosestPlayer(in npc, out var closest);
        var context = new VanillaNpcGlobalLootContext1458(
            npc.TypeIdentity, value,
            npc.Simulation.DamageOverride ?? npc.Simulation.BaseDamage ?? definition.Damage,
            npc.Simulation.DefenseOverride ?? npc.Simulation.BaseDefense ?? definition.Defense,
            npc.Simulation.LifeMax, npc.Simulation.Friendly, boss, npc.Target,
            npc.PositionX, npc.PositionY, body.Width, world.Difficulty, world.Width, world.Height,
            world.HardMode || DeathProgression.IsCompleted(VanillaWorldProgressionId.Hardmode), world.RemixWorld)
        {
            Halloween = world.Halloween,
            Christmas = world.Christmas,
            Zones = closestKnown ? closest.Zones : null,
            RockLayer = world.RockLayer,
            WorldSurface = world.WorldSurface,
            SkeletronDowned = world.SkeletronDowned == true || DeathProgression.IsCompleted(VanillaWorldProgressionId.Skeletron)
                ? true : world.SkeletronDowned,
            AnyMechDowned = world.AnyMechDowned == true || DeathProgression.IsCompleted(VanillaWorldProgressionId.Destroyer) ||
                DeathProgression.IsCompleted(VanillaWorldProgressionId.Twins) || DeathProgression.IsCompleted(VanillaWorldProgressionId.SkeletronPrime) ||
                DeathProgression.IsCompleted(VanillaWorldProgressionId.AnyMechanicalBoss)
                ? true : world.AnyMechDowned
        };
        if (!VanillaNpcGlobalLoot1458.TryValidateContext(in context)) return false;
        var origin = ResolveNpcLootOrigin(in npc, in definition);
        for (int index = 0; index < VanillaNpcGlobalLoot1458.RuleCount; index++)
        {
            if (!VanillaNpcGlobalLoot1458.TryEvaluateRule(index, in context, random, out bool dropped, out var drop) ||
                (dropped && !lootDelivery.TryWorld(in origin, in drop, random))) return false;
        }
        return true;
    }
}
