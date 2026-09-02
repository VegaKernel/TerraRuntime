using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState
{
    private void ApplyProjectileSpawn(ProjectileSpawnRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        if (_projectiles.TrySpawn(command.Slot, in state, out ProjectileSnapshot snapshot))
        {
            AppliedProjectileSpawns++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        RejectedProjectileSpawns++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyProjectileUpdate(ProjectileUpdateRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        if (_projectiles.TryUpdate(command.Projectile, in state, out _))
        {
            AppliedProjectileUpdates++;
            return;
        }

        RejectedProjectileUpdates++;
    }

    private void ApplyProjectileDespawn(ProjectileDespawnRuntimeCommand command)
    {
        if (_projectiles.TryDespawn(command.Projectile, out _))
        {
            AppliedProjectileDespawns++;
            return;
        }

        RejectedProjectileDespawns++;
    }

    private void ApplyClientProjectileUpdate(ClientProjectileUpdateRuntimeCommand command)
    {
        TerrariaProjectileUpdateState packet = command.State;
        if (_projectileReplication is null ||
            !_players.IsCurrent(command.Connection) ||
            packet.Key.Spawner != command.Connection.Player.Slot.Value ||
            !TryConvertClientProjectileUpdate(in packet, out ProjectileStateUpdate update))
        {
            RejectedClientProjectileUpdates++;
            return;
        }

        RuntimeProjectileWireIdentityRegistry identities = _projectileReplication.WireIdentities;
        RuntimeProjectileClientCommitContext clientCommits = _projectileReplication.ClientCommitContext;
        TerrariaProjectileKeyState key = packet.Key;

        if (identities.TryResolve(in key, out ProjectileHandle projectile))
        {
            using IDisposable scope = clientCommits.Enter(command.Connection.Source, in key);
            if (_projectiles.TryUpdate(projectile, in update, out _))
            {
                AppliedProjectileUpdates++;
                return;
            }

            RejectedProjectileUpdates++;
            RejectedClientProjectileUpdates++;
            return;
        }

        using (clientCommits.Enter(command.Connection.Source, in key))
        {
            if (_projectiles.TrySpawnVanilla(in update, out _))
            {
                AppliedProjectileSpawns++;
                return;
            }
        }

        RejectedProjectileSpawns++;
        RejectedClientProjectileUpdates++;
    }

    private void ApplyClientProjectileDestroy(ClientProjectileDestroyRuntimeCommand command)
    {
        TerrariaProjectileDestroyState packet = command.State;
        if (_projectileReplication is null ||
            !packet.IsValid ||
            !_players.IsCurrent(command.Connection))
        {
            RejectedClientProjectileDestroys++;
            return;
        }

        RuntimeProjectileWireIdentityRegistry identities = _projectileReplication.WireIdentities;
        TerrariaProjectileKeyState key = packet.Key;
        if (!identities.TryResolve(in key, out ProjectileHandle projectile))
        {
            if (_projectileReplication.TryRelayUnresolvedDestroy(command.Connection.Source, in packet))
            {
                RelayedUnknownProjectileDestroys++;
                return;
            }

            RejectedClientProjectileDestroys++;
            return;
        }

        if (!_projectiles.TryGet(projectile, out ProjectileSnapshot current))
        {
            identities.TryUnbind(projectile, out _);
            if (_projectileReplication.TryRelayUnresolvedDestroy(command.Connection.Source, in packet))
            {
                RelayedUnknownProjectileDestroys++;
                return;
            }

            RejectedClientProjectileDestroys++;
            return;
        }

        if (current.Spawner != command.Connection.Player.Slot.Value)
        {
            RejectedClientProjectileDestroys++;
            return;
        }

        using (_projectileReplication.ClientCommitContext.Enter(command.Connection.Source, in key))
        {
            if (_projectiles.TryDespawnAt(projectile, packet.PositionX, packet.PositionY, out _))
            {
                AppliedProjectileDespawns++;
                return;
            }
        }

        RejectedProjectileDespawns++;
        RejectedClientProjectileDestroys++;
    }

    private static bool TryConvertClientProjectileUpdate(
        in TerrariaProjectileUpdateState packet,
        out ProjectileStateUpdate update)
    {
        if (!packet.IsValid ||
            !VanillaProjectileIds.TryCreate(packet.ProjectileType, out ProjectileTypeId type) ||
            !VanillaProjectileIds.IsLiveWireType(type) ||
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
