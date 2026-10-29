using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectilePlayerCombatPass
{
    private readonly ProjectileGeneration[] groundCombatHandledGenerations;
    private bool groundCombatSimulationActive;
    private bool groundCombatActorOwnerBound;
    private long groundCombatTick;

    internal bool OwnsGroundCombatWorld(RuntimeProjectileStore store, RuntimeNpcStore npcStore,
        VanillaUnifiedRandom1458 random, PlayerAuthority playerStore, IProjectileTerminationCommitSink termination) =>
        ReferenceEquals(projectiles, store) && ReferenceEquals(npcs, npcStore) &&
        ReferenceEquals(sourceRandom, random) && ReferenceEquals(players, playerStore) &&
        ReferenceEquals(terminationEffects, termination);

    internal void BindGroundCombatActorOwner()
    {
        if (groundCombatSimulationActive || groundCombatActorOwnerBound)
            throw new InvalidOperationException("Projectile combat already has an actor owner.");
        groundCombatActorOwnerBound = true;
    }

    internal bool BeginGroundCombatSimulation()
    {
        if (groundCombatSimulationActive) return false;
        groundCombatTick = tickProvider();
        Array.Clear(groundCombatHandledGenerations);
        groundCombatSimulationActive = true;
        return true;
    }

    internal void EndGroundCombatSimulation() => groundCombatSimulationActive = false;

    // Invoke this external clock BEFORE the final callback-free owner comparisons.
    internal bool IsGroundCombatClockCurrent => groundCombatSimulationActive && tickProvider() == groundCombatTick;

    internal bool MarkGroundCombatHandled(in ProjectileSnapshot projectile)
    {
        if (!groundCombatSimulationActive || !VanillaGroundCombatPlayerHit1458.IsSupported(projectile.Type) ||
            !projectile.Handle.IsAssigned || projectile.Handle.Slot >= RuntimeProjectileStore.VanillaPhysicalSlotCount ||
            projectile.Handle.Slot >= groundCombatHandledGenerations.Length) return false;
        groundCombatHandledGenerations[projectile.Handle.Slot] = projectile.Handle.Generation;
        return true;
    }

    private bool IsGroundCombatHandled(in ProjectileSnapshot projectile) =>
        VanillaGroundCombatPlayerHit1458.IsSupported(projectile.Type) && projectile.Handle.IsAssigned &&
        projectile.Handle.Slot < groundCombatHandledGenerations.Length &&
        groundCombatHandledGenerations[projectile.Handle.Slot] == projectile.Handle.Generation;

    internal bool TryPrepareGroundCombatContacts(in ProjectileSnapshot initial, NpcHandle source,
        VanillaProjectileWorldStateStepper.OrdinaryArrowTerrainCapture terrain,
        out PlayerAuthority.GroundCombatHitPreparation? humans,
        out ServerPlayerAuthority.GroundCombatHitPreparation? servers)
    {
        humans = null; servers = null;
        if (!groundCombatSimulationActive ||
            !players.TryPrepareGroundCombatHits(in initial, source, groundCombatTick,
                expertMode, masterMode, goodWorld, terrain, out humans) || humans is null) return false;
        if (serverPlayers is not null &&
            (!serverPlayers.TryPrepareGroundCombatHits(in initial, source, groundCombatTick,
                expertMode, masterMode, goodWorld, terrain, out servers) || servers is null)) return false;
        for (int slot = 0; slot < byte.MaxValue; slot++)
            if (humans.HasPlayer(slot) && servers?.HasPlayer(slot) == true) return false;
        return true;
    }

    internal void AdoptGroundCombatMetrics(PlayerAuthority.GroundCombatHitPreparation humans,
        ServerPlayerAuthority.GroundCombatHitPreparation? servers, bool consumed)
    {
        HostileCommittedHits += humans.CommittedHits + (servers?.CommittedHits ?? 0);
        HostileKills += humans.Kills + (servers?.Kills ?? 0);
        HostileGodModeAvoidances += humans.GodModeAvoidances + (servers?.GodModeAvoidances ?? 0);
        if (consumed) ConsumedProjectiles++;
    }
}
