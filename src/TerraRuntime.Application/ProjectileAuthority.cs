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
    RuntimePlayerInventoryMutation? InventoryMutation,
    int ManaCost,
    VanillaLaunchSpeedEnvelope LaunchSpeedEnvelope,
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
    private readonly RuntimeProjectileExplosionQueue explosions;
    private readonly IProjectileStateStepper? stepper;
    private readonly RuntimeNpcProjectileReflectionPass reflections;
    private readonly RuntimeProjectileReplicationRegistry? replication;
    private readonly Func<long> tickProvider;
    private readonly long[] lastTrustedClientUseTick = new long[byte.MaxValue + 1];
    private readonly PlayerSessionGeneration[] trustedClientUseGenerations = new PlayerSessionGeneration[byte.MaxValue + 1];
    private readonly ProjectileSnapshot[] controlledProjectileBuffer;
    private const byte ControlUseItemFlag = 1 << 5;

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
        explosions = new RuntimeProjectileExplosionQueue(projectiles.Capacity);
        executor = new RuntimeProjectileStateExecutor(projectiles, terminationSink: explosions);
        this.stepper = stepper;
        reflections = new RuntimeNpcProjectileReflectionPass(npcs, projectiles, playerSnapshots, goodWorld: goodWorld);
        this.replication = replication;
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        controlledProjectileBuffer = new ProjectileSnapshot[projectiles.Capacity];
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

        explosions.Reset();
        SynchronizeControlledProjectileReleaseInputs();
        LastTick = executor.Tick(stepper);
        return true;
    }

    public ReadOnlySpan<RuntimeProjectileExplosionEvent> PendingExplosions => explosions.Events;

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
    public long AcceptedTrustedSteeringInputs { get; private set; }
    public long RejectedTrustedClientDestroys { get; private set; }
    public long PromotedClientProjectileSpawns { get; private set; }
    public long RejectedClientProjectileProvenance { get; private set; }
    public long RelayedUnknownDestroys { get; private set; }
    public ProjectileStateTickSummary LastTick { get; private set; }

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
            weaponItem.IsEmpty)
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        if (VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeapon(
                weaponItem.ItemType,
                out VanillaChanneledMagicProjectileWeaponCombatDefinition channeledMagicWeapon))
        {
            return TryResolveStrictChanneledMagicProjectileSpawn(
                connection, in player, in weaponItem, in channeledMagicWeapon, in packet, out authoritative);
        }

        if (VanillaProjectileWeaponCombatCatalog.TryGetStandaloneWeapon(
                weaponItem.ItemType,
                out VanillaStandaloneProjectileWeaponCombatDefinition standaloneWeapon))
        {
            return TryResolveStrictStandaloneProjectileSpawn(
                connection, in player, selectedSlot, in weaponItem, in standaloneWeapon, in packet, out authoritative);
        }

        if (!VanillaProjectileWeaponCombatCatalog.TryGetWeapon(
                weaponItem.ItemType,
                out VanillaProjectileWeaponCombatDefinition weapon))
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        if (!VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(weaponItem.Prefix, out VanillaCombatPrefixModifiers prefix) ||
            !players.TryCaptureCombatSnapshot(connection, out VanillaPlayerCombatSnapshot attackerCombat))
        {
            // An unsupported prefix/equipment combination may still be a legitimate vanilla shot, but it cannot cross
            // the CombatTrusted boundary until its exact source formula is imported.
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        Span<RuntimePlayerInventoryItem> inventory =
            stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        if (!players.TryCopyInventory(connection, inventory))
            return ClientProjectileProvenanceResolveResult.Rejected;

        int ammoSlot = FindFirstAmmo(
            weapon.AmmoFamily,
            inventory,
            VanillaPlayerItemSlotCatalog.CoinSlotStart,
            VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive,
            out RuntimePlayerInventoryItem ammoItem,
            out VanillaProjectileAmmoCombatDefinition ammo);
        if (ammoSlot == -1)
            ammoSlot = FindFirstAmmo(
                weapon.AmmoFamily,
                inventory,
                VanillaPlayerItemSlotCatalog.AmmoSlotStart,
                VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive,
                out ammoItem,
                out ammo);
        if (ammoSlot == -1)
            ammoSlot = FindFirstAmmo(
                weapon.AmmoFamily,
                inventory,
                VanillaPlayerItemSlotCatalog.MainInventoryStart,
                VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive,
                out ammoItem,
                out ammo);
        if (ammoSlot < 0)
            return ClientProjectileProvenanceResolveResult.NotApplicable;

        if (!VanillaProjectileWeaponCombatCatalog.TryResolveProjectileType(in weapon, in ammo, out ProjectileTypeId expectedProjectileType))
            return ClientProjectileProvenanceResolveResult.NotApplicable;

        int expectedDamage = VanillaProjectileWeaponCombatCatalog.ResolveDamage(
            in weapon, in ammo, in prefix, in attackerCombat);
        float expectedKnockBack = VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(
            in weapon, in ammo, in prefix, in attackerCombat);
        VanillaLaunchSpeedEnvelope speedEnvelope = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(
            in weapon, in ammo, in prefix, in attackerCombat);
        if (!speedEnvelope.IsValid)
            return ClientProjectileProvenanceResolveResult.NotApplicable;

        float packetSpeedSquared = packet.VelocityX * packet.VelocityX + packet.VelocityY * packet.VelocityY;
        if (!(packetSpeedSquared > 0f) || !float.IsFinite(packetSpeedSquared))
            return RejectProvenance();
        float packetSpeed = MathF.Sqrt(packetSpeedSquared);

        float playerCx = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float playerCy = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = packet.PositionX - playerCx;
        float dy = packet.PositionY - playerCy;
        float maximumDistance = weapon.ImpossibleSpawnCenterDistancePixels;
        int authoritativeUseTime = Math.Max(1, (int)Math.Round(weapon.UseTimeTicks * prefix.SpeedMultiplier));
        long tick = tickProvider();
        float knockBackTolerance = MathF.Max(0.001f, MathF.Abs(expectedKnockBack) * 0.00001f);

        if (packet.ProjectileType != expectedProjectileType.Value ||
            packet.Damage != expectedDamage ||
            packet.OriginalDamage != 0 ||
            MathF.Abs(packet.KnockBack - expectedKnockBack) > knockBackTolerance ||
            !speedEnvelope.ContainsMagnitude(packetSpeed) ||
            MathF.Abs(packet.Ai0) > 0.001f ||
            MathF.Abs(packet.Ai1) > 0.001f ||
            MathF.Abs(packet.Ai2) > 0.001f ||
            dx * dx + dy * dy > maximumDistance * maximumDistance ||
            IsTrustedClientUseOnCooldown(connection.Player, tick, authoritativeUseTime))
        {
            return RejectProvenance();
        }

        // Preserve the client's aim direction. Only magnitude is validated/canonicalized; there is deliberately no
        // generic angular envelope because source-specific spread belongs to individual weapon rules.
        float canonicalSpeed = speedEnvelope.CanonicalMagnitude;
        float velocityScale = canonicalSpeed / packetSpeed;
        var state = new ProjectileStateUpdate(
            expectedProjectileType,
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

        int weaponConservationRoll = weapon.WeaponAmmoConservationOneIn > 0
            ? Random.Shared.Next(weapon.WeaponAmmoConservationOneIn)
            : -1;
        int quiverConservationRoll = weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow && attackerCombat.MagicQuiver
            ? Random.Shared.Next(5)
            : -1;
        bool conserveAmmo = VanillaProjectileWeaponCombatCatalog.ShouldConserveAmmo(
            in weapon, in ammo, in attackerCombat, weaponConservationRoll, quiverConservationRoll);
        RuntimePlayerInventoryItem remainingAmmo = conserveAmmo
            ? ammoItem
            : ammoItem.Stack == 1
                ? default
                : ammoItem with { Stack = checked((short)(ammoItem.Stack - 1)) };
        authoritative = new AuthoritativeClientProjectileSpawn(
            state,
            new RuntimePlayerInventoryMutation(checked((short)ammoSlot), remainingAmmo),
            ManaCost: 0,
            speedEnvelope,
            authoritativeUseTime);
        return ClientProjectileProvenanceResolveResult.Accepted;

        static ClientProjectileProvenanceResolveResult RejectProvenance() =>
            ClientProjectileProvenanceResolveResult.Rejected;
    }

    private ClientProjectileProvenanceResolveResult TryResolveStrictChanneledMagicProjectileSpawn(
        ConnectionHandle connection,
        in PlayerStateSnapshot player,
        in RuntimePlayerInventoryItem weaponItem,
        in VanillaChanneledMagicProjectileWeaponCombatDefinition weapon,
        in TerrariaProjectileUpdateState packet,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        // Exact magic prefix formulas are deliberately not guessed in this slice. Prefix-free items plus the
        // source-backed equipment snapshot are enough to make damage/mana/cadence authoritative.
        if (weaponItem.Prefix != VanillaPrefixIds.None || weaponItem.Stack <= 0 ||
            !player.HasMana || player.Mana < weapon.ManaCost ||
            !players.TryCaptureCombatSnapshot(connection, out VanillaPlayerCombatSnapshot attackerCombat))
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        int expectedDamage = VanillaProjectileWeaponCombatCatalog.ResolveChanneledMagicDamage(in weapon, in attackerCombat);
        float expectedKnockBack = weapon.BaseKnockBack;
        VanillaLaunchSpeedEnvelope speedEnvelope =
            VanillaProjectileWeaponCombatCatalog.ResolveChanneledMagicLaunchSpeedEnvelope(in weapon);
        float speedSquared = packet.VelocityX * packet.VelocityX + packet.VelocityY * packet.VelocityY;
        if (!(speedSquared > 0f) || !float.IsFinite(speedSquared) || !speedEnvelope.IsValid)
            return ClientProjectileProvenanceResolveResult.Rejected;
        float packetSpeed = MathF.Sqrt(speedSquared);

        float playerCx = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float playerCy = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = packet.PositionX - playerCx;
        float dy = packet.PositionY - playerCy;
        long tick = tickProvider();
        float knockBackTolerance = MathF.Max(0.001f, MathF.Abs(expectedKnockBack) * 0.00001f);
        if (packet.ProjectileType != weapon.ProjectileType.Value ||
            packet.Damage != expectedDamage || packet.OriginalDamage != 0 ||
            MathF.Abs(packet.KnockBack - expectedKnockBack) > knockBackTolerance ||
            !speedEnvelope.ContainsMagnitude(packetSpeed) ||
            MathF.Abs(packet.Ai0) > 0.001f || MathF.Abs(packet.Ai1) > 0.001f || MathF.Abs(packet.Ai2) > 0.001f ||
            dx * dx + dy * dy > weapon.ImpossibleSpawnCenterDistancePixels * weapon.ImpossibleSpawnCenterDistancePixels ||
            IsTrustedClientUseOnCooldown(connection.Player, tick, weapon.UseTimeTicks))
        {
            return ClientProjectileProvenanceResolveResult.Rejected;
        }

        float canonicalSpeed = speedEnvelope.CanonicalMagnitude;
        float scale = canonicalSpeed / packetSpeed;
        var state = new ProjectileStateUpdate(
            weapon.ProjectileType,
            connection.Player.Slot.Value,
            packet.PositionX,
            packet.PositionY,
            packet.VelocityX * scale,
            packet.VelocityY * scale,
            default,
            BannerIdToRespondTo: 0,
            Damage: checked((short)expectedDamage),
            KnockBack: expectedKnockBack,
            OriginalDamage: 0);
        authoritative = new AuthoritativeClientProjectileSpawn(
            state,
            InventoryMutation: null,
            ManaCost: weapon.ManaCost,
            speedEnvelope,
            weapon.UseTimeTicks);
        return ClientProjectileProvenanceResolveResult.Accepted;
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

    private static ProjectileStateUpdate SnapshotToUpdate(in ProjectileSnapshot current) => new(
        current.Type,
        current.Spawner,
        current.PositionX,
        current.PositionY,
        current.VelocityX,
        current.VelocityY,
        current.Ai,
        current.BannerIdToRespondTo,
        current.Damage,
        current.KnockBack,
        current.OriginalDamage);

    private ClientProjectileProvenanceResolveResult TryResolveStrictStandaloneProjectileSpawn(
        ConnectionHandle connection,
        in PlayerStateSnapshot player,
        int selectedSlot,
        in RuntimePlayerInventoryItem weaponItem,
        in VanillaStandaloneProjectileWeaponCombatDefinition weapon,
        in TerrariaProjectileUpdateState packet,
        out AuthoritativeClientProjectileSpawn authoritative)
    {
        authoritative = default;
        if (weaponItem.Prefix != VanillaPrefixIds.None ||
            weaponItem.Stack <= 0 ||
            !players.TryCaptureCombatSnapshot(connection, out VanillaPlayerCombatSnapshot attackerCombat))
        {
            return ClientProjectileProvenanceResolveResult.NotApplicable;
        }

        int expectedDamage = VanillaProjectileWeaponCombatCatalog.ResolveStandaloneDamage(in weapon, in attackerCombat);
        float expectedKnockBack = weapon.BaseKnockBack;
        VanillaLaunchSpeedEnvelope speedEnvelope =
            VanillaProjectileWeaponCombatCatalog.ResolveStandaloneLaunchSpeedEnvelope(in weapon);
        float speedSquared = packet.VelocityX * packet.VelocityX + packet.VelocityY * packet.VelocityY;
        if (!(speedSquared > 0f) || !float.IsFinite(speedSquared) || !speedEnvelope.IsValid)
            return ClientProjectileProvenanceResolveResult.Rejected;
        float packetSpeed = MathF.Sqrt(speedSquared);

        float playerCx = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float playerCy = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = packet.PositionX - playerCx;
        float dy = packet.PositionY - playerCy;
        long tick = tickProvider();
        float knockBackTolerance = MathF.Max(0.001f, MathF.Abs(expectedKnockBack) * 0.00001f);
        if (packet.ProjectileType != weapon.ProjectileType.Value ||
            packet.Damage != expectedDamage ||
            packet.OriginalDamage != 0 ||
            MathF.Abs(packet.KnockBack - expectedKnockBack) > knockBackTolerance ||
            !speedEnvelope.ContainsMagnitude(packetSpeed) ||
            MathF.Abs(packet.Ai0) > 0.001f ||
            MathF.Abs(packet.Ai1) > 0.001f ||
            MathF.Abs(packet.Ai2) > 0.001f ||
            dx * dx + dy * dy > weapon.ImpossibleSpawnCenterDistancePixels * weapon.ImpossibleSpawnCenterDistancePixels ||
            IsTrustedClientUseOnCooldown(connection.Player, tick, weapon.UseTimeTicks))
        {
            return ClientProjectileProvenanceResolveResult.Rejected;
        }

        float canonicalSpeed = speedEnvelope.CanonicalMagnitude;
        float velocityScale = canonicalSpeed / packetSpeed;
        var state = new ProjectileStateUpdate(
            weapon.ProjectileType,
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

        RuntimePlayerInventoryItem remaining = !weapon.Consumable
            ? weaponItem
            : weaponItem.Stack == 1
                ? default
                : weaponItem with { Stack = checked((short)(weaponItem.Stack - 1)) };
        authoritative = new AuthoritativeClientProjectileSpawn(
            state,
            new RuntimePlayerInventoryMutation(checked((short)selectedSlot), remaining),
            ManaCost: 0,
            speedEnvelope,
            weapon.UseTimeTicks);
        return ClientProjectileProvenanceResolveResult.Accepted;
    }

    private static int FindFirstAmmo(
        VanillaProjectileAmmoFamily family,
        ReadOnlySpan<RuntimePlayerInventoryItem> inventory,
        int start,
        int endExclusive,
        out RuntimePlayerInventoryItem ammoItem,
        out VanillaProjectileAmmoCombatDefinition ammo)
    {
        ammoItem = default;
        ammo = default;
        for (int slot = start; slot < endExclusive; slot++)
        {
            RuntimePlayerInventoryItem candidate = inventory[slot];
            if (candidate.IsEmpty || !VanillaProjectileWeaponCombatCatalog.IsAmmoType(family, candidate.ItemType))
                continue;

            // Terraria ammo itself is not prefixable here. Unknown compatible ammo is recognized by family but remains
            // fail-closed rather than allowing a later supported stack to leapfrog PickAmmo's first valid candidate.
            if (candidate.Prefix != VanillaPrefixIds.None ||
                !VanillaProjectileWeaponCombatCatalog.TryGetAmmo(family, candidate.ItemType, out ammo))
            {
                return -2;
            }

            ammoItem = candidate;
            return slot;
        }
        return -1;
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
