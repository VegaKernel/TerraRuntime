using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    public bool TryTickState(RuntimeProjectileNpcCombatPass? ordinaryArrowCombat = null)
    {
        if (activeOrdinaryArrowCombat is not null) return false;
        if (stepper is null)
        {
            ExpirePendingBulletVolleys();
            return false;
        }
        if (ordinaryArrowCombat is not null &&
            !ordinaryArrowCombat.OwnsOrdinaryArrowWorld(projectiles, npcs, projectileRandom, players))
            throw new ArgumentException("Arrow simulation must use this world's stores and source RNG.", nameof(ordinaryArrowCombat));
        if (ordinaryArrowCombat is not null && !ordinaryArrowCombat.BeginOrdinaryArrowSimulation()) return false;
        activeOrdinaryArrowCombat = ordinaryArrowCombat;
        try
        {
            ExpirePendingBulletVolleys();
            explosions.Reset();
            tileExplosions.Reset();
            childSpawns.Reset();
            liveChildSpawns.Reset();
            SynchronizeControlledProjectileReleaseInputs();
            LastTick = ordinaryArrowCombat is not null && stepper is VanillaProjectileWorldStateStepper
                ? executor.Tick(stepper, ordinaryArrowActor ??= TrySimulateOrdinaryArrowActor)
                : executor.Tick(stepper);
            replication?.AdvanceNetSpamBudget();
            FallingBlocks.ForgetDisplaced(projectiles);
            ApplyPendingLiveChildSpawns();
            ApplyPendingChildSpawns();
            return true;
        }
        finally
        {
            activeOrdinaryArrowCombat = null;
            ordinaryArrowCombat?.EndOrdinaryArrowSimulation();
        }
    }

    private void ApplyPendingLiveChildSpawns()
    {
        Span<NpcAiProjectileIntent> lightningIntents =
            stackalloc NpcAiProjectileIntent[RuntimeCultistLightningOrbLiveChildSpawn1458.MaximumTargets];
        foreach (RuntimeProjectileLiveChildSpawnEvent child in liveChildSpawns.Events)
        {
            // The simulation sink proves the parent transition committed. Re-read the exact handle here so a
            // later slot reuse can neither publish a stale child nor borrow another NPC generation's provenance.
            if (!projectiles.TryGet(child.Parent, out ProjectileSnapshot parent) ||
                parent.Type != child.InitialProjectile.Type ||
                !projectiles.TryGetServerNpcSource(child.Parent, out NpcHandle sourceNpc) ||
                !sourceNpc.IsAssigned ||
                !npcs.TryGet(sourceNpc, out _))
            {
                continue;
            }

            if (child.Kind == RuntimeProjectileLiveChildKind.TornadoSegment)
            {
                if (!RuntimeTornadoLiveChildSpawn1458.TryCreateIntents(
                        in child,
                        out NpcAiProjectileIntent projectileIntent,
                        out bool hasNpcIntent,
                        out NpcAiSpawnIntent npcIntent))
                {
                    continue;
                }

                RuntimeNpcProjectileIntentApplier.TryApply(projectiles, sourceNpc, in projectileIntent, out _);
                if (hasNpcIntent)
                    npcs.TrySpawnIntent(in npcIntent, out _);
                continue;
            }

            if (child.Kind == RuntimeProjectileLiveChildKind.CultistIceMist &&
                RuntimeCultistIceMistLiveChildSpawn1458.TryCreateIntent(in child, out NpcAiProjectileIntent mistIntent))
            {
                RuntimeNpcProjectileIntentApplier.TryApply(projectiles, sourceNpc, in mistIntent, out _);
                continue;
            }

            if (child.Kind == RuntimeProjectileLiveChildKind.CultistLightningOrb && hostilePlayerTargets is not null)
            {
                lightningIntents.Clear();
                int intentCount = RuntimeCultistLightningOrbLiveChildSpawn1458.CopyIntents(
                    in child,
                    hostilePlayerTargets,
                    projectileRandom,
                    lightningIntents);
                for (int i = 0; i < intentCount; i++)
                    RuntimeNpcProjectileIntentApplier.TryApply(projectiles, sourceNpc, in lightningIntents[i], out _);
            }
        }
    }


    private void ApplyPendingChildSpawns()
    {
        foreach (RuntimeProjectileChildSpawnEvent child in childSpawns.Events)
        {
            if (!RuntimeSharknadoChildSpawn1458.TryCreateIntent(
                    in child,
                    worldTiles,
                    expertMode,
                    out NpcAiProjectileIntent intent))
            {
                continue;
            }

            RuntimeNpcProjectileIntentApplier.TryApply(projectiles, child.SourceNpc, in intent, out _);
        }
    }


    private void ApplySpawn(ProjectileSpawnRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        PlayerHandle trustedOwner = default;
        if (VanillaProjectileOwnership.IsPlayerOwned(state.Spawner) &&
            playerSnapshots.TryGetPlayer(new PlayerSlotId(state.Spawner), out PlayerStateSnapshot owner) &&
            owner.Player.IsAssigned && owner.Player.Slot.Value == state.Spawner)
        {
            trustedOwner = owner.Player;
        }

        if (projectiles.TrySpawn(command.Slot, in state, out ProjectileSnapshot snapshot))
        {
            if (projectiles.TryMarkCombatTrusted(snapshot.Handle, trustedOwner))
            {
                AppliedSpawns++;
                command.Completion?.TrySetResult(snapshot);
                return;
            }

            // Trust metadata is part of the server-spawn contract. If it cannot be attached, do not leave a live
            // generation that callers may mistakenly treat as authoritative merely because the spawn command returned.
            projectiles.TryDespawn(snapshot.Handle, out _);
        }

        RejectedSpawns++;
        command.Completion?.TrySetResult(null);
    }


    private void ApplyUpdate(ProjectileUpdateRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        if (projectiles.TryUpdate(command.Projectile, in state, out _))
        {
            AppliedUpdates++;
            return;
        }

        RejectedUpdates++;
    }


    private void ApplyDespawn(ProjectileDespawnRuntimeCommand command)
    {
        if (projectiles.TryDespawn(command.Projectile, out _))
        {
            AppliedDespawns++;
            return;
        }

        RejectedDespawns++;
    }
}
