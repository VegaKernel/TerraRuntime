using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileNpcCombatPass
{
    private readonly ProjectileGeneration[] ordinaryArrowHandledGenerations;
    private bool ordinaryArrowSimulationActive;

    // The enclosing projectile simulation owns one census. Retain its markers until the next census
    // so the following outer NPC pass cannot strike a committed or refused actor a second time.
    internal bool BeginOrdinaryArrowSimulation()
    {
        if (ordinaryArrowSimulationActive) return false;
        Array.Clear(ordinaryArrowHandledGenerations);
        ordinaryArrowSimulationActive = true;
        return true;
    }

    internal void EndOrdinaryArrowSimulation() => ordinaryArrowSimulationActive = false;

    internal bool OwnsOrdinaryArrowWorld(RuntimeProjectileStore store, RuntimeNpcStore npcStore,
        TerraRuntime.Core.VanillaUnifiedRandom1458 random, PlayerAuthority playerStore) =>
        ReferenceEquals(projectiles, store) && ReferenceEquals(npcs, npcStore) &&
        usesSourceRandom && ReferenceEquals(sourceRandom, random) && ReferenceEquals(players, playerStore);

    internal bool CanMarkOrdinaryArrowHandled(in ProjectileSnapshot projectile) =>
        ordinaryArrowSimulationActive && projectile.Type.Value is 4 or 5 && projectile.Handle.IsAssigned &&
        projectile.Handle.Slot < RuntimeProjectileStore.VanillaPhysicalSlotCount &&
        projectile.Handle.Slot < ordinaryArrowHandledGenerations.Length;

    internal bool MarkOrdinaryArrowHandled(in ProjectileSnapshot projectile)
    {
        if (!CanMarkOrdinaryArrowHandled(in projectile)) return false;
        ordinaryArrowHandledGenerations[projectile.Handle.Slot] = projectile.Handle.Generation;
        return true;
    }

    internal bool IsOrdinaryArrowHandled(in ProjectileSnapshot projectile) =>
        projectile.Type.Value is 4 or 5 && projectile.Handle.IsAssigned &&
        projectile.Handle.Slot < ordinaryArrowHandledGenerations.Length &&
        ordinaryArrowHandledGenerations[projectile.Handle.Slot] == projectile.Handle.Generation;
}
