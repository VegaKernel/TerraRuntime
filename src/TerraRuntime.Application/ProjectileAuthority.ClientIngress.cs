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
            if (!projectiles.TryGet(projectile, out ProjectileSnapshot current) ||
                current.Type != update.Type ||
                current.Spawner != update.Spawner ||
                current.Damage != update.Damage ||
                current.OriginalDamage != update.OriginalDamage ||
                current.KnockBack != update.KnockBack)
            {
                RejectedClientUpdates++;
                return;
            }

            // A trusted generation has crossed the authoritative spawn boundary. Position, velocity, lifetime and
            // termination remain runtime-owned. aiStyle-9 controlled missiles are the deliberate exception for input:
            // vanilla packet 27 ai[0]/ai[1] carries MouseWorld intent. We accept only that bounded intent and never
            // copy the packet's position/velocity into the trusted generation.
            if (projectiles.IsCombatTrusted(projectile))
            {
                if (VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeaponForProjectile(
                        current.Type, out VanillaChanneledMagicProjectileWeaponCombatDefinition controlledWeapon) &&
                    TryApplyTrustedControlledMagicInput(command.Connection, projectile, in current, in controlledWeapon, in packet))
                {
                    AcceptedTrustedSteeringInputs++;
                    AppliedUpdates++;
                    return;
                }

                RejectedTrustedClientUpdates++;
                RejectedClientUpdates++;
                return;
            }

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

        ClientProjectileProvenanceResolveResult provenance =
            TryResolveStrictClientProjectileSpawn(command.Connection, in packet, out AuthoritativeClientProjectileSpawn authoritative);
        if (provenance == ClientProjectileProvenanceResolveResult.Rejected)
        {
            RejectedClientProjectileProvenance++;
            RejectedClientUpdates++;
            return;
        }

        using (clientCommits.Enter(command.Connection.Source, in key))
        {
            if (provenance == ClientProjectileProvenanceResolveResult.Accepted)
            {
                ProjectileStateUpdate authoritativeState = authoritative.State;
                if (projectiles.TrySpawnVanilla(in authoritativeState, out ProjectileSnapshot trusted) &&
                    projectiles.TryMarkCombatTrusted(trusted.Handle, command.Connection.Player) &&
                    TryCommitAuthoritativeProjectileUse(command.Connection, in authoritative))
                {
                    trustedClientUseCadence.MarkUse(command.Connection.Player, tickProvider());
                    AppliedSpawns++;
                    PromotedClientProjectileSpawns++;
                    return;
                }

                if (trusted.Handle.IsAssigned)
                    projectiles.TryDespawn(trusted.Handle, out _);
                RejectedSpawns++;
                RejectedClientProjectileProvenance++;
                RejectedClientUpdates++;
                return;
            }

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

        // Trusted projectiles die only through authoritative lifetime/collision/penetration/behavior paths.
        // Accepting packet 29 here would let an owning client erase a server-owned combat generation early.
        if (projectiles.IsCombatTrusted(projectile))
        {
            RejectedTrustedClientDestroys++;
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


    private bool TryApplyTrustedControlledMagicInput(
        ConnectionHandle connection,
        ProjectileHandle projectile,
        in ProjectileSnapshot current,
        in VanillaChanneledMagicProjectileWeaponCombatDefinition weapon,
        in TerrariaProjectileUpdateState packet)
    {
        if (!projectiles.TryGetCombatTrustedOwner(projectile, out PlayerHandle trustedOwner) ||
            trustedOwner != connection.Player || current.Spawner != trustedOwner.Slot.Value ||
            MathF.Abs(packet.Ai2) > 0.001f ||
            !players.TryCapture(trustedOwner, out PlayerStateSnapshot player))
        {
            return false;
        }

        bool channel = (player.ControlFlags & ControlUseItemFlag) != 0;
        if (!channel || !IsHeldControlledMagicWeapon(trustedOwner, in player, in weapon))
            return TryReleaseControlledMagicProjectile(projectile, in current, in player);

        if (!(packet.Ai0 > 0f) || !(packet.Ai1 > 0f) ||
            !players.TryClampVanillaReachableAim(trustedOwner, packet.Ai0, packet.Ai1, out float targetX, out float targetY))
        {
            return false;
        }

        ProjectileStateUpdate update = SnapshotToUpdate(in current) with
        {
            Ai = new ProjectileAiState(targetX, targetY, current.Ai.Ai2)
        };
        return projectiles.TryUpdate(projectile, in update, out _);
    }


    private void SynchronizeControlledProjectileReleaseInputs()
    {
        int count = projectiles.CopyActive(controlledProjectileBuffer);
        for (int i = 0; i < count; i++)
        {
            ProjectileSnapshot current = controlledProjectileBuffer[i];
            if (!projectiles.IsCombatTrusted(current.Handle) || current.Ai.Ai0 < 0f ||
                !VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeaponForProjectile(
                    current.Type, out VanillaChanneledMagicProjectileWeaponCombatDefinition weapon) ||
                !projectiles.TryGetCombatTrustedOwner(current.Handle, out PlayerHandle trustedOwner) ||
                !players.TryCapture(trustedOwner, out PlayerStateSnapshot player))
            {
                continue;
            }

            bool channel = (player.ControlFlags & ControlUseItemFlag) != 0;
            if (channel && IsHeldControlledMagicWeapon(trustedOwner, in player, in weapon))
                continue;
            _ = TryReleaseControlledMagicProjectile(current.Handle, in current, in player);
        }
    }


    private bool IsHeldControlledMagicWeapon(
        PlayerHandle owner,
        in PlayerStateSnapshot player,
        in VanillaChanneledMagicProjectileWeaponCombatDefinition weapon)
    {
        int selectedSlot = player.SelectedItem;
        return VanillaPlayerItemSlotCatalog.IsInventorySlot((short)selectedSlot) &&
            players.TryGetInventoryItem(owner, selectedSlot, out RuntimePlayerInventoryItem held) &&
            !held.IsEmpty && held.ItemType == weapon.Type;
    }


    private bool TryReleaseControlledMagicProjectile(
        ProjectileHandle projectile,
        in ProjectileSnapshot current,
        in PlayerStateSnapshot player)
    {
        float vx = current.VelocityX;
        float vy = current.VelocityY;
        float speed = MathF.Sqrt(vx * vx + vy * vy);
        if (!(speed >= 2f) || !float.IsFinite(speed))
        {
            float projectileCenterX = current.PositionX + 16f;
            float projectileCenterY = current.PositionY + 16f;
            float playerCenterX = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
            float playerCenterY = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
            vx = projectileCenterX - playerCenterX;
            vy = projectileCenterY - playerCenterY;
            speed = MathF.Sqrt(vx * vx + vy * vy);
        }
        if (!(speed > 0f) || !float.IsFinite(speed))
        {
            vx = 0f;
            vy = 32f;
        }
        else
        {
            vx = vx / speed * 32f;
            vy = vy / speed * 32f;
        }

        ProjectileStateUpdate update = SnapshotToUpdate(in current) with
        {
            VelocityX = vx,
            VelocityY = vy,
            Ai = new ProjectileAiState(-1f, -1f, current.Ai.Ai2)
        };
        return projectiles.TryUpdate(projectile, in update, out _);
    }


    private bool TryCommitAuthoritativeProjectileUse(
        ConnectionHandle connection,
        in AuthoritativeClientProjectileSpawn authoritative)
    {
        if (authoritative.ManaCost > 0)
            return authoritative.InventoryMutation is null && players.TryConsumeMana(connection, authoritative.ManaCost);
        if (authoritative.InventoryMutation is RuntimePlayerInventoryMutation mutation)
            return players.TryCommitInventoryMutation(connection, in mutation);
        return true;
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
