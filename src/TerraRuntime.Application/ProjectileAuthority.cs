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

internal enum ClientProjectileProvenanceResolveResult : byte
{
    NotApplicable = 0,
    Accepted = 1,
    Rejected = 2
}

internal readonly record struct AuthoritativeClientProjectileSpawn(
    ProjectileStateUpdate State,
    RuntimePlayerInventoryMutation AmmoMutation,
    int UseTimeTicks);

/// <summary>
/// Owns the projectile store, simulation, client commit validation and lifecycle metrics for one world.
/// It is invoked only by the enclosing authoritative world loop.
/// </summary>
internal sealed class ProjectileAuthority
{
    private readonly RuntimeProjectileStore projectiles;
    private readonly PlayerAuthority players;
    private readonly IRuntimePlayerSlotSnapshotLookup playerSnapshots;
    private readonly RuntimeProjectileStateExecutor executor;
    private readonly IProjectileStateStepper? stepper;
    private readonly RuntimeNpcProjectileReflectionPass reflections;
    private readonly RuntimeProjectileReplicationRegistry? replication;
    private readonly Func<long> tickProvider;
    private readonly long[] lastTrustedClientUseTick = new long[byte.MaxValue + 1];
    private readonly PlayerSessionGeneration[] trustedClientUseGenerations = new PlayerSessionGeneration[byte.MaxValue + 1];

    public ProjectileAuthority(
        RuntimeProjectileStore projectiles,
        PlayerAuthority players,
        RuntimeNpcStore npcs,
        IRuntimePlayerSlotSnapshotLookup playerSnapshots,
        IProjectileStateStepper? stepper,
        RuntimeProjectileReplicationRegistry? replication,
        Func<long> tickProvider,
        bool goodWorld = false)
    {
        this.projectiles = projectiles;
        this.players = players;
        this.playerSnapshots = playerSnapshots ?? throw new ArgumentNullException(nameof(playerSnapshots));
        executor = new RuntimeProjectileStateExecutor(projectiles);
        this.stepper = stepper;
        reflections = new RuntimeNpcProjectileReflectionPass(npcs, projectiles, playerSnapshots, goodWorld: goodWorld);
        this.replication = replication;
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        Array.Fill(lastTrustedClientUseTick, long.MinValue);
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
    public long RejectedTrustedClientUpdates { get; private set; }
    public long RejectedTrustedClientDestroys { get; private set; }
    public long PromotedClientProjectileSpawns { get; private set; }
    public long RejectedClientProjectileProvenance { get; private set; }
    public long RelayedUnknownDestroys { get; private set; }
    public ProjectileStateTickSummary LastTick { get; private set; }

    private void ApplySpawn(ProjectileSpawnRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        PlayerHandle trustedOwner = default;
        if (VanillaProjectileOwnership.IsPlayerOwned(state.Spawner))
        {
            if (!playerSnapshots.TryGetPlayer(new PlayerSlotId(state.Spawner), out PlayerStateSnapshot owner) ||
                !owner.Player.IsAssigned || owner.Player.Slot.Value != state.Spawner)
            {
                RejectedSpawns++;
                command.Completion?.TrySetResult(null);
                return;
            }
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

            // A trusted generation has crossed the authoritative spawn boundary. From that point onward its
            // position, velocity, ai, lifetime and termination are runtime-owned. Packet 27 may still arrive
            // from the vanilla client, but it is diagnostic input only and must not overwrite simulation state.
            if (projectiles.IsCombatTrusted(projectile))
            {
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
                RuntimePlayerInventoryMutation ammoMutation = authoritative.AmmoMutation;
                if (projectiles.TrySpawnVanilla(in authoritativeState, out ProjectileSnapshot trusted) &&
                    projectiles.TryMarkCombatTrusted(trusted.Handle, command.Connection.Player) &&
                    players.TryCommitInventoryMutation(command.Connection, in ammoMutation))
                {
                    MarkTrustedClientUse(command.Connection.Player, tickProvider());
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

    private ClientProjectileProvenanceResolveResult TryResolveStrictClientProjectileSpawn(
        ConnectionHandle connection,
        in TerrariaProjectileUpdateState packet,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        if (!players.TryCapture(connection.Player, out PlayerStateSnapshot player) || player.IsDead)
            return ClientProjectileProvenanceResolveResult.Rejected;

        int selectedSlot = player.SelectedItem;
        if (!VanillaPlayerItemSlotCatalog.IsInventorySlot((short)selectedSlot) ||
            !players.TryGetInventoryItem(connection, selectedSlot, out RuntimePlayerInventoryItem weaponItem) ||
            weaponItem.IsEmpty ||
            !VanillaProjectileWeaponCombatCatalog.TryGetWeapon(weaponItem.ItemType, out VanillaProjectileWeaponCombatDefinition weapon))
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        // Prefix and equipment modifiers are not guessed. Until they are imported into the same calculator this
        // exact source remains compatibility-only and any packet-27 generation stays combat-untrusted.
        if (weaponItem.Prefix != VanillaPrefixIds.None ||
            (players.TryCaptureEquipment(connection, out PlayerEquipmentCommitRequest[] equipment) && equipment.Length != 0))
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        Span<RuntimePlayerInventoryItem> inventory =
            stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        if (!players.TryCopyInventory(connection, inventory))
            return ClientProjectileProvenanceResolveResult.Rejected;

        // PickAmmo's default order scans coin slots first, then ammo slots, then the main inventory. This initial
        // strict slice only promotes when the earlier coin cells are empty and the first non-empty ammo cell is one
        // of the two imported Arrow-ammo definitions. Ambiguous/unknown cells fall back untrusted instead of making
        // up ammo classification data that has not been source-imported yet.
        for (int slot = VanillaPlayerItemSlotCatalog.CoinSlotStart;
             slot < VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive;
             slot++)
        {
            if (!inventory[slot].IsEmpty)
                return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        int ammoSlot = -1;
        RuntimePlayerInventoryItem ammoItem = default;
        VanillaProjectileAmmoCombatDefinition ammo = default;
        for (int slot = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
             slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive;
             slot++)
        {
            RuntimePlayerInventoryItem candidate = inventory[slot];
            if (candidate.IsEmpty)
                continue;
            if (!VanillaProjectileWeaponCombatCatalog.TryGetArrowAmmo(candidate.ItemType, out ammo) ||
                candidate.Prefix != VanillaPrefixIds.None)
            {
                return ClientProjectileProvenanceResolveResult.NotApplicable;
            }

            ammoSlot = slot;
            ammoItem = candidate;
            break;
        }
        if (ammoSlot < 0)
            return ClientProjectileProvenanceResolveResult.NotApplicable;

        int expectedDamage = checked(weapon.BaseDamage + ammo.Damage);
        float expectedKnockBack = weapon.BaseKnockBack + ammo.KnockBack;
        float expectedSpeed = weapon.BaseShootSpeed + ammo.ShootSpeed;
        float packetSpeedSquared = packet.VelocityX * packet.VelocityX + packet.VelocityY * packet.VelocityY;
        if (!(packetSpeedSquared > 0f) || !float.IsFinite(packetSpeedSquared))
            return RejectProvenance();
        float packetSpeed = MathF.Sqrt(packetSpeedSquared);

        float playerCx = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float playerCy = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = packet.PositionX - playerCx;
        float dy = packet.PositionY - playerCy;
        float maximumDistance = weapon.ImpossibleSpawnCenterDistancePixels;
        long tick = tickProvider();

        if (packet.ProjectileType != ammo.ProjectileType.Value ||
            packet.Damage != expectedDamage ||
            packet.OriginalDamage != 0 ||
            MathF.Abs(packet.KnockBack - expectedKnockBack) > 0.01f ||
            MathF.Abs(packetSpeed - expectedSpeed) > 0.05f ||
            MathF.Abs(packet.Ai0) > 0.001f ||
            MathF.Abs(packet.Ai1) > 0.001f ||
            MathF.Abs(packet.Ai2) > 0.001f ||
            dx * dx + dy * dy > maximumDistance * maximumDistance ||
            IsTrustedClientUseOnCooldown(connection.Player, tick, weapon.UseTimeTicks))
        {
            return RejectProvenance();
        }

        float velocityScale = expectedSpeed / packetSpeed;
        var state = new ProjectileStateUpdate(
            ammo.ProjectileType,
            connection.Player.Slot.Value,
            packet.PositionX,
            packet.PositionY,
            packet.VelocityX * velocityScale,
            packet.VelocityY * velocityScale,
            default,
            BannerIdToRespondTo: 0,
            Damage: checked((short)expectedDamage),
            KnockBack: expectedKnockBack,
            OriginalDamage: 0);

        RuntimePlayerInventoryItem remainingAmmo = ammoItem.Stack == 1
            ? default
            : ammoItem with { Stack = checked((short)(ammoItem.Stack - 1)) };
        authoritative = new AuthoritativeClientProjectileSpawn(
            state,
            new RuntimePlayerInventoryMutation(checked((short)ammoSlot), remainingAmmo),
            weapon.UseTimeTicks);
        return ClientProjectileProvenanceResolveResult.Accepted;

        static ClientProjectileProvenanceResolveResult RejectProvenance() =>
            ClientProjectileProvenanceResolveResult.Rejected;
    }

    private bool IsTrustedClientUseOnCooldown(PlayerHandle player, long tick, int useTimeTicks)
    {
        int slot = player.Slot.Value;
        if (trustedClientUseGenerations[slot] != player.Generation)
        {
            trustedClientUseGenerations[slot] = player.Generation;
            lastTrustedClientUseTick[slot] = long.MinValue;
            return false;
        }

        long previous = lastTrustedClientUseTick[slot];
        return previous != long.MinValue && tick - previous < useTimeTicks;
    }

    private void MarkTrustedClientUse(PlayerHandle player, long tick)
    {
        int slot = player.Slot.Value;
        trustedClientUseGenerations[slot] = player.Generation;
        lastTrustedClientUseTick[slot] = tick;
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
