using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Owns the projectile store, simulation, client commit validation and lifecycle metrics for one world.
/// It is invoked only by the enclosing authoritative world loop.
/// </summary>
internal sealed class ProjectileAuthority
{
    private readonly RuntimeProjectileStore projectiles;
    private readonly PlayerAuthority players;
    private readonly RuntimeProjectileStateExecutor executor;
    private readonly IProjectileStateStepper? stepper;
    private readonly RuntimeNpcProjectileReflectionPass reflections;
    private readonly RuntimeProjectileReplicationRegistry? replication;

    public ProjectileAuthority(
        RuntimeProjectileStore projectiles,
        PlayerAuthority players,
        RuntimeNpcStore npcs,
        IRuntimePlayerSlotSnapshotLookup playerSnapshots,
        IProjectileStateStepper? stepper,
        RuntimeProjectileReplicationRegistry? replication)
    {
        this.projectiles = projectiles;
        this.players = players;
        executor = new RuntimeProjectileStateExecutor(projectiles);
        this.stepper = stepper;
        reflections = new RuntimeNpcProjectileReflectionPass(npcs, projectiles, playerSnapshots);
        this.replication = replication;
    }

    public bool TryApply(RuntimeCommand command)
    {
        switch (command)
        {
            case ProjectileSpawnRuntimeCommand spawn:
                ApplySpawn(spawn);
                return true;
            case ProjectileUpdateRuntimeCommand update:
                ApplyUpdate(update);
                return true;
            case ProjectileDespawnRuntimeCommand despawn:
                ApplyDespawn(despawn);
                return true;
            case ClientProjectileUpdateRuntimeCommand update:
                ApplyClientUpdate(update);
                return true;
            case ClientProjectileDestroyRuntimeCommand destroy:
                ApplyClientDestroy(destroy);
                return true;
            default:
                return false;
        }
    }

    public bool TryTickState()
    {
        if (stepper is null)
            return false;

        LastTick = executor.Tick(stepper);
        return true;
    }

    public void ApplyReflections() => AppliedReflections += reflections.Tick();

    public bool TryCapture(ProjectileHandle projectile, out ProjectileSnapshot snapshot) =>
        projectiles.TryGet(projectile, out snapshot);

    public long AppliedSpawns { get; private set; }
    public long RejectedSpawns { get; private set; }
    public long AppliedUpdates { get; private set; }
    public long RejectedUpdates { get; private set; }
    public long AppliedDespawns { get; private set; }
    public long RejectedDespawns { get; private set; }
    public long AppliedReflections { get; private set; }
    public long RejectedClientUpdates { get; private set; }
    public long RejectedClientDestroys { get; private set; }
    public long RelayedUnknownDestroys { get; private set; }
    public ProjectileStateTickSummary LastTick { get; private set; }

    private void ApplySpawn(ProjectileSpawnRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        if (projectiles.TrySpawn(command.Slot, in state, out ProjectileSnapshot snapshot))
        {
            AppliedSpawns++;
            command.Completion?.TrySetResult(snapshot);
            return;
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

    private void ApplyClientUpdate(ClientProjectileUpdateRuntimeCommand command)
    {
        TerrariaProjectileUpdateState packet = command.State;
        if (replication is null ||
            !players.IsCurrent(command.Connection) ||
            packet.Key.Spawner != command.Connection.Player.Slot.Value ||
            !TryConvertClientProjectileUpdate(in packet, out ProjectileStateUpdate update))
        {
            RejectedClientUpdates++;
            return;
        }

        RuntimeProjectileWireIdentityRegistry identities = replication.WireIdentities;
        RuntimeProjectileClientCommitContext clientCommits = replication.ClientCommitContext;
        TerrariaProjectileKeyState key = packet.Key;

        if (identities.TryResolve(in key, out ProjectileHandle projectile))
        {
            using IDisposable scope = clientCommits.Enter(command.Connection.Source, in key);
            if (projectiles.TryUpdate(projectile, in update, out _))
            {
                AppliedUpdates++;
                return;
            }

            RejectedUpdates++;
            RejectedClientUpdates++;
            return;
        }

        using (clientCommits.Enter(command.Connection.Source, in key))
        {
            if (projectiles.TrySpawnVanilla(in update, out _))
            {
                AppliedSpawns++;
                return;
            }
        }

        RejectedSpawns++;
        RejectedClientUpdates++;
    }

    private void ApplyClientDestroy(ClientProjectileDestroyRuntimeCommand command)
    {
        TerrariaProjectileDestroyState packet = command.State;
        if (replication is null ||
            !packet.IsValid ||
            !players.IsCurrent(command.Connection))
        {
            RejectedClientDestroys++;
            return;
        }

        RuntimeProjectileWireIdentityRegistry identities = replication.WireIdentities;
        TerrariaProjectileKeyState key = packet.Key;
        if (!identities.TryResolve(in key, out ProjectileHandle projectile))
        {
            if (replication.TryRelayUnresolvedDestroy(command.Connection.Source, in packet))
            {
                RelayedUnknownDestroys++;
                return;
            }

            RejectedClientDestroys++;
            return;
        }

        if (!projectiles.TryGet(projectile, out ProjectileSnapshot current))
        {
            identities.TryUnbind(projectile, out _);
            if (replication.TryRelayUnresolvedDestroy(command.Connection.Source, in packet))
            {
                RelayedUnknownDestroys++;
                return;
            }

            RejectedClientDestroys++;
            return;
        }

        if (current.Spawner != command.Connection.Player.Slot.Value)
        {
            RejectedClientDestroys++;
            return;
        }

        using (replication.ClientCommitContext.Enter(command.Connection.Source, in key))
        {
            if (projectiles.TryDespawnAt(projectile, packet.PositionX, packet.PositionY, out _))
            {
                AppliedDespawns++;
                return;
            }
        }

        RejectedDespawns++;
        RejectedClientDestroys++;
    }

    private static bool TryConvertClientProjectileUpdate(
        in TerrariaProjectileUpdateState packet,
        out ProjectileStateUpdate update)
    {
        if (!packet.IsValid ||
            !VanillaProjectileIds.TryCreate(packet.ProjectileType, out ProjectileTypeId type) ||
            !VanillaProjectileLifecycleFacts.IsDefinedLiveType(type) ||
            VanillaProjectileFacts.IsHostile(type))
        {
            update = default;
            return false;
        }

        update = new ProjectileStateUpdate(
            type,
            packet.Key.Spawner,
            packet.PositionX,
            packet.PositionY,
            packet.VelocityX,
            packet.VelocityY,
            new ProjectileAiState(packet.Ai0, packet.Ai1, packet.Ai2),
            packet.BannerIdToRespondTo,
            packet.Damage,
            packet.KnockBack,
            packet.OriginalDamage);
        return true;
    }
}
